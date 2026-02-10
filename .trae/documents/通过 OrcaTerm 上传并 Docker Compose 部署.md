## 你要的两种方案（都行，我建议先选 A）

## 方案 A：我在本地把“需要上传的文件”打包好，你拖到服务器解压更新
### A1）我会帮你打包什么
- 生成一个 `tar.gz` 更新包，包含部署必须文件：
  - `docker-compose.yml`、`Dockerfile`、`Dockerfile.web`、`nginx.conf`
  - `jshERP-boot/`、`jshERP-web/`（源码）
  - `jshERP-boot/docs/jsh_erp.sql`（首次安装用，更新时不会重复导入）
- 自动排除体积/无用目录：
  - `.git/`、`jshERP-web/node_modules/`、`jshERP-boot/target/`、日志等

### A2）你在服务器怎么用（更新到已部署目录）
你上次目录是：`/root/erp_20260205004713`
- 上传更新包到服务器（例如 `/root/`）
- 在服务器执行（覆盖更新，不动数据卷）：
  - `cd /root/erp_20260205004713`
  - `tar -xzf /root/<更新包名>.tar.gz -C /root/erp_20260205004713`
  - `docker compose up -d --build`

说明：
- 这会更新 web/backend；MySQL/Redis 数据卷不会被清空（除非手动删 volume）。

### A3）你确认后我会做的落地动作
- 在仓库内新增一个本地打包脚本（可重复使用），并在本机直接生成一个可上传的 `tar.gz` 文件。


## 方案 B：不上传文件，服务器端直接重新 docker 部署一遍（拉 GitHub）
适合你不想拖文件、且服务器能访问 GitHub。

### B1）在服务器重新拉起（不清数据）
在 `/root/erp_20260205004713`：
- `docker compose down`（不加 `-v`，避免删数据卷）
- `docker compose up -d --build`

### B2）如果你上次目录其实是 git clone 出来的（有 `.git`）
- `cd /root/erp_20260205004713`
- `git pull --ff-only`
- `docker compose up -d --build`


## 验收（两种方案都一样）
- `curl -I http://127.0.0.1/`
- `curl -I http://127.0.0.1/jshERP-boot/`
- 浏览器打开：`http://118.89.83.99/`

## 你确认后我建议执行的选择
- 如果你更倾向“拖拽文件”：选 **方案 A**（我打包给你，你上传解压即可）
- 如果你更倾向“不上传”：选 **方案 B**（服务器 git pull/重建）
