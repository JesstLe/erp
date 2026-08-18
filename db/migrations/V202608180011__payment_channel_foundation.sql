-- V2 payment-channel foundation. Secret material is deliberately excluded from the database.

CREATE TABLE payment_channel_configurations (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    provider varchar(24) NOT NULL,
    environment varchar(16) NOT NULL,
    display_name varchar(80) NOT NULL,
    credential_profile varchar(40) NOT NULL,
    is_enabled boolean NOT NULL DEFAULT false,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_payment_channel_configuration UNIQUE (tenant_id, store_id, provider),
    CONSTRAINT ck_payment_channel_configuration_provider CHECK (provider IN ('WeChatPay', 'Alipay')),
    CONSTRAINT ck_payment_channel_configuration_environment CHECK (environment IN ('Sandbox', 'Production')),
    CONSTRAINT ck_payment_channel_configuration_profile CHECK (
        credential_profile ~ '^[A-Z][A-Z0-9_]{2,39}$')
);

CREATE INDEX ix_payment_channel_configurations_store_enabled
    ON payment_channel_configurations (store_id, is_enabled, provider);

CREATE TABLE payment_channel_orders (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    configuration_id uuid NOT NULL REFERENCES payment_channel_configurations(id) ON DELETE RESTRICT,
    payment_allocation_id uuid NOT NULL REFERENCES payment_allocations(id) ON DELETE RESTRICT,
    provider varchar(24) NOT NULL,
    out_trade_no varchar(64) NOT NULL,
    attempt_no integer NOT NULL,
    amount_minor bigint NOT NULL,
    currency varchar(3) NOT NULL,
    subject varchar(120) NOT NULL,
    status varchar(24) NOT NULL,
    qr_payload varchar(2048),
    provider_trade_no varchar(128),
    failure_code varchar(80),
    expires_at_utc timestamptz NOT NULL,
    paid_at_utc timestamptz,
    closed_at_utc timestamptz,
    last_queried_at_utc timestamptz,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_payment_channel_order_trade_no UNIQUE (tenant_id, provider, out_trade_no),
    CONSTRAINT uq_payment_channel_order_attempt UNIQUE (payment_allocation_id, attempt_no),
    CONSTRAINT ck_payment_channel_order_provider CHECK (provider IN ('WeChatPay', 'Alipay')),
    CONSTRAINT ck_payment_channel_order_status CHECK (
        status IN ('Created', 'QrReady', 'Paid', 'Closed', 'Failed', 'Expired')),
    CONSTRAINT ck_payment_channel_order_amount CHECK (
        amount_minor > 0 AND amount_minor <= 10000000000 AND currency = 'CNY'),
    CONSTRAINT ck_payment_channel_order_attempt CHECK (attempt_no BETWEEN 1 AND 100),
    CONSTRAINT ck_payment_channel_order_qr CHECK (
        (status = 'Created' AND qr_payload IS NULL AND provider_trade_no IS NULL AND paid_at_utc IS NULL) OR
        (status = 'QrReady' AND qr_payload IS NOT NULL AND provider_trade_no IS NULL AND paid_at_utc IS NULL) OR
        (status = 'Paid' AND provider_trade_no IS NOT NULL AND paid_at_utc IS NOT NULL) OR
        (status IN ('Closed', 'Failed', 'Expired') AND provider_trade_no IS NULL AND paid_at_utc IS NULL)),
    CONSTRAINT ck_payment_channel_order_failure CHECK (
        (status = 'Failed' AND failure_code IS NOT NULL) OR
        (status <> 'Failed' AND failure_code IS NULL)),
    CONSTRAINT ck_payment_channel_order_closed CHECK (
        (status = 'Closed' AND closed_at_utc IS NOT NULL) OR
        (status <> 'Closed' AND closed_at_utc IS NULL))
);

CREATE UNIQUE INDEX uq_payment_channel_order_active_allocation
    ON payment_channel_orders (payment_allocation_id)
    WHERE status IN ('Created', 'QrReady');
CREATE INDEX ix_payment_channel_orders_configuration_status
    ON payment_channel_orders (configuration_id, status, created_at_utc DESC);
CREATE INDEX ix_payment_channel_orders_provider_trade
    ON payment_channel_orders (provider, provider_trade_no)
    WHERE provider_trade_no IS NOT NULL;
CREATE INDEX ix_payment_channel_orders_expiry
    ON payment_channel_orders (expires_at_utc)
    WHERE status IN ('Created', 'QrReady');

CREATE TABLE payment_channel_events (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    configuration_id uuid NOT NULL REFERENCES payment_channel_configurations(id) ON DELETE RESTRICT,
    channel_order_id uuid REFERENCES payment_channel_orders(id) ON DELETE RESTRICT,
    provider varchar(24) NOT NULL,
    provider_event_id varchar(128) NOT NULL,
    event_type varchar(80) NOT NULL,
    payload_sha256 bytea NOT NULL,
    status varchar(24) NOT NULL,
    received_at_utc timestamptz NOT NULL,
    processed_at_utc timestamptz,
    error_code varchar(80),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_payment_channel_event UNIQUE (configuration_id, provider_event_id),
    CONSTRAINT ck_payment_channel_event_provider CHECK (provider IN ('WeChatPay', 'Alipay')),
    CONSTRAINT ck_payment_channel_event_digest CHECK (octet_length(payload_sha256) = 32),
    CONSTRAINT ck_payment_channel_event_status CHECK (
        status IN ('Received', 'Processed', 'Ignored', 'Failed')),
    CONSTRAINT ck_payment_channel_event_completion CHECK (
        (status = 'Received' AND processed_at_utc IS NULL AND error_code IS NULL) OR
        (status IN ('Processed', 'Ignored') AND processed_at_utc IS NOT NULL AND error_code IS NULL) OR
        (status = 'Failed' AND processed_at_utc IS NOT NULL AND error_code IS NOT NULL))
);

CREATE INDEX ix_payment_channel_events_order_received
    ON payment_channel_events (channel_order_id, received_at_utc DESC);
