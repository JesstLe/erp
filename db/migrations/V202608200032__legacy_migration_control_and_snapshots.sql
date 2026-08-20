-- Controlled, idempotent legacy imports. Financial values remain non-spendable snapshots.
CREATE TABLE legacy_migration_runs (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id) ON DELETE RESTRICT,
    source_system varchar(80) NOT NULL,
    source_fingerprint_sha256 varchar(64) NOT NULL,
    import_version varchar(40) NOT NULL,
    status varchar(24) NOT NULL,
    is_dry_run boolean NOT NULL,
    started_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz,
    counts jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT ck_legacy_migration_runs_fingerprint CHECK (source_fingerprint_sha256 ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_legacy_migration_runs_status CHECK (status IN ('Running', 'Completed', 'Failed', 'RolledBack')),
    CONSTRAINT uq_legacy_migration_runs UNIQUE (tenant_id, source_system, source_fingerprint_sha256, is_dry_run)
);

CREATE TABLE legacy_migration_record_maps (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id) ON DELETE RESTRICT,
    run_id uuid NOT NULL REFERENCES legacy_migration_runs(id) ON DELETE RESTRICT,
    source_entity varchar(80) NOT NULL,
    source_id varchar(160) NOT NULL,
    source_sha256 varchar(64) NOT NULL,
    target_table varchar(100) NOT NULL,
    target_id uuid NOT NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_legacy_migration_record_maps_hash CHECK (source_sha256 ~ '^[0-9a-f]{64}$'),
    CONSTRAINT uq_legacy_migration_record_maps_source UNIQUE (tenant_id, source_entity, source_id)
);
CREATE INDEX ix_legacy_migration_record_maps_run ON legacy_migration_record_maps(run_id);

CREATE TABLE legacy_migration_exceptions (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id) ON DELETE RESTRICT,
    run_id uuid NOT NULL REFERENCES legacy_migration_runs(id) ON DELETE RESTRICT,
    source_entity varchar(80) NOT NULL,
    source_id varchar(160),
    field_name varchar(100),
    code varchar(80) NOT NULL,
    severity varchar(16) NOT NULL,
    detail varchar(500) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_legacy_migration_exceptions_severity CHECK (severity IN ('Info', 'Warning', 'Error'))
);
CREATE INDEX ix_legacy_migration_exceptions_run ON legacy_migration_exceptions(run_id, severity, code);

CREATE TABLE legacy_customer_financial_snapshots (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id) ON DELETE RESTRICT,
    run_id uuid NOT NULL REFERENCES legacy_migration_runs(id) ON DELETE RESTRICT,
    customer_id uuid NOT NULL REFERENCES customers(id) ON DELETE RESTRICT,
    source_customer_id varchar(160) NOT NULL,
    source_card_reference_ciphertext text,
    source_member_money_minor bigint,
    source_member_bonus_minor bigint,
    source_member_sbonus_minor bigint,
    source_member_store_minor bigint,
    source_member_credit_minor bigint,
    source_member_arrear_minor bigint,
    source_member_score numeric(18,4),
    is_spendable boolean NOT NULL DEFAULT false,
    captured_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_legacy_customer_financial_snapshots_not_spendable CHECK (is_spendable = false),
    CONSTRAINT uq_legacy_customer_financial_snapshots_source UNIQUE (tenant_id, source_customer_id)
);
CREATE INDEX ix_legacy_customer_financial_snapshots_customer
    ON legacy_customer_financial_snapshots(tenant_id, customer_id);

CREATE OR REPLACE FUNCTION reject_legacy_append_only_change() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'legacy migration evidence is append-only';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER tr_legacy_migration_record_maps_append_only
BEFORE UPDATE OR DELETE ON legacy_migration_record_maps
FOR EACH ROW EXECUTE FUNCTION reject_legacy_append_only_change();

CREATE TRIGGER tr_legacy_customer_financial_snapshots_append_only
BEFORE UPDATE OR DELETE ON legacy_customer_financial_snapshots
FOR EACH ROW EXECUTE FUNCTION reject_legacy_append_only_change();
