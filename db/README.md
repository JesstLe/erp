# 数据库迁移

`migrations/` 是正式数据库结构的唯一来源。文件一旦进入共享历史不得修改，只能新增后续迁移；EF Core 模型用于运行时映射，不使用 `EnsureCreated` 或 EF 自动迁移修改数据库。

本地一次性开发库初始化或临时空库验证：

```bash
set -a; source .env; set +a
docker compose up -d postgres
for migration in db/migrations/V*.sql; do
  PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" \
    -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f "$migration" || exit 1
done
```

上述循环不建立 `flyway_schema_history`，只允许用于一次性本地开发库和会删除的空库验证。共享测试与发布环境必须使用 `deploy/windows/Invoke-DatabaseMigration.ps1` 调用 Flyway，禁止把手工执行过 SQL 的非空库自动 baseline。
