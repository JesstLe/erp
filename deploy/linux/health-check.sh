#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
if [[ -f "$script_directory/common.sh" ]]; then source "$script_directory/common.sh"; else source /usr/local/lib/erp-common.sh; fi
require_root
for command_name in curl jq systemctl pg_isready openssl python3; do require_command "$command_name"; done
# shellcheck disable=SC1091
source /etc/erp/host.env

issues=()
state_file=/srv/erp/active-slot.json
if [[ ! -f $state_file ]]; then
  issues+=('active release state is missing')
else
  active_slot=$(jq -r '.slot' "$state_file")
  expected_schema=$(jq -r '.schemaVersion' "$state_file")
  if [[ $active_slot != blue && $active_slot != green ]]; then
    issues+=('active slot state is invalid')
  elif ! systemctl is-active --quiet "erp-$active_slot.service"; then
    issues+=("active service erp-$active_slot is not running")
  fi
  if ! response=$(curl --fail --silent --show-error --max-time 10 \
      --resolve "$ERP_PUBLIC_ADDRESS:443:127.0.0.1" "$ERP_PUBLIC_READY_URL" 2>/dev/null) ||
    ! jq -e --arg schema "$expected_schema" '.status == "ready" and (.schemaVersion | tostring) == $schema' \
      <<<"$response" >/dev/null; then
    issues+=('public readiness gate failed')
  fi
fi

pg_isready --host=127.0.0.1 --port=5432 --dbname=erp --timeout=3 >/dev/null 2>&1 || issues+=('PostgreSQL is not ready')
root_used=$(df -P / | awk 'NR==2 {gsub("%", "", $5); print $5}')
[[ $root_used =~ ^[0-9]+$ && $root_used -lt 85 ]] || issues+=("root filesystem usage is ${root_used:-unknown}%")

latest_backup=$(find /srv/erp/backups -maxdepth 1 -type f -name 'erp-backup-*.tar.gz.age' -print0 2>/dev/null \
  | xargs -0 stat -c '%Y %n' 2>/dev/null | sort -rn | head -1 || true)
if [[ -z $latest_backup ]]; then
  issues+=('no completed encrypted backup exists')
else
  backup_epoch=${latest_backup%% *}
  backup_age_seconds=$(( $(date +%s) - backup_epoch ))
  (( backup_age_seconds <= 129600 )) || issues+=("latest encrypted backup is $((backup_age_seconds / 3600)) hours old")
fi

certificate=/etc/letsencrypt/live/erp-ip/fullchain.pem
[[ -r $certificate ]] && openssl x509 -checkend 86400 -noout -in "$certificate" >/dev/null 2>&1 \
  || issues+=('HTTPS certificate is missing or expires within 24 hours')

if ((${#issues[@]} == 0)); then
  printf 'ERP_HEALTH_OK\n'
  exit 0
fi

message=$(printf '%s; ' "${issues[@]}")
logger -p daemon.err -t erp-health -- "$message"
if [[ -r /etc/erp/monitor.env ]]; then
  # shellcheck disable=SC1091
  source /etc/erp/monitor.env
  webhook_pattern='^https://[A-Za-z0-9._~:/?%&=+-]+$'
  if [[ ${ERP_ALERT_WEBHOOK_URL:-} =~ $webhook_pattern ]]; then
    payload=$(python3 -c 'import json,sys; print(json.dumps({"source":"erp-health","status":"failed","message":sys.argv[1]}))' "$message")
    curl --fail --silent --show-error --max-time 10 --data-binary "$payload" --config - >/dev/null <<EOF || true
url = "$ERP_ALERT_WEBHOOK_URL"
request = "POST"
header = "Content-Type: application/json"
EOF
  fi
fi
printf 'ERP_HEALTH_FAILED:%s\n' "$message" >&2
exit 1
