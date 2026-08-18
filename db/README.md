# 数据库迁移

`migrations/` 是正式数据库结构的唯一来源。文件一旦进入共享历史不得修改，只能新增后续迁移；EF Core 模型用于运行时映射，不使用 `EnsureCreated` 或 EF 自动迁移修改数据库。

本地首次初始化：

```bash
set -a; source .env; set +a
docker compose up -d postgres
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180001__baseline_system_catalog.sql
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180002__facilities_visits_and_timing.sql
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180003__customers_membership_and_accounts.sql
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180004__service_orders_and_price_snapshots.sql
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180005__payments_cashier_shifts_and_reconciliation.sql
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180006__employees_accounts_and_store_assignments.sql
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180007__product_catalog_and_versioned_prices.sql
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180008__member_topups_and_generalized_payments.sql
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180009__member_account_payments_and_verification.sql
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180010__service_payment_refunds.sql
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180011__payment_channel_foundation.sql
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180012__async_channel_payments.sql
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180013__channel_refunds.sql
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180014__channel_bill_reconciliation.sql
PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "${ERP_DB_PORT:-54318}" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f db/migrations/V202608180015__product_sales_and_inventory.sql
```

当前开发阶段可用上述命令快速验证迁移；CI 和发布环境接入 Flyway 后，仍使用同一批版本化 SQL。
