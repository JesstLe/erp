-- Configurable payment methods, atomic payment allocations and cashier-shift snapshots.

ALTER TABLE service_orders DROP CONSTRAINT ck_service_orders_status;
ALTER TABLE service_orders ADD CONSTRAINT ck_service_orders_status
    CHECK (status IN ('Draft', 'PendingPayment', 'PaymentProcessing', 'Settled', 'Voided'));

CREATE TABLE payment_methods (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    code varchar(40) NOT NULL,
    name varchar(80) NOT NULL,
    category varchar(32) NOT NULL,
    requires_open_shift boolean NOT NULL,
    is_enabled boolean NOT NULL DEFAULT true,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_payment_methods_tenant_code UNIQUE (tenant_id, code),
    CONSTRAINT ck_payment_methods_category CHECK (category IN ('Cash', 'ManualExternal', 'InternalAccount'))
);
CREATE INDEX ix_payment_methods_tenant_id ON payment_methods (tenant_id);

INSERT INTO payment_methods (id, tenant_id, code, name, category, requires_open_shift, is_enabled,
    created_at_utc, updated_at_utc, version)
SELECT gen_random_uuid(), tenant.id, seed.code, seed.name, seed.category, true, true,
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 0
FROM organization_tenants tenant
CROSS JOIN (VALUES
    ('CASH', '现金', 'Cash'),
    ('WECHAT_MANUAL', '微信人工登记', 'ManualExternal'),
    ('ALIPAY_MANUAL', '支付宝人工登记', 'ManualExternal')
) AS seed(code, name, category)
WHERE NOT EXISTS (
    SELECT 1 FROM payment_methods existing
    WHERE existing.tenant_id = tenant.id AND existing.code = seed.code
);

CREATE TABLE cashier_shifts (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    operator_id uuid NOT NULL REFERENCES identity_users(id),
    shift_no varchar(40) NOT NULL,
    status varchar(32) NOT NULL,
    opening_cash_minor bigint NOT NULL,
    expected_cash_minor bigint,
    submitted_cash_minor bigint,
    cash_difference_minor bigint,
    pending_reconciliation_minor bigint,
    handover_note varchar(500),
    opened_at_utc timestamptz NOT NULL,
    submitted_at_utc timestamptz,
    reviewed_by uuid REFERENCES identity_users(id),
    review_reason varchar(500),
    closed_at_utc timestamptz,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_cashier_shifts_tenant_no UNIQUE (tenant_id, shift_no),
    CONSTRAINT ck_cashier_shifts_status CHECK (status IN ('Open', 'ReviewPending', 'Closed')),
    CONSTRAINT ck_cashier_shifts_amounts CHECK (
        opening_cash_minor >= 0 AND
        (expected_cash_minor IS NULL OR expected_cash_minor >= 0) AND
        (submitted_cash_minor IS NULL OR submitted_cash_minor >= 0) AND
        (pending_reconciliation_minor IS NULL OR pending_reconciliation_minor >= 0))
);
CREATE INDEX ix_cashier_shifts_tenant_id ON cashier_shifts (tenant_id);
CREATE INDEX ix_cashier_shifts_store_status ON cashier_shifts (store_id, status, opened_at_utc DESC);
CREATE UNIQUE INDEX uq_cashier_shifts_operator_open
    ON cashier_shifts (store_id, operator_id) WHERE status = 'Open';

CREATE TABLE payments (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    order_id uuid NOT NULL REFERENCES service_orders(id),
    payment_no varchar(40) NOT NULL,
    status varchar(32) NOT NULL,
    currency varchar(3) NOT NULL,
    receivable_minor bigint NOT NULL,
    paid_minor bigint NOT NULL,
    paid_at_utc timestamptz,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_payments_tenant_no UNIQUE (tenant_id, payment_no),
    CONSTRAINT uq_payments_order UNIQUE (order_id),
    CONSTRAINT ck_payments_status CHECK (status IN ('Processing', 'Paid', 'Cancelled', 'ReversalRequired')),
    CONSTRAINT ck_payments_currency CHECK (currency = 'CNY'),
    CONSTRAINT ck_payments_amounts CHECK (receivable_minor >= 0 AND paid_minor >= 0 AND paid_minor <= receivable_minor)
);
CREATE INDEX ix_payments_tenant_id ON payments (tenant_id);
CREATE INDEX ix_payments_store_time ON payments (store_id, created_at_utc DESC);

CREATE TABLE payment_allocations (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    payment_id uuid NOT NULL REFERENCES payments(id) ON DELETE RESTRICT,
    method_id uuid NOT NULL REFERENCES payment_methods(id),
    method_code_snapshot varchar(40) NOT NULL,
    method_name_snapshot varchar(80) NOT NULL,
    category varchar(32) NOT NULL,
    amount_minor bigint NOT NULL,
    external_reference varchar(100),
    shift_id uuid REFERENCES cashier_shifts(id),
    confirmation_status varchar(48) NOT NULL,
    reconciliation_status varchar(32) NOT NULL,
    confirmed_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_payment_allocations_method UNIQUE (payment_id, method_id),
    CONSTRAINT ck_payment_allocations_category CHECK (category IN ('Cash', 'ManualExternal', 'InternalAccount')),
    CONSTRAINT ck_payment_allocations_amount CHECK (amount_minor > 0 AND amount_minor <= 10000000000),
    CONSTRAINT ck_payment_allocations_confirmation CHECK (confirmation_status IN
        ('CashRecorded', 'ManualPendingReconciliation', 'InternalConfirmed', 'ChannelConfirmed', 'Failed', 'Cancelled')),
    CONSTRAINT ck_payment_allocations_reconciliation CHECK (reconciliation_status IN
        ('NotRequired', 'Pending', 'Matched', 'Difference', 'Resolved')),
    CONSTRAINT ck_payment_allocations_manual_reference CHECK (
        category <> 'ManualExternal' OR char_length(external_reference) BETWEEN 4 AND 100),
    CONSTRAINT ck_payment_allocations_shift CHECK (
        category NOT IN ('Cash', 'ManualExternal') OR shift_id IS NOT NULL)
);
CREATE INDEX ix_payment_allocations_tenant_id ON payment_allocations (tenant_id);
CREATE INDEX ix_payment_allocations_payment ON payment_allocations (payment_id);
CREATE INDEX ix_payment_allocations_shift ON payment_allocations (shift_id);
