#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "$SCRIPT_DIR/../.." && pwd)"

OUTPUT_DIR="${OUTPUT_DIR:-$ROOT_DIR/dist/lighthouse}"
NAME_PREFIX="${NAME_PREFIX:-erp-upload}"

if command -v git >/dev/null 2>&1 && git -C "$ROOT_DIR" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  GIT_SHA="$(git -C "$ROOT_DIR" rev-parse --short HEAD 2>/dev/null || true)"
else
  GIT_SHA=""
fi

TS="$(date +%Y%m%d%H%M%S)"
if [[ -n "$GIT_SHA" ]]; then
  ARCHIVE_NAME="${NAME_PREFIX}-${TS}-${GIT_SHA}.tar.gz"
else
  ARCHIVE_NAME="${NAME_PREFIX}-${TS}.tar.gz"
fi

mkdir -p "$OUTPUT_DIR"
ARCHIVE_PATH="$OUTPUT_DIR/$ARCHIVE_NAME"

cd "$ROOT_DIR"

INCLUDE_PATHS=(
  docker-compose.yml
  Dockerfile
  Dockerfile.web
  nginx.conf
  .dockerignore
  jshERP-boot
  jshERP-web
)

export COPYFILE_DISABLE=1

TAR_OPTS=()
if tar --help 2>/dev/null | grep -q -- '--no-xattrs'; then
  TAR_OPTS+=(--no-xattrs)
fi
if tar --help 2>/dev/null | grep -q -- '--no-acls'; then
  TAR_OPTS+=(--no-acls)
fi
if tar --help 2>/dev/null | grep -q -- '--no-selinux'; then
  TAR_OPTS+=(--no-selinux)
fi

tar "${TAR_OPTS[@]}" -czf "$ARCHIVE_PATH" \
  --exclude="./.git" \
  --exclude="./.data" \
  --exclude="*/logs" \
  --exclude="*/logs/*" \
  --exclude="*/logs*" \
  --exclude="./jshERP-boot/target" \
  --exclude="./jshERP-boot/dist" \
  --exclude="./jshERP-boot/logs*" \
  --exclude="./jshERP-web/node_modules" \
  --exclude="./jshERP-web/dist" \
  "${INCLUDE_PATHS[@]}"

echo "$ARCHIVE_PATH"
