#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
if [[ -f "$script_directory/common.sh" ]]; then
  # shellcheck source=common.sh
  source "$script_directory/common.sh"
else
  # shellcheck source=/usr/local/lib/erp-common.sh
  source /usr/local/lib/erp-common.sh
fi

usage() {
  printf '用法: %s RELEASE.tar.gz EXPECTED_SHA256\n' "$0" >&2
  exit 2
}

[[ $# -eq 2 ]] || usage
require_root
package=$(realpath "$1")
expected_hash=${2,,}
[[ -f "$package" ]] || die '发布包不存在'
[[ "$expected_hash" =~ ^[0-9a-f]{64}$ ]] || die 'SHA-256 格式无效'
[[ $(sha256_file "$package") == "$expected_hash" ]] || die '发布包 SHA-256 不匹配'
for command_name in python3 flyway curl jq systemctl nginx psql; do require_command "$command_name"; done
[[ -f /etc/erp/backup.env ]] || die '缺少加密备份配置，拒绝发布'

install_root=$(safe_absolute_directory /srv/erp)
inspection_directory=$(mktemp -d "$install_root/inspection.XXXXXX")
cleanup() {
  if [[ -n $inspection_directory && -d $inspection_directory ]]; then
    rm -rf -- "$inspection_directory"
  fi
}
trap cleanup EXIT

python3 - "$package" "$inspection_directory" <<'PY'
import os, pathlib, tarfile, sys
archive, target = sys.argv[1:]
root = pathlib.Path(target).resolve()
with tarfile.open(archive, "r:gz") as handle:
    for member in handle.getmembers():
        name = member.name.removeprefix("./")
        destination = (root / name).resolve()
        if destination != root and root not in destination.parents:
            raise SystemExit(f"archive path escapes root: {member.name}")
        if member.issym() or member.islnk() or member.isdev():
            raise SystemExit(f"archive contains unsupported entry: {member.name}")
    handle.extractall(root, filter="data")
PY

python3 - "$inspection_directory" <<'PY'
import hashlib, json, os, pathlib, sys
root = pathlib.Path(sys.argv[1]).resolve()
manifest_path = root / "release-manifest.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
if manifest.get("formatVersion") != 1 or manifest.get("runtime") != "linux-x64-framework-dependent":
    raise SystemExit("unsupported release manifest")
listed = set()
for item in manifest.get("files", []):
    relative = item["path"]
    if relative in listed:
        raise SystemExit(f"duplicate manifest path: {relative}")
    listed.add(relative)
    candidate = (root / relative).resolve()
    if root not in candidate.parents or not candidate.is_file():
        raise SystemExit(f"invalid manifest path: {relative}")
    digest = hashlib.sha256(candidate.read_bytes()).hexdigest()
    if digest != item["sha256"] or candidate.stat().st_size != item["size"]:
        raise SystemExit(f"manifest mismatch: {relative}")
actual = {
    str(path.relative_to(root)).replace(os.sep, "/")
    for path in root.rglob("*") if path.is_file() and path != manifest_path
}
if actual != listed:
    raise SystemExit(f"payload mismatch missing={sorted(listed-actual)} extra={sorted(actual-listed)}")
PY

manifest="$inspection_directory/release-manifest.json"
version=$(jq -r '.version' "$manifest")
schema_min=$(jq -r '.schema.min' "$manifest")
schema_max=$(jq -r '.schema.max' "$manifest")
git_commit=$(jq -r '.gitCommit' "$manifest")
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]] || die '清单版本号无效'
[[ "$schema_min" =~ ^[0-9]+$ && "$schema_max" =~ ^[0-9]+$ ]] || die '清单 schema 无效'
(( schema_min <= schema_max )) || die '清单 schema 范围无效'
release_directory="$install_root/releases/$version"
[[ ! -e "$release_directory" ]] || die "版本目录已存在: $version"

# shellcheck disable=SC1091
source /etc/erp/migrator.env
export FLYWAY_URL=$ERP_FLYWAY_URL FLYWAY_USER=$ERP_MIGRATOR_USER FLYWAY_PASSWORD=$ERP_MIGRATOR_PASSWORD
if PGPASSWORD=$ERP_MIGRATOR_PASSWORD psql -h 127.0.0.1 -U "$ERP_MIGRATOR_USER" -d erp -tAc \
  "SELECT to_regclass('public.flyway_schema_history') IS NOT NULL" | grep -qx t; then
  [[ -f /etc/erp/backup.env ]] || die '已有数据库缺少 /etc/erp/backup.env，禁止无备份发布'
  /usr/local/sbin/erp-backup >/dev/null
else
  non_system_tables=$(PGPASSWORD=$ERP_MIGRATOR_PASSWORD psql -h 127.0.0.1 -U "$ERP_MIGRATOR_USER" -d erp -tAc \
    "SELECT count(*) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema')")
  [[ $non_system_tables == 0 ]] || die '非空数据库缺少 Flyway 历史，拒绝自动 baseline'
fi

flyway_arguments=(
  "-locations=filesystem:$inspection_directory/db/migrations"
  '-baselineOnMigrate=false' '-cleanDisabled=true' '-validateMigrationNaming=true' '-connectRetries=3'
)
flyway "${flyway_arguments[@]}" '-ignoreMigrationPatterns=*:pending' validate
flyway "${flyway_arguments[@]}" migrate
flyway "${flyway_arguments[@]}" validate
unset FLYWAY_PASSWORD FLYWAY_USER FLYWAY_URL ERP_MIGRATOR_PASSWORD

sudo -u postgres psql -v ON_ERROR_STOP=1 -d erp <<'SQL'
GRANT USAGE ON SCHEMA public TO erp_app, erp_backup;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO erp_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO erp_app;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO erp_backup;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO erp_backup;
SQL

mv "$inspection_directory" "$release_directory"
inspection_directory=''
chown -R root:erp "$release_directory"
find "$release_directory" -type d -exec chmod 0750 {} +
find "$release_directory" -type f -exec chmod 0640 {} +
if [[ -f "$release_directory/app/Erp.Api" ]]; then
  chmod 0750 "$release_directory/app/Erp.Api"
fi

state_file="$install_root/active-slot.json"
active_slot=''
if [[ -f "$state_file" ]]; then active_slot=$(jq -r '.slot' "$state_file"); fi
case "$active_slot" in
  blue) target_slot=green; target_port=5102 ;;
  green) target_slot=blue; target_port=5101 ;;
  '') target_slot=blue; target_port=5101 ;;
  *) die '活动槽位记录无效' ;;
esac

atomic_symlink "$release_directory" "$install_root/slots/$target_slot"
systemctl enable "erp-$target_slot.service"
systemctl restart "erp-$target_slot.service"
wait_for_ready "http://127.0.0.1:$target_port/health/ready" "$schema_max"

upstream_file=/etc/nginx/snippets/erp-upstream.conf
previous_upstream=$(cat "$upstream_file")
printf 'proxy_pass http://127.0.0.1:%s;\n' "$target_port" >"$upstream_file.new"
mv -f "$upstream_file.new" "$upstream_file"
if ! nginx -t || ! systemctl reload nginx; then
  printf '%s\n' "$previous_upstream" >"$upstream_file"
  systemctl reload nginx
  systemctl stop "erp-$target_slot.service" || true
  die 'Nginx 切流失败，已恢复原代理'
fi

# shellcheck disable=SC1091
source /etc/erp/host.env
public_body=''
for attempt in {1..20}; do
  if public_body=$(curl --fail --silent --show-error --max-time 10 \
      --resolve "$ERP_PUBLIC_ADDRESS:443:127.0.0.1" "$ERP_PUBLIC_READY_URL" 2>/dev/null) &&
     python3 - "$schema_max" "$public_body" <<'PY'
import json, sys
expected, raw = sys.argv[1:]
payload = json.loads(raw)
raise SystemExit(0 if payload.get("status") == "ready" and str(payload.get("schemaVersion")) == expected else 1)
PY
  then
    break
  fi
  [[ $attempt -lt 20 ]] || {
    printf '%s\n' "$previous_upstream" >"$upstream_file"
    systemctl reload nginx
    systemctl stop "erp-$target_slot.service" || true
    die '公网 HTTPS 健康检查失败，已恢复原代理'
  }
  sleep 2
done

atomic_symlink "$release_directory" "$install_root/current"
python3 - "$state_file" "$target_slot" "$target_port" "$version" "$git_commit" "$schema_max" "$active_slot" <<'PY'
import datetime, json, os, sys, tempfile
path, slot, port, version, commit, schema, previous = sys.argv[1:]
payload = {
    "slot": slot, "port": int(port), "version": version, "gitCommit": commit,
    "schemaVersion": schema, "previousSlot": previous or None,
    "activatedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
}
directory = os.path.dirname(path)
fd, temporary = tempfile.mkstemp(prefix="active-slot.", dir=directory, text=True)
with os.fdopen(fd, "w", encoding="utf-8") as handle:
    json.dump(payload, handle, ensure_ascii=False, indent=2)
    handle.write("\n")
os.replace(temporary, path)
PY
chmod 0640 "$state_file"
chown root:erp "$state_file"
if [[ -n "$active_slot" && "$active_slot" != "$target_slot" ]]; then
  systemctl stop "erp-$active_slot.service" || true
fi
systemctl enable --now erp-backup.timer erp-health.timer
printf 'DEPLOYED:%s:%s:%s\n' "$version" "$target_slot" "$schema_max"
