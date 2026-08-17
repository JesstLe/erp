# 门店 ERP

这是依据 `docs/PRD.md` 从空库开发的新 ERP。V1 采用 .NET 10 模块化单体、PostgreSQL 18 和 React 19，首个闭环覆盖门店授权、目录价格、设施接待计时、顾客会员、服务录单、人工支付、交班和审计。

## 当前状态

工程正在按纵向模块持续实现。已完成能力、运行方法和未确认事项分别记录在：

- [CHANGELOG](CHANGELOG.md)
- [开发进度](docs/development-progress.md)
- [用户手册](docs/user-manual/README.md)
- [实现假设与待确认项](docs/implementation-assumptions.md)

## 本地开发

所需工具：.NET SDK 10.0.301、Node.js 24、Docker。

1. 将 `.env.example` 复制为 `.env`，替换所有 `CHANGE_ME`。
2. 启动数据库：`docker compose up -d postgres`。
3. 安装前端依赖：`npm --prefix apps/web ci`。
4. 载入配置：`set -a; source .env; set +a`。
5. 启动 API：`dotnet run --project apps/api/Erp.Api`。
6. 启动前端：`npm --prefix apps/web run dev`。

不会在仓库中提供可用于正式环境的默认密码。开发种子账号仅在明确设置 `ERP_SEED_OWNER_PASSWORD` 后创建。

## 测试策略

日常开发优先运行受影响项目的单元或组件测试；模块完成时运行对应集成测试；首个完整闭环完成后才执行解决方案、前端和端到端全量回归。
