## 你现在这台云服务器，需要提供信息吗？
- **如果你只想要“流程模板/交付文档”**：不需要提供任何服务器信息；我可以把“怎么部署到 Lighthouse”整理成固定步骤，方便你手动照抄执行。
- **如果你想要“你说一句话→我直接连上服务器把项目部署好”**：需要提供最少的 SSH 连接信息；这些信息的作用是让自动化脚本/Skill 能“登录服务器、上传/拉取代码、执行 docker compose、做健康检查”。

## 需要提供哪些信息（用于自动执行）
- **必需**
  - 服务器公网 IP 或域名（用于 SSH 连接与部署后验收）
  - SSH 用户名（常见：root/ubuntu）
  - SSH 端口（默认 22）
  - 登录方式（二选一）：SSH 私钥路径（推荐）或密码（不推荐写入任何仓库文件）
- **可选（有了更自动）**
  - 远端部署目录（默认建议 /opt/erp 或 /opt/jshERP）
  - 代码分支（默认 main/master）
  - 站点域名（用于写入 nginx server_name；仓库当前 [nginx.conf](file:///Users/lv/Workspace/erp/nginx.conf) 里是 bozuerp.eu.cc）

## 这些信息“提供了之后有什么用”
- Skill 能从“你的一句话”推导出完整动作链：
  - SSH 连通性检查 →（必要时）安装 Docker/Compose → 同步代码 → `docker compose up -d --build` → `curl` 验收 → 输出访问地址/回滚命令。
- 仓库里已经有 Docker 部署入口（[docker-compose.yml](file:///Users/lv/Workspace/erp/docker-compose.yml)、[Dockerfile](file:///Users/lv/Workspace/erp/Dockerfile)、[Dockerfile.web](file:///Users/lv/Workspace/erp/Dockerfile.web)），所以自动化可以直接复用现成方案。
- 另外仓库已有 [.codebuddy/integration/lighthouse.json](file:///Users/lv/Workspace/erp/.codebuddy/integration/lighthouse.json) 的 `previewUrl`，可用来做“部署后打开/验收”的默认地址，但它**不能替代 SSH 凭据**（只能告诉我们访问哪里，不等于能登录服务器）。

## 信息如何保存（避免泄露）
- 我会把敏感信息（SSH 私钥/密码）放在**本地环境变量或本地忽略文件**（例如 `scripts/lighthouse/.env.local` 并加入 `.gitignore`），保证不会提交到仓库。
- 仓库里只放 `*.env.example` 模板。

## 计划实现（你确认后我会直接落地）
### 1）新增 Skill：一句话触发“Lighthouse 部署模式”
- 新增 `.trae/skills/lighthouse-deploy/SKILL.md`
- 触发词包含："帮我在lighthouse部署这个项目"、"部署到轻量"、"部署最新版本" 等
- 支持两种运行形态：
  - **无凭据模式**：输出可复制粘贴的最短部署命令清单
  - **自动执行模式**：读取本地 `.env.local`（若存在）后直接执行远程部署与验收

### 2）新增一键脚本：把部署步骤固化成可重复执行的命令
- `scripts/lighthouse/deploy-compose.sh`：SSH 到服务器后用 [docker-compose.yml](file:///Users/lv/Workspace/erp/docker-compose.yml) 一键拉起
- `scripts/lighthouse/bootstrap-docker.sh`：在缺失时安装 Docker/Compose（兼容 OpenCloudOS/CentOS/Ubuntu）
- `scripts/lighthouse/.env.example`：变量模板（不含任何真实密钥）

### 3）新增 VS Code Task：实现“点一下就部署”
- `.vscode/tasks.json`：任务“部署到 Lighthouse（Docker Compose）”，直接调用部署脚本

### 4）新增文档：把关键点写进仓库
- `docs/部署到Lighthouse.md`：安全组/端口、目录规划、部署/回滚/备份、验收接口

### 5）验收
- 自动验证：前端 `/` + 后端经 Nginx 反代的 `/jshERP-boot/` 可访问

## 可选增强
- 需要“push 即部署”时，再加 CI 工作流（GitHub Actions/GitLab CI），用 Secrets 存 SSH Key 后远程执行同一套脚本。
