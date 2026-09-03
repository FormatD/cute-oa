#!/usr/bin/env bash
set -euo pipefail

oa_project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
oa_backup_dir="$oa_project_dir/backups"
oa_backup_database="${OA_BACKUP_DATABASE:-oa}"
oa_backup_user="${OA_BACKUP_USER:-oa}"
oa_backup_timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
oa_backup_name="${1:-${oa_backup_database}-${oa_backup_timestamp}.dump}"

if [[ ! "$oa_backup_database" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "OA_BACKUP_DATABASE 只能包含字母、数字和下划线。" >&2
  exit 2
fi
if [[ ! "$oa_backup_user" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "OA_BACKUP_USER 只能包含字母、数字和下划线。" >&2
  exit 2
fi
if [[ ! "$oa_backup_name" =~ ^[A-Za-z0-9._-]+\.dump$ ]]; then
  echo "备份文件名必须是 backups/ 下不含路径的 .dump 文件名。" >&2
  exit 2
fi

mkdir -p "$oa_backup_dir"
oa_backup_file="$oa_backup_dir/$oa_backup_name"
if [[ -e "$oa_backup_file" ]]; then
  echo "备份文件已存在，拒绝覆盖：$oa_backup_file" >&2
  exit 3
fi

umask 077
cd "$oa_project_dir"
docker compose exec -T postgres pg_dump -U "$oa_backup_user" -d "$oa_backup_database" --format=custom --no-owner --no-privileges > "$oa_backup_file"
if [[ ! -s "$oa_backup_file" ]]; then
  rm -f "$oa_backup_file"
  echo "备份文件为空，已清理。" >&2
  exit 4
fi
docker compose exec -T postgres pg_restore --list < "$oa_backup_file" >/dev/null
echo "$oa_backup_file"
