#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
if [[ -f "$script_directory/common.sh" ]]; then source "$script_directory/common.sh"; else source /usr/local/lib/erp-common.sh; fi
require_root
for command_name in pg_dump psql age python3 tar; do require_command "$command_name"; done
[[ -f /etc/erp/backup.env ]] || die '缺少 /etc/erp/backup.env'
# shellcheck disable=SC1091
source /etc/erp/backup.env
# shellcheck disable=SC1091
source /etc/erp/backup-db.env
[[ ${ERP_BACKUP_AGE_RECIPIENT:-} =~ ^age1[0-9a-z]+$ ]] || die 'age 接收方公钥无效'
backup_root=$(safe_absolute_directory "${ERP_BACKUP_DIRECTORY:-/srv/erp/backups}")
mkdir -p "$backup_root"
stamp=$(date -u +%Y%m%d-%H%M%S)
work_directory=$(mktemp -d "$backup_root/work.$stamp.XXXXXX")
plain_archive="$backup_root/erp-backup-$stamp.tar.gz"
encrypted_archive="$plain_archive.age"
cleanup() { rm -rf -- "$work_directory"; rm -f -- "$plain_archive"; }
trap cleanup EXIT

schema_version=$(PGPASSWORD=$ERP_BACKUP_PASSWORD psql --host="$ERP_BACKUP_HOST" --port="$ERP_BACKUP_PORT" \
  --username="$ERP_BACKUP_USER" --dbname="$ERP_BACKUP_DATABASE" -AtX \
  -c "SELECT COALESCE(MAX(version), '') FROM flyway_schema_history WHERE success = true")
[[ "$schema_version" =~ ^[0-9]+$ ]] || die '无法读取备份 schema 版本'
PGPASSWORD=$ERP_BACKUP_PASSWORD pg_dump --host="$ERP_BACKUP_HOST" --port="$ERP_BACKUP_PORT" \
  --username="$ERP_BACKUP_USER" --dbname="$ERP_BACKUP_DATABASE" --format=custom \
  --no-owner --no-privileges --file="$work_directory/database.dump"
cp -a /srv/erp/data/attachments "$work_directory/attachments"
cp -a /srv/erp/data/data-protection-keys "$work_directory/data-protection-keys"
python3 - "$work_directory" "$schema_version" <<'PY'
import datetime, hashlib, json, os, pathlib, sys
root = pathlib.Path(sys.argv[1])
schema = sys.argv[2]
files = []
for path in sorted(root.rglob("*")):
    if not path.is_file(): continue
    files.append({
        "path": str(path.relative_to(root)), "size": path.stat().st_size,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
    })
(root / "backup-manifest.json").write_text(json.dumps({
    "formatVersion": 1,
    "createdAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    "database": "erp", "schemaVersion": schema, "files": files,
}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
PY
tar -C "$work_directory" -czf "$plain_archive" .
age --recipient "$ERP_BACKUP_AGE_RECIPIENT" --output "$encrypted_archive" "$plain_archive"
printf '%s  %s\n' "$(sha256_file "$encrypted_archive")" "$(basename "$encrypted_archive")" >"$encrypted_archive.sha256"
chmod 0600 "$encrypted_archive" "$encrypted_archive.sha256"
retention_days=${ERP_BACKUP_RETENTION_DAYS:-14}
[[ $retention_days =~ ^[1-9][0-9]{0,2}$ ]] || die 'ERP_BACKUP_RETENTION_DAYS 必须为1到999天'
while IFS= read -r -d '' expired_archive; do
  expired_checksum="$expired_archive.sha256"
  [[ $expired_archive == "$backup_root"/erp-backup-*.tar.gz.age ]] || die '备份清理路径越界'
  rm -f -- "$expired_archive" "$expired_checksum"
done < <(find "$backup_root" -maxdepth 1 -type f -name 'erp-backup-*.tar.gz.age' -mtime "+$retention_days" -print0)
printf '%s\n' "$encrypted_archive"
