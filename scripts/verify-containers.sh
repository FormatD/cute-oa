#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
run_id="$$"
network_name="cute-oa-smoke-${run_id}"
api_container="cute-oa-api-smoke-${run_id}"
web_container="cute-oa-web-smoke-${run_id}"
temp_dir="$(mktemp -d)"

cleanup() {
  docker rm --force --volumes "${web_container}" "${api_container}" >/dev/null 2>&1 || true
  docker network rm "${network_name}" >/dev/null 2>&1 || true
  rm -rf "${temp_dir}"
}
trap cleanup EXIT

wait_for_health() {
  local container_name="$1"
  local attempts="${2:-90}"
  local status=""

  for ((attempt = 1; attempt <= attempts; attempt++)); do
    status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "${container_name}")"
    if [[ "${status}" == "healthy" ]]; then
      return 0
    fi
    if [[ "${status}" == "unhealthy" || "${status}" == "exited" || "${status}" == "dead" ]]; then
      docker logs "${container_name}"
      return 1
    fi
    sleep 1
  done

  docker logs "${container_name}"
  echo "Container ${container_name} did not become healthy; last status: ${status}" >&2
  return 1
}

cd "${repo_root}"

[[ "$(docker image inspect cute-oa-api:local --format '{{.Config.User}}')" == "app" ]]
[[ "$(docker image inspect cute-oa-web:local --format '{{.Config.User}}')" == "nginx" ]]

openssl req -x509 -newkey rsa:2048 -nodes -days 1 \
  -subj "/CN=localhost" \
  -keyout "${temp_dir}/tls.key" \
  -out "${temp_dir}/tls.crt" >/dev/null 2>&1
chmod 0444 "${temp_dir}/tls.key" "${temp_dir}/tls.crt"

docker network create "${network_name}" >/dev/null

docker run --detach \
  --name "${api_container}" \
  --network "${network_name}" \
  --network-alias api \
  --add-host host.docker.internal:host-gateway \
  --read-only \
  --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  --cap-drop ALL \
  --security-opt no-new-privileges:true \
  --env ASPNETCORE_ENVIRONMENT=Development \
  --env Persistence__UsePostgreSql=true \
  --env 'ConnectionStrings__OaDatabase=Host=host.docker.internal;Port=5433;Database=oa;Username=oa;Password=oa_dev_password' \
  --env Authentication__SigningKey=container-smoke-signing-key-at-least-32-bytes \
  --env FileScanning__Mode=Disabled \
  --env Storage__Root=/var/lib/cute-oa/files \
  cute-oa-api:local >/dev/null

wait_for_health "${api_container}"

docker run --detach \
  --name "${web_container}" \
  --network "${network_name}" \
  --read-only \
  --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  --cap-drop ALL \
  --security-opt no-new-privileges:true \
  --publish 127.0.0.1:5443:8443 \
  --volume "${temp_dir}/tls.crt:/run/secrets/tls_certificate:ro" \
  --volume "${temp_dir}/tls.key:/run/secrets/tls_private_key:ro" \
  cute-oa-web:local >/dev/null

wait_for_health "${web_container}"
curl --fail --silent --show-error --insecure https://127.0.0.1:5443/ >/dev/null
curl --fail --silent --show-error --insecure https://127.0.0.1:5443/health >/dev/null
curl --fail --silent --show-error --insecure https://127.0.0.1:5443/health/ready >/dev/null

headers="$(curl --fail --silent --show-error --insecure --head https://127.0.0.1:5443/)"
grep -qi '^strict-transport-security:' <<<"${headers}"
grep -qi '^content-security-policy:' <<<"${headers}"
grep -qi '^x-content-type-options:' <<<"${headers}"

echo "Production container smoke checks passed."
