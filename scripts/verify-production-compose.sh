#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
deployment_tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/cute-oa-production-config.XXXXXX")"
trap 'rm -rf "$deployment_tmp_dir"' EXIT

printf '%s' 'verification-signing-key-at-least-thirty-two-bytes' > "$deployment_tmp_dir/signing-key"
printf '%s' 'MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=' > "$deployment_tmp_dir/mfa-encryption-key"
printf '%s' 'Production#Bootstrap2026' > "$deployment_tmp_dir/bootstrap-admin-password"
printf '%s' 'Host=db.example.internal;Database=oa;Username=oa_app;Password=verification;SSL Mode=VerifyFull;Pooling=true;Maximum Pool Size=100' > "$deployment_tmp_dir/database-connection"
: > "$deployment_tmp_dir/tls.crt"
: > "$deployment_tmp_dir/tls.key"

export OA_PUBLIC_ORIGIN='https://oa.example.com'
export OA_PUBLIC_HOST='oa.example.com'
export OA_SIGNING_KEY_FILE="$deployment_tmp_dir/signing-key"
export OA_DATABASE_CONNECTION_FILE="$deployment_tmp_dir/database-connection"
export OA_MFA_ENCRYPTION_KEY_FILE="$deployment_tmp_dir/mfa-encryption-key"
export OA_BOOTSTRAP_ADMIN_PASSWORD_FILE="$deployment_tmp_dir/bootstrap-admin-password"
export OA_BOOTSTRAP_ENABLED='true'
export OA_BOOTSTRAP_ADMIN_USER_ID='oa-production-admin'
export OA_BOOTSTRAP_ADMIN_NAME='生产引导管理员'
export OA_BOOTSTRAP_DEPARTMENT_ID='management'
export OA_BOOTSTRAP_DEPARTMENT_NAME='管理部'
export OA_BOOTSTRAP_ADMIN_EMPLOYEE_NUMBER='ADMIN-001'
export OA_BOOTSTRAP_ADMIN_HIRE_DATE='2026-01-01'
export OA_PERSONNEL_HR_ASSIGNEE_ID='hr-owner'
export OA_PERSONNEL_FINANCE_ASSIGNEE_ID='finance-owner'
export OA_PERSONNEL_IT_ASSIGNEE_ID='it-owner'
export OA_PERSONNEL_ADMIN_ASSIGNEE_ID='admin-owner'
export OA_TLS_CERTIFICATE_FILE="$deployment_tmp_dir/tls.crt"
export OA_TLS_PRIVATE_KEY_FILE="$deployment_tmp_dir/tls.key"

cd "$project_root"
rendered_config="$deployment_tmp_dir/compose.json"
docker compose -f compose.production.yml config --format json > "$rendered_config"

jq -e '
  .services.api.depends_on.clamav.condition == "service_healthy" and
  .services.clamav.platform == "linux/amd64" and
  .services.clamav.mem_limit >= 4294967296 and
  (.services.clamav.cap_drop | index("ALL") != null) and
  (["CHOWN", "FOWNER", "DAC_OVERRIDE", "SETGID", "SETUID"] - .services.clamav.cap_add | length == 0) and
  (.services.clamav.healthcheck.test | length > 0)
' "$rendered_config" >/dev/null

jq -e '
  .services.api.environment.PersonnelCases__CategoryAssignees__HR == "hr-owner" and
  .services.api.environment.PersonnelCases__CategoryAssignees__FINANCE == "finance-owner" and
  .services.api.environment.PersonnelCases__CategoryAssignees__IT == "it-owner" and
  .services.api.environment.PersonnelCases__CategoryAssignees__ADMIN == "admin-owner" and
  .services.api.environment.Bootstrap__AdminEmployeeNumber == "ADMIN-001" and
  .services.api.environment.Bootstrap__AdminHireDate == "2026-01-01" and
  (.services.api.secrets | any(.target == "Bootstrap__AdminPassword"))
' "$rendered_config" >/dev/null

if rg -q 'http://[^" ]*:5234/api/v1' frontend/dist/assets; then
    echo 'Production frontend bundle contains the development API URL.' >&2
    exit 1
fi

echo 'Production Compose configuration passed.'
