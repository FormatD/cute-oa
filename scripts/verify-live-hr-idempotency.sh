#!/usr/bin/env bash
set -euo pipefail

api_base="${OA_VERIFY_API_BASE:-http://127.0.0.1:5234/api/v1}"
user_id="${OA_VERIFY_HR_USER:-u-sun}"
password_file="${OA_VERIFY_HR_PASSWORD_FILE:-}"
if [[ -n "$password_file" ]]; then
  password="$(<"$password_file")"
elif [[ -n "${OA_VERIFY_HR_PASSWORD:-}" ]]; then
  password="$OA_VERIFY_HR_PASSWORD"
elif [[ "$api_base" == http://127.0.0.1:* || "$api_base" == http://localhost:* ]]; then
  password='Oa@123456'
else
  echo 'OA_VERIFY_HR_PASSWORD_FILE or OA_VERIFY_HR_PASSWORD is required for a non-local API.' >&2
  exit 1
fi
temp_dir="$(mktemp -d "${TMPDIR:-/tmp}/cute-oa-hr-idempotency.XXXXXX")"
cleanup() {
  if [[ -s "$temp_dir/cookies" ]]; then
    curl --silent --show-error --request POST --cookie "$temp_dir/cookies" "$api_base/auth/logout" >/dev/null 2>&1 || true
  fi
  rm -rf "$temp_dir"
}
trap cleanup EXIT

jq -cn --arg userId "$user_id" --arg password "$password" '{userId:$userId,password:$password}' > "$temp_dir/login-request.json"
curl --fail --silent --show-error \
  --header 'Content-Type: application/json' \
  --cookie-jar "$temp_dir/cookies" \
  --data-binary "@$temp_dir/login-request.json" \
  "$api_base/auth/login" > "$temp_dir/login-response.json"
access_token="$(jq -er 'select(.status == "AUTHENTICATED") | .session.accessToken' "$temp_dir/login-response.json")"
printf 'Authorization: Bearer %s\n' "$access_token" > "$temp_dir/auth-header"
chmod 0600 "$temp_dir/auth-header"

curl --fail --silent --show-error \
  --header "@$temp_dir/auth-header" \
  "$api_base/attendance/shifts" > "$temp_dir/shifts.json"

jq -e 'map(select(.isDefault and .isEnabled)) | length == 1' "$temp_dir/shifts.json" >/dev/null
jq -c 'map(select(.isDefault and .isEnabled))[0]' "$temp_dir/shifts.json" > "$temp_dir/shift.json"
shift_id="$(jq -er '.id' "$temp_dir/shift.json")"
initial_version="$(jq -er '.version' "$temp_dir/shift.json")"
jq -c '{code,name,workStart,workEnd,breakMinutes,lateToleranceMinutes,earlyLeaveToleranceMinutes,isDefault,isEnabled,version}' "$temp_dir/shift.json" > "$temp_dir/request.json"
jq -c '.name += "-不同请求"' "$temp_dir/request.json" > "$temp_dir/conflicting-request.json"

idempotency_key="live-hr-idempotency-$(date +%s)-$$"
endpoint="$api_base/attendance/shifts/$shift_id"
first_status="$(curl --silent --show-error --output "$temp_dir/first.json" --write-out '%{http_code}' \
  --request PUT \
  --header "@$temp_dir/auth-header" \
  --header 'Content-Type: application/json' \
  --header "Idempotency-Key: $idempotency_key" \
  --data-binary "@$temp_dir/request.json" \
  "$endpoint")"
replay_status="$(curl --silent --show-error --output "$temp_dir/replay.json" --write-out '%{http_code}' \
  --request PUT \
  --header "@$temp_dir/auth-header" \
  --header 'Content-Type: application/json' \
  --header "Idempotency-Key: $idempotency_key" \
  --data-binary "@$temp_dir/request.json" \
  "$endpoint")"
conflict_status="$(curl --silent --show-error --output "$temp_dir/conflict.json" --write-out '%{http_code}' \
  --request PUT \
  --header "@$temp_dir/auth-header" \
  --header 'Content-Type: application/json' \
  --header "Idempotency-Key: $idempotency_key" \
  --data-binary "@$temp_dir/conflicting-request.json" \
  "$endpoint")"

[[ "$first_status" == "200" ]]
[[ "$replay_status" == "200" ]]
[[ "$conflict_status" == "409" ]]
jq -S . "$temp_dir/first.json" > "$temp_dir/first-canonical.json"
jq -S . "$temp_dir/replay.json" > "$temp_dir/replay-canonical.json"
cmp --silent "$temp_dir/first-canonical.json" "$temp_dir/replay-canonical.json"
jq -e --argjson expected "$((initial_version + 1))" '.version == $expected' "$temp_dir/first.json" >/dev/null
jq -e '.code == "IDEMPOTENCY_002"' "$temp_dir/conflict.json" >/dev/null

curl --fail --silent --show-error \
  --header "@$temp_dir/auth-header" \
  "$api_base/attendance/shifts" > "$temp_dir/final-shifts.json"
jq -e --arg id "$shift_id" --argjson expected "$((initial_version + 1))" 'map(select(.id == $id))[0].version == $expected' "$temp_dir/final-shifts.json" >/dev/null

echo "Live HR atomic idempotency verification passed."
