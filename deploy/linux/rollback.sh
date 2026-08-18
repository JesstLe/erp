#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
if [[ -f "$script_directory/common.sh" ]]; then source "$script_directory/common.sh"; else source /usr/local/lib/erp-common.sh; fi
require_root
for command_name in jq curl nginx systemctl python3; do require_command "$command_name"; done
state_file=/srv/erp/active-slot.json
[[ -f "$state_file" ]] || die '缺少活动槽位记录'
current_slot=$(jq -r '.slot' "$state_file")
current_schema=$(jq -r '.schemaVersion' "$state_file")
case "$current_slot" in
  blue) target_slot=green; target_port=5102 ;;
  green) target_slot=blue; target_port=5101 ;;
  *) die '当前槽位无效' ;;
esac
target_link="/srv/erp/slots/$target_slot"
[[ -L "$target_link" && -f "$target_link/release-manifest.json" ]] || die '没有可回退的目标槽位'
manifest="$target_link/release-manifest.json"
schema_min=$(jq -r '.schema.min' "$manifest")
schema_max=$(jq -r '.schema.max' "$manifest")
(( current_schema >= schema_min && current_schema <= schema_max )) || die "目标版本不兼容当前 schema $current_schema"
version=$(jq -r '.version' "$manifest")
git_commit=$(jq -r '.gitCommit' "$manifest")

systemctl start "erp-$target_slot.service"
wait_for_ready "http://127.0.0.1:$target_port/health/ready" "$current_schema"
upstream_file=/etc/nginx/snippets/erp-upstream.conf
previous_upstream=$(cat "$upstream_file")
printf 'proxy_pass http://127.0.0.1:%s;\n' "$target_port" >"$upstream_file.new"
mv -f "$upstream_file.new" "$upstream_file"
if ! nginx -t || ! systemctl reload nginx; then
  printf '%s\n' "$previous_upstream" >"$upstream_file"
  systemctl reload nginx
  die '回退切流失败，已恢复原代理'
fi

# shellcheck disable=SC1091
source /etc/erp/host.env
public_body=''
for attempt in {1..20}; do
  if public_body=$(curl --fail --silent --show-error --max-time 10 \
      --resolve "$ERP_PUBLIC_ADDRESS:443:127.0.0.1" "$ERP_PUBLIC_READY_URL" 2>/dev/null) &&
     python3 - "$current_schema" "$public_body" <<'PY'
import json, sys
expected, raw = sys.argv[1:]
payload = json.loads(raw)
raise SystemExit(0 if payload.get("status") == "ready" and str(payload.get("schemaVersion")) == expected else 1)
PY
  then break; fi
  [[ $attempt -lt 20 ]] || {
    printf '%s\n' "$previous_upstream" >"$upstream_file"
    systemctl reload nginx
    die '回退后的 HTTPS 健康检查失败，已恢复原代理'
  }
  sleep 2
done

atomic_symlink "$(readlink -f "$target_link")" /srv/erp/current
python3 - "$state_file" "$target_slot" "$target_port" "$version" "$git_commit" "$current_schema" "$current_slot" <<'PY'
import datetime, json, os, sys, tempfile
path, slot, port, version, commit, schema, previous = sys.argv[1:]
payload = {
    "slot": slot, "port": int(port), "version": version, "gitCommit": commit,
    "schemaVersion": schema, "previousSlot": previous, "rollback": True,
    "activatedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
}
fd, temporary = tempfile.mkstemp(prefix="active-slot.", dir=os.path.dirname(path), text=True)
with os.fdopen(fd, "w", encoding="utf-8") as handle:
    json.dump(payload, handle, ensure_ascii=False, indent=2); handle.write("\n")
os.replace(temporary, path)
PY
systemctl stop "erp-$current_slot.service" || true
printf 'ROLLED_BACK:%s:%s\n' "$version" "$target_slot"
