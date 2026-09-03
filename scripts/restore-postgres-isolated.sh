#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "用法：bash scripts/restore-postgres-isolated.sh <backups中的文件名.dump> <oa_restore_开头的新数据库名>" >&2
  exit 2
fi

oa_project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
oa_backup_dir="$oa_project_dir/backups"
oa_restore_name="$1"
oa_restore_database="$2"
oa_restore_user="${OA_BACKUP_USER:-oa}"

if [[ ! "$oa_restore_name" =~ ^[A-Za-z0-9._-]+\.dump$ ]]; then
  echo "恢复文件必须是 backups/ 下不含路径的 .dump 文件名。" >&2
  exit 2
fi
if [[ ! "$oa_restore_database" =~ ^oa_restore_[a-z0-9_]+$ ]]; then
  echo "安全限制：目标数据库必须是新的 oa_restore_* 隔离库，禁止覆盖 oa 或其他现有库。" >&2
  exit 2
fi
if [[ ! "$oa_restore_user" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "OA_BACKUP_USER 只能包含字母、数字和下划线。" >&2
  exit 2
fi

oa_restore_file="$oa_backup_dir/$oa_restore_name"
if [[ ! -s "$oa_restore_file" ]]; then
  echo "备份文件不存在或为空：$oa_restore_file" >&2
  exit 3
fi

cd "$oa_project_dir"
oa_restore_exists="$(docker compose exec -T postgres psql -U "$oa_restore_user" -d postgres -Atc "SELECT 1 FROM pg_database WHERE datname = '$oa_restore_database';")"
if [[ -n "$oa_restore_exists" ]]; then
  echo "目标数据库已存在，拒绝覆盖：$oa_restore_database" >&2
  exit 4
fi

docker compose exec -T postgres createdb -U "$oa_restore_user" "$oa_restore_database"
if ! docker compose exec -T postgres pg_restore -U "$oa_restore_user" -d "$oa_restore_database" --exit-on-error --no-owner --no-privileges < "$oa_restore_file"; then
  docker compose exec -T postgres dropdb -U "$oa_restore_user" --if-exists "$oa_restore_database" >/dev/null
  echo "恢复失败，已清理未完成的隔离数据库。" >&2
  exit 5
fi

echo "$oa_restore_database"
