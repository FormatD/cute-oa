#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
verification_tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/cute-oa-hr-import.XXXXXX")"
trap 'rm -rf "$verification_tmp_dir"' EXIT

connection_file="$verification_tmp_dir/postgres-connection"
input_file="$verification_tmp_dir/personnel-profiles.json"
printf '%s' 'Host=localhost;Port=5433;Database=oa_test;Username=oa;Password=oa_dev_password' > "$connection_file"
chmod 600 "$connection_file"
jq -n '[{
  userId: "u-import-e2e",
  employeeNumber: "E2E-IMPORT-001",
  hireDate: "2025-03-01",
  employmentType: "FULL_TIME",
  personnelStatus: "ACTIVE",
  cumulativeWorkStartDate: "2020-03-01",
  workEmail: "import-e2e@example.com",
  workPhone: "+86 21 5555 0199",
  workLocation: "上海办公室"
}]' > "$input_file"
chmod 644 "$input_file"

docker compose exec -T postgres psql -U oa -d oa_test -v ON_ERROR_STOP=1 -c "
  INSERT INTO oa_user (\"Id\", \"TenantId\", \"Name\", \"DepartmentId\", \"ManagerId\", \"CumulativeWorkYears\", \"Status\", \"Version\", \"CreatedAt\", \"UpdatedAt\", \"PositionId\")
  VALUES ('u-import-e2e', 'demo', '导入端到端验证员工', 'engineering', 'u-li', 0, 'ACTIVE', 1, NOW(), NOW(), NULL);
  INSERT INTO user_role (\"UserId\", \"RoleCode\", \"IsPrimary\") VALUES ('u-import-e2e', '员工', TRUE);
" >/dev/null

cd "$project_root"
export OA_HR_IMPORT_ACTOR_ID='u-admin'
export OA_HR_IMPORT_CONNECTION_FILE="$connection_file"
if dotnet run --project tools/Oa.HrProfileImport/Oa.HrProfileImport.csproj -- --file "$input_file" >/dev/null 2>&1; then
  echo 'HR profile import accepted an input file readable by other users.' >&2
  exit 1
fi
chmod 600 "$input_file"
dotnet run --project tools/Oa.HrProfileImport/Oa.HrProfileImport.csproj --no-build -- --file "$input_file"

before_apply="$(docker compose exec -T postgres psql -U oa -d oa_test -tAc "SELECT COUNT(*) FROM personnel_profile WHERE \"UserId\"='u-import-e2e';")"
if [[ "$before_apply" != "0" ]]; then
  echo 'HR profile import dry-run wrote data.' >&2
  exit 1
fi

dotnet run --project tools/Oa.HrProfileImport/Oa.HrProfileImport.csproj --no-build -- --file "$input_file" --apply

evidence="$(docker compose exec -T postgres psql -U oa -d oa_test -tAc "
  SELECT
    (SELECT COUNT(*) FROM personnel_profile WHERE \"UserId\"='u-import-e2e') || '|' ||
    (SELECT COUNT(*) FROM personnel_event WHERE \"UserId\"='u-import-e2e' AND \"EventType\"='IMPORTED') || '|' ||
    (SELECT COUNT(*) FROM audit_log WHERE \"ResourceId\"='u-import-e2e' AND \"Action\"='PERSONNEL_PROFILE_IMPORTED');
")"
if [[ "$evidence" != "1|1|1" ]]; then
  echo "HR profile import evidence mismatch: $evidence" >&2
  exit 1
fi

if dotnet run --project tools/Oa.HrProfileImport/Oa.HrProfileImport.csproj --no-build -- --file "$input_file" --apply >/dev/null 2>&1; then
  echo 'HR profile import overwrote an existing profile.' >&2
  exit 1
fi

echo 'HR profile import CLI verification passed.'
