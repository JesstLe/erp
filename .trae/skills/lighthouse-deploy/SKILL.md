---
name: "lighthouse-deploy"
description: "将项目部署到腾讯云 Lighthouse（轻量服务器）。当用户说“帮我在lighthouse部署这个项目/部署最新版本/部署到轻量”时调用，自动选择 Docker Compose 方案并执行验收。"
---

# Lighthouse 一键部署

## 适用场景（何时调用）

- 用户明确要把当前项目部署到腾讯云 Lighthouse/轻量服务器
- 用户说“部署最新版本”“一键部署”“上线到云服务器”“帮我在lighthouse部署这个项目”
- 用户点击了“部署”类按钮（该按钮会自动注入类似的部署指令）

## 默认部署策略

- 优先使用仓库根目录的 Docker Compose 一键部署：
  - `docker-compose.yml`
  - `Dockerfile`（后端）
  - `Dockerfile.web`（前端 + Nginx）
  - `nginx.conf`（反代 `/jshERP-boot/` 到后端容器）
- 访问地址默认从 `.codebuddy/integration/lighthouse.json` 的 `previewUrl` 推断；若不存在则使用用户提供的域名/IP。

## 需要的信息（自动执行时）

### 必需（用于 SSH 连接）

- 服务器公网 IP 或域名（SSH_HOST）
- SSH 用户名（SSH_USER，常见 root/ubuntu）
- SSH 端口（SSH_PORT，默认 22）
- 登录凭据（二选一）：
  - SSH 私钥路径（SSH_KEY，推荐）
  - SSH 密码（不建议；不要写入任何会提交到仓库的文件）

### 可选（用于更自动化）

- 远端部署目录（REMOTE_DIR，建议 `/opt/erp` 或 `/opt/jshERP`）
- 部署分支（BRANCH，默认 main/master）
- 站点域名（SERVER_NAME，用于 Nginx server_name）

## 机密信息保存规则

- 任何密钥/密码只允许放在：
  - 本地环境变量，或
  - 本地忽略文件（例如 `scripts/lighthouse/.env.local`，并确保已被 `.gitignore` 忽略）
- 仓库内只允许提交 `*.env.example` 示例模板。

## 执行步骤（自动执行）

1. 读取本地配置：
   - 优先读取 `scripts/lighthouse/.env.local`（若存在）
   - 其次读取环境变量
2. 读取访问地址：
   - 优先读取 `.codebuddy/integration/lighthouse.json` 的 `previewUrl`
   - 否则用用户给出的域名/IP 作为访问地址
3. 预检查：
   - SSH 连通性（端口、用户名、凭据）
   - 远端 Docker/Compose 是否可用（缺失则引导安装或自动安装）
4. 部署：
   - 拉取或更新代码（git clone / git pull，或 rsync 上传）
   - 在项目根目录执行 `docker compose up -d --build`
5. 验收：
   - `GET /`（前端可访问）
   - `GET /jshERP-boot/` 或关键接口（后端经 Nginx 反代可访问）
6. 输出：
   - 访问地址、常用运维命令（查看日志、重启、回滚提示）

## 示例对话（最短触发）

用户：
> 帮我在lighthouse部署这个项目

期望行为：
- 自动选择 Docker Compose 部署路径
- 如缺少 SSH 信息，则列出“你需要提供的最少信息清单”
- 信息齐全则直接完成部署并返回访问地址与验收结果

