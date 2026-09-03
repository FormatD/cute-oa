#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
run_id="$$"
network_name="cute-oa-clamav-smoke-${run_id}"
clamav_container="cute-oa-clamav-smoke-${run_id}"
api_container="cute-oa-api-clamav-smoke-${run_id}"
files_volume="cute-oa-files-clamav-smoke-${run_id}"
clamav_volume="cute-oa-db-clamav-smoke-${run_id}"
test_database="oa_clamav_test_${run_id}"
clamav_image="${OA_CLAMAV_IMAGE:-clamav/clamav:1.4}"
clamav_platform="${OA_CLAMAV_PLATFORM:-linux/amd64}"

cleanup() {
  cd "$project_root"
  docker rm --force "$api_container" "$clamav_container" >/dev/null 2>&1 || true
  docker network rm "$network_name" >/dev/null 2>&1 || true
  docker volume rm --force "$files_volume" "$clamav_volume" >/dev/null 2>&1 || true
  docker compose exec -T postgres psql -U oa -d postgres -Atc "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$test_database' AND pid <> pg_backend_pid();" >/dev/null 2>&1 || true
  docker compose exec -T postgres dropdb -U oa --if-exists "$test_database" >/dev/null 2>&1 || true
}
trap cleanup EXIT

wait_for_health() {
  local container_name="$1"
  local attempts="${2:-180}"
  local status=""
  for ((attempt = 1; attempt <= attempts; attempt++)); do
    status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container_name")"
    if [[ "$status" == "healthy" ]]; then return 0; fi
    if [[ "$status" == "unhealthy" || "$status" == "exited" || "$status" == "dead" ]]; then
      docker logs "$container_name"
      return 1
    fi
    sleep 1
  done
  docker logs "$container_name"
  echo "Container $container_name did not become healthy; last status: $status" >&2
  return 1
}

cd "$project_root"
docker image inspect cute-oa-api:local >/dev/null
if ! docker image inspect "$clamav_image" >/dev/null 2>&1; then
  echo "Missing $clamav_image. Pull it first with: docker pull --platform $clamav_platform $clamav_image" >&2
  exit 2
fi

docker compose up -d postgres >/dev/null
docker compose exec -T postgres createdb -U oa "$test_database"
docker network create "$network_name" >/dev/null
docker volume create "$files_volume" >/dev/null
docker volume create "$clamav_volume" >/dev/null

docker run --detach \
  --name "$clamav_container" \
  --platform "$clamav_platform" \
  --network "$network_name" \
  --network-alias clamav \
  --cap-drop ALL \
  --cap-add CHOWN \
  --cap-add FOWNER \
  --cap-add DAC_OVERRIDE \
  --cap-add SETGID \
  --cap-add SETUID \
  --security-opt no-new-privileges:true \
  --mount "source=$clamav_volume,target=/var/lib/clamav" \
  "$clamav_image" >/dev/null

wait_for_health "$clamav_container"

docker run --detach \
  --name "$api_container" \
  --network "$network_name" \
  --add-host host.docker.internal:host-gateway \
  --read-only \
  --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  --mount "source=$files_volume,target=/var/lib/cute-oa/files" \
  --cap-drop ALL \
  --security-opt no-new-privileges:true \
  --publish 127.0.0.1::8080 \
  --env ASPNETCORE_ENVIRONMENT=Development \
  --env Persistence__UsePostgreSql=true \
  --env "ConnectionStrings__OaDatabase=Host=host.docker.internal;Port=5433;Database=$test_database;Username=oa;Password=oa_dev_password" \
  --env Authentication__SigningKey=clamav-smoke-signing-key-at-least-32-bytes \
  --env FileScanning__Mode=ClamAv \
  --env FileScanning__ClamAv__Host=clamav \
  --env FileScanning__ClamAv__Port=3310 \
  --env FileScanning__ClamAv__TimeoutSeconds=5 \
  --env Storage__Root=/var/lib/cute-oa/files \
  cute-oa-api:local >/dev/null

wait_for_health "$api_container"
mapped_port="$(docker port "$api_container" 8080/tcp | sed -E 's/.*:([0-9]+)$/\1/')"
api_origin="http://127.0.0.1:$mapped_port"
ready_status="$(curl --silent --output /dev/null --write-out '%{http_code}' "$api_origin/health/ready")"
[[ "$ready_status" == "200" ]]

OA_TEST_API_BASE="$api_origin/api/v1" node scripts/verify-clamav-client.mjs scan
scan_signature="$(docker compose exec -T postgres psql -U oa -d "$test_database" -Atc 'SELECT (SELECT count(*) FROM file_object WHERE "OriginalName" = '\''safe-clamav-check.pdf'\''), (SELECT count(*) FROM file_object WHERE "OriginalName" = '\''eicar-clamav-check.docx'\''), (SELECT count(*) FROM audit_log WHERE "Action" = '\''FILE_SCAN_BLOCKED'\'');')"
[[ "$scan_signature" == "1|0|1" ]]

docker stop "$clamav_container" >/dev/null
unready_status="$(curl --silent --output /dev/null --write-out '%{http_code}' "$api_origin/health/ready")"
[[ "$unready_status" == "503" ]]
OA_TEST_API_BASE="$api_origin/api/v1" node scripts/verify-clamav-client.mjs unavailable
failure_signature="$(docker compose exec -T postgres psql -U oa -d "$test_database" -Atc 'SELECT (SELECT count(*) FROM file_object WHERE "OriginalName" = '\''unavailable-clamav-check.pdf'\''), (SELECT count(*) FROM audit_log WHERE "Action" = '\''FILE_SCAN_FAILED'\'');')"
[[ "$failure_signature" == "0|1" ]]

docker start "$clamav_container" >/dev/null
wait_for_health "$clamav_container"
recovered_status="$(curl --silent --output /dev/null --write-out '%{http_code}' "$api_origin/health/ready")"
[[ "$recovered_status" == "200" ]]

echo "ClamAV integration passed: clean=stored eicar=blocked unavailable=503 recovered=ready"
