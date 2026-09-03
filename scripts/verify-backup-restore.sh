#!/usr/bin/env bash
set -euo pipefail

oa_project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
oa_restore_user="${OA_BACKUP_USER:-oa}"
oa_verify_stamp="$(date -u +%Y%m%d%H%M%S)-$$"
oa_verify_backup_name="verify-${oa_verify_stamp}.dump"
oa_verify_backup_file="$oa_project_dir/backups/$oa_verify_backup_name"
oa_verify_database="oa_restore_${oa_verify_stamp//-/_}"

cleanup_oa_restore_verify() {
  cd "$oa_project_dir"
  docker compose exec -T postgres psql -U "$oa_restore_user" -d postgres -Atc "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$oa_verify_database' AND pid <> pg_backend_pid();" >/dev/null 2>&1 || true
  docker compose exec -T postgres dropdb -U "$oa_restore_user" --if-exists "$oa_verify_database" >/dev/null 2>&1 || true
  if [[ "$oa_verify_backup_file" == "$oa_project_dir/backups/verify-"*.dump ]]; then
    rm -f "$oa_verify_backup_file"
  fi
}
trap cleanup_oa_restore_verify EXIT

cd "$oa_project_dir"
bash scripts/backup-postgres.sh "$oa_verify_backup_name" >/dev/null
bash scripts/restore-postgres-isolated.sh "$oa_verify_backup_name" "$oa_verify_database" >/dev/null

oa_source_signature="$(docker compose exec -T postgres psql -U "$oa_restore_user" -d oa -Atc "SELECT (SELECT count(*) FROM \"__EFMigrationsHistory\"), (SELECT count(*) FROM oa_user), (SELECT count(*) FROM personnel_profile), (SELECT count(*) FROM employment_contract), (SELECT count(*) FROM personnel_case);")"
oa_restored_signature="$(docker compose exec -T postgres psql -U "$oa_restore_user" -d "$oa_verify_database" -Atc "SELECT (SELECT count(*) FROM \"__EFMigrationsHistory\"), (SELECT count(*) FROM oa_user), (SELECT count(*) FROM personnel_profile), (SELECT count(*) FROM employment_contract), (SELECT count(*) FROM personnel_case);")"

if [[ "$oa_source_signature" != "$oa_restored_signature" ]]; then
  echo "恢复库关键数据计数与源库不一致：source=$oa_source_signature restored=$oa_restored_signature" >&2
  exit 6
fi

echo "PostgreSQL backup/restore rehearsal passed: $oa_source_signature"
