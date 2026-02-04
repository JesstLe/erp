#!/usr/bin/env bash
set -euo pipefail

if command -v docker >/dev/null 2>&1 && (docker compose version >/dev/null 2>&1 || command -v docker-compose >/dev/null 2>&1); then
  echo "Docker/Compose 已安装。"
  exit 0
fi

if ! command -v curl >/dev/null 2>&1; then
  echo "缺少 curl，无法继续自动安装。请先安装 curl。" >&2
  exit 1
fi

echo "开始安装 Docker（使用官方安装脚本）..."
curl -fsSL https://get.docker.com | sh

if command -v systemctl >/dev/null 2>&1; then
  systemctl enable --now docker || true
fi

if ! command -v docker >/dev/null 2>&1; then
  echo "Docker 安装失败，请手动排查。" >&2
  exit 1
fi

if docker compose version >/dev/null 2>&1; then
  echo "Docker Compose 可用。"
  exit 0
fi

if command -v docker-compose >/dev/null 2>&1; then
  echo "docker-compose 可用。"
  exit 0
fi

echo "Docker 已安装，但 Compose 不可用。请按系统包管理器安装 docker-compose-plugin 或安装 docker-compose 二进制。" >&2
exit 1

