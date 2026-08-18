#!/usr/bin/env bash

die() {
  printf '%s\n' "$1" >&2
  exit 1
}

log() {
  printf '[ERP] %s\n' "$1"
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || die "缺少命令: $1"
}

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  else
    shasum -a 256 "$1" | awk '{print $1}'
  fi
}

safe_absolute_directory() {
  local directory="$1"
  [[ $directory == /* && $directory != / && ${#directory} -ge 8 ]] || die "不安全的目录: $directory"
  printf '%s\n' "$directory"
}

atomic_symlink() {
  local target="$1" link_path="$2" temporary_link
  [[ -d $target ]] || die "软链接目标不是目录: $target"
  install -d "$(dirname -- "$link_path")"
  temporary_link="${link_path}.new.$$"
  ln -s "$target" "$temporary_link"
  mv -Tf "$temporary_link" "$link_path"
}

wait_for_ready() {
  local url="$1" expected_schema="$2" response
  for _ in {1..30}; do
    if response=$(curl --fail --silent --show-error --max-time 5 "$url" 2>/dev/null) &&
      python3 - "$expected_schema" "$response" <<'PY'
import json, sys
expected, raw = sys.argv[1:]
payload = json.loads(raw)
raise SystemExit(0 if payload.get("status") == "ready" and str(payload.get("schemaVersion")) == expected else 1)
PY
    then
      return 0
    fi
    sleep 1
  done
  die "就绪检查失败: $url"
}

require_root() {
  if [[ ${EUID} -ne 0 ]]; then
    echo 'run as root with sudo' >&2
    exit 1
  fi
}

require_commands() {
  local missing=() command_name
  for command_name in "$@"; do
    command -v "$command_name" >/dev/null 2>&1 || missing+=("$command_name")
  done
  if ((${#missing[@]})); then
    echo "missing required commands: ${missing[*]}" >&2
    exit 1
  fi
}

load_deploy_config() {
  local config_path="${1:-/etc/erp/deploy.env}"
  [[ -f $config_path && -r $config_path ]] || { echo "deploy config is not readable: $config_path" >&2; exit 1; }
  local mode owner
  mode="$(stat -c '%a' "$config_path")"
  owner="$(stat -c '%U:%G' "$config_path")"
  [[ $owner == root:root && $mode =~ ^(600|400)$ ]] || {
    echo 'deploy config must be owned by root:root with mode 0600 or 0400' >&2
    exit 1
  }

  local key value
  while IFS='=' read -r key value; do
    [[ -z $key || $key == \#* ]] && continue
    case "$key" in
      ERP_FLYWAY_URL|ERP_FLYWAY_USER|ERP_FLYWAY_PASSWORD|ERP_BACKUP_HOST|ERP_BACKUP_PORT|ERP_BACKUP_DATABASE|ERP_BACKUP_USER|ERP_AGE_RECIPIENT|ERP_PUBLIC_HEALTH_URL|ERP_EXPECTED_HOST|ERP_BACKUP_RETENTION_DAYS|ERP_RELEASE_RETENTION)
        declare -gx "$key=$value"
        ;;
      *) echo "unknown deploy config key: $key" >&2; exit 1 ;;
    esac
  done < "$config_path"
}

require_config_values() {
  local name
  for name in "$@"; do
    [[ -n ${!name:-} && ${!name} != CHANGE_ME* && ${!name} != age1CHANGE_ME ]] || {
      echo "missing deploy config value: $name" >&2
      exit 1
    }
  done
}

assert_safe_archive() {
  local archive_path="$1" entry
  while IFS= read -r entry; do
    [[ -n $entry ]] || continue
    [[ $entry != /* && $entry != ../* && $entry != */../* && $entry != *'/..' ]] || {
      echo "unsafe archive entry: $entry" >&2
      exit 1
    }
  done < <(tar -tzf "$archive_path")
}

verify_release_tree() {
  local release_root="$1"
  local manifest="$release_root/release-manifest.json"
  [[ -f $manifest ]] || { echo 'release manifest is missing' >&2; exit 1; }
  jq -e '.formatVersion == 1 and (.version | type == "string") and
    (.gitCommit | test("^[0-9a-f]{40}$")) and .runtime == "linux-x64-framework-dependent" and
    (.schema.min | type == "string") and (.schema.max | type == "string") and (.files | type == "array")' \
    "$manifest" >/dev/null || { echo 'release manifest is invalid' >&2; exit 1; }

  local expected actual file_path file_size file_hash
  expected="$(mktemp)"
  actual="$(mktemp)"
  jq -r '.files[].path' "$manifest" | LC_ALL=C sort > "$expected"
  find "$release_root" -type f ! -name release-manifest.json -printf '%P\n' | LC_ALL=C sort > "$actual"
  cmp -s "$expected" "$actual" || { rm -f -- "$expected" "$actual"; echo 'release file list differs from manifest' >&2; exit 1; }
  [[ $(wc -l < "$expected") -eq $(sort -u "$expected" | wc -l) ]] || {
    rm -f -- "$expected" "$actual"
    echo 'manifest contains duplicate paths' >&2
    exit 1
  }

  while IFS=$'\t' read -r file_path file_size file_hash; do
    [[ $file_path != /* && $file_path != ../* && $file_path != */../* ]] || { echo 'manifest path escapes release root' >&2; exit 1; }
    [[ -f $release_root/$file_path ]] || { echo "manifest file is missing: $file_path" >&2; exit 1; }
    [[ $(stat -c '%s' "$release_root/$file_path") == "$file_size" ]] || { echo "file size mismatch: $file_path" >&2; exit 1; }
    [[ $(sha256sum "$release_root/$file_path" | awk '{print $1}') == "$file_hash" ]] || { echo "file digest mismatch: $file_path" >&2; exit 1; }
  done < <(jq -r '.files[] | [.path, (.size|tostring), .sha256] | @tsv' "$manifest")
  rm -f -- "$expected" "$actual"
}
