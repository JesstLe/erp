#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd -- "$SCRIPT_DIR/../.." && pwd)"

ENV_FILE="${ENV_FILE:-}"
CHECK_ONLY="${CHECK_ONLY:-0}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --env-file)
      ENV_FILE="${2:-}"
      shift 2
      ;;
    --check)
      CHECK_ONLY="1"
      shift
      ;;
    *)
      echo "不支持的参数：$1" >&2
      exit 2
      ;;
  esac
done

if [[ -z "$ENV_FILE" ]]; then
  if [[ -f "$SCRIPT_DIR/.env.local" ]]; then
    ENV_FILE="$SCRIPT_DIR/.env.local"
  elif [[ -f "$SCRIPT_DIR/.env" ]]; then
    ENV_FILE="$SCRIPT_DIR/.env"
  fi
fi

if [[ -n "$ENV_FILE" ]]; then
  if [[ ! -f "$ENV_FILE" ]]; then
    echo "环境文件不存在：$ENV_FILE" >&2
    exit 2
  fi
  set -a
  source "$ENV_FILE"
  set +a
fi

SSH_HOST="${SSH_HOST:-}"
SSH_USER="${SSH_USER:-}"
SSH_PORT="${SSH_PORT:-22}"
SSH_KEY="${SSH_KEY:-}"
SSH_EXTRA_OPTS="${SSH_EXTRA_OPTS:-}"

REMOTE_DIR="${REMOTE_DIR:-/opt/erp}"
BRANCH="${BRANCH:-}"
REPO_URL="${REPO_URL:-}"

VERIFY_URL="${VERIFY_URL:-}"
VERIFY_TIMEOUT_SECONDS="${VERIFY_TIMEOUT_SECONDS:-10}"

if [[ -z "$VERIFY_URL" ]]; then
  LIGHTHOUSE_JSON="$ROOT_DIR/.codebuddy/integration/lighthouse.json"
  if command -v python3 >/dev/null 2>&1 && [[ -f "$LIGHTHOUSE_JSON" ]]; then
    VERIFY_URL="$(python3 - <<'PY'
import json, pathlib
p = pathlib.Path(".codebuddy/integration/lighthouse.json")
try:
  data = json.loads(p.read_text(encoding="utf-8"))
  print((data.get("previewUrl") or "").strip())
except Exception:
  print("")
PY
)"
  fi
fi

if [[ -z "$SSH_HOST" ]]; then
  echo "缺少 SSH_HOST（服务器公网 IP 或域名）" >&2
  exit 2
fi
if [[ -z "$SSH_USER" ]]; then
  echo "缺少 SSH_USER（SSH 用户名，例如 root/ubuntu）" >&2
  exit 2
fi

if [[ -z "$REPO_URL" ]]; then
  if command -v git >/dev/null 2>&1; then
    REPO_URL="$(git -C "$ROOT_DIR" remote get-url origin 2>/dev/null || true)"
  fi
fi
if [[ -z "$REPO_URL" ]]; then
  echo "缺少 REPO_URL（仓库地址）。请在环境文件里配置，例如：https://... 或 git@..." >&2
  exit 2
fi

SSH_OPTS=(-p "$SSH_PORT" -o BatchMode=yes -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10)
if [[ -n "$SSH_KEY" ]]; then
  SSH_OPTS+=(-i "$SSH_KEY" -o IdentitiesOnly=yes)
fi
if [[ -n "$SSH_EXTRA_OPTS" ]]; then
  SSH_OPTS+=($SSH_EXTRA_OPTS)
fi

echo "目标服务器：$SSH_USER@$SSH_HOST:$SSH_PORT"
echo "远端目录：$REMOTE_DIR"
echo "仓库地址：$REPO_URL"

REMOTE_SSH_TARGET="$SSH_USER@$SSH_HOST"

echo "检查 SSH 连通性..."
ssh "${SSH_OPTS[@]}" "$REMOTE_SSH_TARGET" "echo ok" >/dev/null

if [[ "$CHECK_ONLY" == "1" ]]; then
  echo "检查远端 Docker/Compose..."
  ssh "${SSH_OPTS[@]}" "$REMOTE_SSH_TARGET" "command -v docker >/dev/null && docker version >/dev/null && (docker compose version >/dev/null 2>&1 || docker-compose version >/dev/null 2>&1)" >/dev/null || {
    echo "远端 Docker/Compose 不可用。请先执行 scripts/lighthouse/bootstrap-docker.sh（在远端或通过 SSH）" >&2
    exit 1
  }
  echo "检查通过。"
  exit 0
fi

echo "开始远端部署（Docker Compose）..."
ssh "${SSH_OPTS[@]}" "$REMOTE_SSH_TARGET" \
  "export REMOTE_DIR='$REMOTE_DIR' REPO_URL='$REPO_URL' BRANCH='${BRANCH}' && bash -s" <<'EOS'
set -euo pipefail

REMOTE_DIR="${REMOTE_DIR:?}"
REPO_URL="${REPO_URL:?}"
BRANCH="${BRANCH:-}"

mkdir -p "$REMOTE_DIR"
cd "$REMOTE_DIR"

if [[ ! -d .git ]]; then
  if [[ -n "$BRANCH" ]]; then
    git clone --branch "$BRANCH" --depth 1 "$REPO_URL" .
  else
    git clone "$REPO_URL" .
  fi
fi

git fetch --all --prune

if [[ -n "$BRANCH" ]]; then
  git checkout "$BRANCH"
else
  DEFAULT_BRANCH="$(git remote show origin 2>/dev/null | awk '/HEAD branch/ {print $NF}' || true)"
  if [[ -n "$DEFAULT_BRANCH" ]]; then
    git checkout "$DEFAULT_BRANCH"
  fi
fi

git pull --ff-only

if docker compose version >/dev/null 2>&1; then
  COMPOSE=(docker compose)
elif command -v docker-compose >/dev/null 2>&1; then
  COMPOSE=(docker-compose)
else
  echo "未检测到 docker compose / docker-compose，请先安装 Docker Compose" >&2
  exit 1
fi

"${COMPOSE[@]}" up -d --build
EOS

if [[ -n "$VERIFY_URL" ]]; then
  echo "验收地址：$VERIFY_URL"
  echo "验收前端 / ..."
  curl -fsS --max-time "$VERIFY_TIMEOUT_SECONDS" "$VERIFY_URL/" >/dev/null
  echo "验收后端 /jshERP-boot/ ..."
  curl -fsS --max-time "$VERIFY_TIMEOUT_SECONDS" "$VERIFY_URL/jshERP-boot/" >/dev/null || true
  echo "验收完成。"
else
  echo "未配置 VERIFY_URL，跳过自动验收。"
fi

echo "部署完成。"
