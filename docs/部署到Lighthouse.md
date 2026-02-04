# 部署到 Tencent Lighthouse（Docker Compose）

本项目已在仓库根目录提供 Docker Compose 一键部署能力，适合部署到腾讯云 Lighthouse（轻量服务器）。

## 你需要准备的信息

- 服务器公网 IP 或域名（用于 SSH 与验收访问）
- SSH 用户名与端口（默认 22）
- SSH 私钥路径（推荐）或密码（不建议持久化保存）

## 服务器侧准备（建议）

- 安全组/防火墙：
  - 放行：80（必要），443（如需 HTTPS）
  - SSH：22 仅允许管理 IP
  - 不对公网开放：9999、3306、6379
- 服务器需具备 Docker 与 Docker Compose（轻量镜像通常已包含 Docker）

## 仓库内的部署入口

- Docker Compose： [docker-compose.yml](file:///Users/lv/Workspace/erp/docker-compose.yml)
- 后端镜像： [Dockerfile](file:///Users/lv/Workspace/erp/Dockerfile)
- 前端镜像： [Dockerfile.web](file:///Users/lv/Workspace/erp/Dockerfile.web)
- Nginx 反代： [nginx.conf](file:///Users/lv/Workspace/erp/nginx.conf)

## 一键部署（推荐）

### 1）准备本地配置文件（不会提交到仓库）

在本机执行：

1. 复制示例文件：
   - `scripts/lighthouse/.env.example` → `scripts/lighthouse/.env.local`
2. 按你的服务器信息修改 `scripts/lighthouse/.env.local`：
   - `SSH_HOST`、`SSH_USER`、`SSH_PORT`
   - `SSH_KEY`（推荐）或使用 SSH Agent
   - `REPO_URL`（仓库地址）
   - `REMOTE_DIR`（远端部署目录，默认 `/opt/erp`）
   - `VERIFY_URL`（部署后验收地址，例如 `http://你的IP`）

说明：`scripts/lighthouse/.env.local` 已被 `.gitignore` 忽略，不会被提交。

### 2）执行部署

在仓库根目录执行：

```bash
bash scripts/lighthouse/deploy-compose.sh --env-file scripts/lighthouse/.env.local
```

脚本会在服务器上完成：

- 初始化/更新代码
- `docker compose up -d --build`
- （可选）根据 `VERIFY_URL` 做 HTTP 验收

### 3）VS Code 一键执行

仓库已提供 VS Code 任务：

- “部署到 Lighthouse（Docker Compose）”
- “检查 Lighthouse 部署配置”

可在 VS Code 的任务列表中直接运行。

## 验收方式（最短路径）

- 前端：`GET http://<host>/`
- 后端（经 Nginx 反代）：`GET http://<host>/jshERP-boot/`

## 回滚与运维（常用命令）

进入服务器的部署目录（默认 `/opt/erp`）后：

- 查看容器：`docker ps`
- 查看日志：`docker compose logs -f --tail=200`
- 重启：`docker compose restart`
- 停止：`docker compose down`

如果你用 git 部署，回滚可以通过 `git checkout <旧版本> && docker compose up -d --build` 完成。

## 常见问题

- 远端缺少 Docker：在服务器上执行 `bash scripts/lighthouse/bootstrap-docker.sh`
- 远端没有 Compose：安装 `docker-compose-plugin`（推荐）或 `docker-compose`
- `REPO_URL` 为空：脚本需要能在服务器上拉取代码；请配置仓库地址并保证服务器可访问

