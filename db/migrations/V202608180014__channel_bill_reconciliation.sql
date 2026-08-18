-- V2 channel bill reconciliation. Provider bill files are normalized into immutable run/items;
-- only hashes and business identifiers are retained, never customer identities or raw bill files.

ALTER TABLE payment_channel_refunds
    ADD COLUMN reconciliation_status varchar(32) NOT NULL DEFAULT 'Pending',
    ADD CONSTRAINT ck_payment_channel_refunds_reconciliation CHECK (
        reconciliation_status IN ('Pending', 'Matched', 'Difference', 'Resolved'));

CREATE TABLE payment_channel_reconciliation_runs (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    configuration_id uuid NOT NULL REFERENCES payment_channel_configurations(id) ON DELETE RESTRICT,
    provider varchar(24) NOT NULL,
    business_date date NOT NULL,
    attempt_no integer NOT NULL,
    status varchar(24) NOT NULL,
    started_by uuid NOT NULL REFERENCES identity_users(id),
    started_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz,
    channel_entry_count integer NOT NULL DEFAULT 0,
    matched_count integer NOT NULL DEFAULT 0,
    difference_count integer NOT NULL DEFAULT 0,
    source_sha256 bytea,
    failure_code varchar(80),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_channel_reconciliation_run_attempt UNIQUE (
        configuration_id, business_date, attempt_no),
    CONSTRAINT ck_channel_reconciliation_run_provider CHECK (provider IN ('WeChatPay', 'Alipay')),
    CONSTRAINT ck_channel_reconciliation_run_attempt CHECK (attempt_no BETWEEN 1 AND 100),
    CONSTRAINT ck_channel_reconciliation_run_status CHECK (
        status IN ('Running', 'Matched', 'Differences', 'Failed')),
    CONSTRAINT ck_channel_reconciliation_run_counts CHECK (
        channel_entry_count >= 0 AND matched_count >= 0 AND difference_count >= 0),
    CONSTRAINT ck_channel_reconciliation_run_completion CHECK (
        (status = 'Running' AND completed_at_utc IS NULL AND source_sha256 IS NULL AND failure_code IS NULL) OR
        (status IN ('Matched', 'Differences') AND completed_at_utc IS NOT NULL AND
            octet_length(source_sha256) = 32 AND failure_code IS NULL) OR
        (status = 'Failed' AND completed_at_utc IS NOT NULL AND source_sha256 IS NULL AND failure_code IS NOT NULL))
);

CREATE INDEX ix_channel_reconciliation_runs_store_date
    ON payment_channel_reconciliation_runs (store_id, business_date DESC, started_at_utc DESC);
CREATE UNIQUE INDEX uq_channel_reconciliation_run_active
    ON payment_channel_reconciliation_runs (configuration_id, business_date)
    WHERE status = 'Running';

CREATE TABLE payment_channel_reconciliation_items (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    run_id uuid NOT NULL REFERENCES payment_channel_reconciliation_runs(id) ON DELETE RESTRICT,
    item_type varchar(16) NOT NULL,
    status varchar(24) NOT NULL,
    match_key varchar(160) NOT NULL,
    out_trade_no varchar(64),
    out_refund_no varchar(64),
    provider_trade_no varchar(128),
    payment_allocation_id uuid REFERENCES payment_allocations(id) ON DELETE RESTRICT,
    channel_refund_id uuid REFERENCES payment_channel_refunds(id) ON DELETE RESTRICT,
    local_amount_minor bigint,
    channel_amount_minor bigint,
    channel_fee_minor bigint NOT NULL DEFAULT 0,
    local_status varchar(40),
    channel_status varchar(80),
    resolved_by uuid REFERENCES identity_users(id),
    resolved_at_utc timestamptz,
    resolution_reason varchar(500),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_channel_reconciliation_item_key UNIQUE (run_id, match_key),
    CONSTRAINT ck_channel_reconciliation_item_type CHECK (item_type IN ('Payment', 'Refund')),
    CONSTRAINT ck_channel_reconciliation_item_status CHECK (status IN (
        'Matched', 'LocalOnly', 'ChannelOnly', 'AmountMismatch', 'StateMismatch', 'Resolved')),
    CONSTRAINT ck_channel_reconciliation_item_identifier CHECK (
        (item_type = 'Payment' AND out_trade_no IS NOT NULL) OR
        (item_type = 'Refund' AND out_refund_no IS NOT NULL)),
    CONSTRAINT ck_channel_reconciliation_item_link CHECK (
        (item_type = 'Payment' AND channel_refund_id IS NULL) OR
        (item_type = 'Refund' AND payment_allocation_id IS NULL)),
    CONSTRAINT ck_channel_reconciliation_item_amount CHECK (
        (local_amount_minor IS NULL OR local_amount_minor >= 0) AND
        (channel_amount_minor IS NULL OR channel_amount_minor >= 0) AND
        channel_fee_minor >= 0),
    CONSTRAINT ck_channel_reconciliation_item_presence CHECK (
        (status = 'LocalOnly' AND local_amount_minor IS NOT NULL AND channel_amount_minor IS NULL) OR
        (status = 'ChannelOnly' AND local_amount_minor IS NULL AND channel_amount_minor IS NOT NULL) OR
        (status IN ('Matched', 'AmountMismatch', 'StateMismatch') AND
            local_amount_minor IS NOT NULL AND channel_amount_minor IS NOT NULL) OR
        status = 'Resolved'),
    CONSTRAINT ck_channel_reconciliation_item_resolution CHECK (
        (status <> 'Resolved' AND resolved_by IS NULL AND resolved_at_utc IS NULL AND resolution_reason IS NULL) OR
        (status = 'Resolved' AND resolved_by IS NOT NULL AND resolved_at_utc IS NOT NULL AND
            char_length(resolution_reason) BETWEEN 1 AND 500))
);

CREATE INDEX ix_channel_reconciliation_items_run_status
    ON payment_channel_reconciliation_items (run_id, status);
CREATE INDEX ix_channel_reconciliation_items_payment
    ON payment_channel_reconciliation_items (payment_allocation_id)
    WHERE payment_allocation_id IS NOT NULL;
CREATE INDEX ix_channel_reconciliation_items_refund
    ON payment_channel_reconciliation_items (channel_refund_id)
    WHERE channel_refund_id IS NOT NULL;
