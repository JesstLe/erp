-- Append-only evidence for money-first incremental synchronization from the legacy system.
CREATE TABLE legacy_migration_record_revisions (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id) ON DELETE RESTRICT,
    run_id uuid NOT NULL REFERENCES legacy_migration_runs(id) ON DELETE RESTRICT,
    source_entity varchar(80) NOT NULL,
    source_id varchar(160) NOT NULL,
    source_sha256 varchar(64) NOT NULL,
    previous_source_sha256 varchar(64) NOT NULL,
    target_table varchar(100) NOT NULL,
    target_id uuid NOT NULL,
    captured_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_legacy_migration_record_revisions_hashes CHECK (
        source_sha256 ~ '^[0-9a-f]{64}$' AND previous_source_sha256 ~ '^[0-9a-f]{64}$'),
    CONSTRAINT uq_legacy_migration_record_revisions_source_hash
        UNIQUE (tenant_id, source_entity, source_id, source_sha256)
);
CREATE INDEX ix_legacy_migration_record_revisions_latest
    ON legacy_migration_record_revisions(tenant_id, source_entity, source_id, captured_at_utc DESC);

CREATE TABLE legacy_customer_financial_revisions (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id) ON DELETE RESTRICT,
    run_id uuid NOT NULL REFERENCES legacy_migration_runs(id) ON DELETE RESTRICT,
    customer_id uuid NOT NULL REFERENCES customers(id) ON DELETE RESTRICT,
    source_customer_id varchar(160) NOT NULL,
    source_sha256 varchar(64) NOT NULL,
    source_member_money_minor bigint,
    source_member_bonus_minor bigint,
    source_member_sbonus_minor bigint,
    source_member_store_minor bigint,
    source_member_credit_minor bigint,
    source_member_arrear_minor bigint,
    source_member_score numeric(18,4),
    principal_delta_minor bigint NOT NULL,
    bonus_delta_minor bigint NOT NULL,
    captured_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_legacy_customer_financial_revisions_hash CHECK (source_sha256 ~ '^[0-9a-f]{64}$'),
    CONSTRAINT uq_legacy_customer_financial_revisions_source_hash
        UNIQUE (tenant_id, source_customer_id, source_sha256)
);
CREATE INDEX ix_legacy_customer_financial_revisions_latest
    ON legacy_customer_financial_revisions(tenant_id, source_customer_id, captured_at_utc DESC);

CREATE TRIGGER tr_legacy_migration_record_revisions_append_only
BEFORE UPDATE OR DELETE ON legacy_migration_record_revisions
FOR EACH ROW EXECUTE FUNCTION reject_legacy_append_only_change();

CREATE TRIGGER tr_legacy_customer_financial_revisions_append_only
BEFORE UPDATE OR DELETE ON legacy_customer_financial_revisions
FOR EACH ROW EXECUTE FUNCTION reject_legacy_append_only_change();
