#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
if [[ -f "$script_directory/common.sh" ]]; then source "$script_directory/common.sh"; else source /usr/local/lib/erp-common.sh; fi

[[ $# -eq 4 ]] || die "用法: $0 BACKUP.tar.gz.age EXPECTED_SHA256 AGE_IDENTITY TEMP_DATABASE"
require_root
for command_name in age python3 createdb dropdb pg_restore psql runuser; do require_command "$command_name"; done
archive=$(realpath "$1")
expected_hash=${2,,}
identity=$(realpath "$3")
temporary_database=$4
[[ -f "$archive" && -f "$identity" ]] || die '备份或 age 私钥文件不存在'
[[ "$expected_hash" =~ ^[0-9a-f]{64}$ ]] || die 'SHA-256 格式无效'
[[ $(sha256_file "$archive") == "$expected_hash" ]] || die '备份 SHA-256 不匹配'
[[ "$temporary_database" =~ ^erp_restore_verify_[a-z0-9_]{1,40}$ ]] || die '临时数据库名称不符合隔离规则'
[[ ${ERP_RESTORE_CONFIRM:-} == RESTORE_TO_DISPOSABLE_DATABASE ]] || die '缺少隔离恢复确认变量'
[[ -f /etc/erp/backup-db.env ]] || die '缺少隔离恢复数据库配置'
if runuser -u postgres -- psql --dbname=postgres -AtX \
  -c "SELECT 1 FROM pg_database WHERE datname = '$temporary_database'" | grep -qx 1; then
  die '隔离恢复目标已经存在，拒绝覆盖'
fi

work_directory=$(mktemp -d /var/tmp/erp-backup-verify.XXXXXX)
plain_archive="$work_directory/backup.tar.gz"
database_created=false
cleanup() {
  if [[ $database_created == true ]]; then
    runuser -u postgres -- dropdb --if-exists "$temporary_database" >/dev/null 2>&1 || true
  fi
  rm -rf -- "$work_directory"
}
trap cleanup EXIT

age --decrypt --identity "$identity" --output "$plain_archive" "$archive"
python3 - "$plain_archive" "$work_directory/content" <<'PY'
import hashlib, json, os, pathlib, tarfile, sys
archive, destination = sys.argv[1:]
root = pathlib.Path(destination).resolve()
root.mkdir()
with tarfile.open(archive, "r:gz") as handle:
    for member in handle.getmembers():
        target = (root / member.name.removeprefix("./")).resolve()
        if target != root and root not in target.parents:
            raise SystemExit(f"backup path escapes root: {member.name}")
        if member.issym() or member.islnk() or member.isdev():
            raise SystemExit(f"backup contains unsupported entry: {member.name}")
    handle.extractall(root, filter="data")
manifest_path = root / "backup-manifest.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
if manifest.get("formatVersion") != 1 or not str(manifest.get("schemaVersion", "")).isdigit():
    raise SystemExit("invalid backup manifest")
listed = set()
for item in manifest.get("files", []):
    relative = item["path"]
    candidate = (root / relative).resolve()
    if relative in listed or root not in candidate.parents or not candidate.is_file():
        raise SystemExit(f"invalid backup manifest path: {relative}")
    listed.add(relative)
    digest_builder = hashlib.sha256()
    with candidate.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest_builder.update(chunk)
    digest = digest_builder.hexdigest()
    if digest != item["sha256"] or candidate.stat().st_size != item["size"]:
        raise SystemExit(f"backup manifest mismatch: {relative}")
actual = {
    str(path.relative_to(root)).replace(os.sep, "/")
    for path in root.rglob("*") if path.is_file() and path != manifest_path
}
if actual != listed:
    raise SystemExit("backup payload differs from manifest")
PY

chown root:postgres "$work_directory"
chmod 0710 "$work_directory"
chown -R postgres:postgres "$work_directory/content"
runuser -u postgres -- createdb "$temporary_database"
database_created=true
runuser -u postgres -- pg_restore --dbname="$temporary_database" \
  --exit-on-error --no-owner --no-privileges "$work_directory/content/database.dump"
expected_schema=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["schemaVersion"])' \
  "$work_directory/content/backup-manifest.json")
actual_schema=$(runuser -u postgres -- psql --dbname="$temporary_database" -AtX \
  -c "SELECT COALESCE(MAX(version), '') FROM flyway_schema_history WHERE success = true")
[[ $actual_schema == "$expected_schema" ]] || die '隔离恢复 schema 与备份清单不一致'
printf 'BACKUP_VERIFIED:%s\n' "$actual_schema"
