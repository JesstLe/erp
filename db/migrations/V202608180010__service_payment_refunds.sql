-- V2 service payment refunds. Original payments and ledgers remain immutable.

ALTER TABLE payments
    ADD COLUMN refunded_minor bigint NOT NULL DEFAULT 0,
    DROP CONSTRAINT ck_payments_status,
    ADD CONSTRAINT ck_payments_status CHECK (status IN
        ('Processing', 'Paid', 'PartiallyRefunded', 'Refunded', 'Cancelled', 'ReversalRequired')),
    ADD CONSTRAINT ck_payments_refunded CHECK (refunded_minor BETWEEN 0 AND paid_minor);

ALTER TABLE service_orders
    ADD COLUMN refunded_minor bigint NOT NULL DEFAULT 0,
    DROP CONSTRAINT ck_service_orders_status,
    ADD CONSTRAINT ck_service_orders_status CHECK (status IN
        ('Draft', 'PendingPayment', 'PaymentProcessing', 'Settled', 'PartiallyRefunded', 'Refunded', 'Voided')),
    ADD CONSTRAINT ck_service_orders_refunded CHECK (refunded_minor BETWEEN 0 AND receivable_minor);

CREATE TABLE payment_refunds (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    payment_id uuid NOT NULL REFERENCES payments(id) ON DELETE RESTRICT,
    refund_no varchar(40) NOT NULL,
    status varchar(24) NOT NULL,
    amount_minor bigint NOT NULL,
    reason varchar(500) NOT NULL,
    requested_by uuid NOT NULL REFERENCES identity_users(id),
    requested_at_utc timestamptz NOT NULL,
    approved_by uuid REFERENCES identity_users(id),
    completed_at_utc timestamptz,
    rejection_reason varchar(500),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_payment_refunds_no UNIQUE (tenant_id, refund_no),
    CONSTRAINT ck_payment_refunds_status CHECK (status IN ('PendingApproval', 'Completed', 'Rejected')),
    CONSTRAINT ck_payment_refunds_amount CHECK (amount_minor > 0 AND amount_minor <= 10000000000),
    CONSTRAINT ck_payment_refunds_completion CHECK (
        (status = 'PendingApproval' AND approved_by IS NULL AND completed_at_utc IS NULL AND rejection_reason IS NULL) OR
        (status = 'Completed' AND approved_by IS NOT NULL AND completed_at_utc IS NOT NULL AND rejection_reason IS NULL) OR
        (status = 'Rejected' AND approved_by IS NOT NULL AND completed_at_utc IS NULL AND rejection_reason IS NOT NULL))
);

CREATE INDEX ix_payment_refunds_payment_status ON payment_refunds (payment_id, status);
CREATE INDEX ix_payment_refunds_store_requested ON payment_refunds (store_id, requested_at_utc DESC);

CREATE TABLE payment_refund_lines (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    refund_id uuid NOT NULL REFERENCES payment_refunds(id) ON DELETE RESTRICT,
    original_allocation_id uuid NOT NULL REFERENCES payment_allocations(id) ON DELETE RESTRICT,
    amount_minor bigint NOT NULL,
    category varchar(32) NOT NULL,
    member_account_id uuid REFERENCES member_accounts(id) ON DELETE RESTRICT,
    route varchar(32) NOT NULL,
    cash_shift_id uuid REFERENCES cashier_shifts(id) ON DELETE RESTRICT,
    completed_at_utc timestamptz,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_payment_refund_line_allocation UNIQUE (refund_id, original_allocation_id),
    CONSTRAINT ck_payment_refund_lines_amount CHECK (amount_minor > 0 AND amount_minor <= 10000000000),
    CONSTRAINT ck_payment_refund_lines_category CHECK (category IN ('Cash', 'InternalAccount')),
    CONSTRAINT ck_payment_refund_lines_route CHECK (route IN ('OriginalCash', 'OriginalMemberAccount')),
    CONSTRAINT ck_payment_refund_lines_account CHECK (
        (category = 'InternalAccount' AND member_account_id IS NOT NULL AND route = 'OriginalMemberAccount') OR
        (category = 'Cash' AND member_account_id IS NULL AND route = 'OriginalCash')),
    CONSTRAINT ck_payment_refund_lines_cash_shift CHECK (
        (category = 'Cash' AND ((completed_at_utc IS NULL AND cash_shift_id IS NULL) OR
            (completed_at_utc IS NOT NULL AND cash_shift_id IS NOT NULL))) OR
        (category = 'InternalAccount' AND cash_shift_id IS NULL))
);

CREATE INDEX ix_payment_refund_lines_refund ON payment_refund_lines (refund_id);
CREATE INDEX ix_payment_refund_lines_original_allocation ON payment_refund_lines (original_allocation_id);
CREATE INDEX ix_payment_refund_lines_cash_shift ON payment_refund_lines (cash_shift_id);
