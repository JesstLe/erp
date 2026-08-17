-- V2 member top-ups. Payments gain an additive business source so stored-value funding is not
-- misclassified as service revenue. Existing service-order payments are backfilled in place.

ALTER TABLE payments
    ADD COLUMN business_type varchar(32),
    ADD COLUMN business_id uuid;

UPDATE payments
SET business_type = 'ServiceOrder', business_id = order_id
WHERE business_type IS NULL;

ALTER TABLE payments
    ALTER COLUMN business_type SET NOT NULL,
    ALTER COLUMN business_id SET NOT NULL,
    ALTER COLUMN order_id DROP NOT NULL,
    DROP CONSTRAINT uq_payments_order,
    ADD CONSTRAINT ck_payments_business_type CHECK (business_type IN ('ServiceOrder', 'MemberTopup')),
    ADD CONSTRAINT ck_payments_service_order_link CHECK (
        (business_type = 'ServiceOrder' AND order_id = business_id) OR
        (business_type <> 'ServiceOrder' AND order_id IS NULL));

CREATE UNIQUE INDEX uq_payments_order
    ON payments (order_id) WHERE order_id IS NOT NULL;
CREATE UNIQUE INDEX uq_payments_business
    ON payments (tenant_id, business_type, business_id);

CREATE TABLE member_topup_orders (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    customer_id uuid NOT NULL REFERENCES customers(id),
    card_id uuid NOT NULL REFERENCES membership_cards(id),
    topup_no varchar(40) NOT NULL,
    principal_minor bigint NOT NULL,
    bonus_minor bigint NOT NULL,
    receivable_minor bigint NOT NULL,
    status varchar(32) NOT NULL,
    note varchar(500),
    paid_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_member_topup_orders_tenant_no UNIQUE (tenant_id, topup_no),
    CONSTRAINT ck_member_topup_orders_amounts CHECK (
        principal_minor > 0 AND principal_minor <= 10000000000 AND
        bonus_minor >= 0 AND bonus_minor <= 10000000000 AND
        receivable_minor = principal_minor),
    CONSTRAINT ck_member_topup_orders_status CHECK (
        status IN ('Paid', 'Cancelled', 'PartiallyRefunded', 'Refunded'))
);

CREATE INDEX ix_member_topup_orders_tenant_id ON member_topup_orders (tenant_id);
CREATE INDEX ix_member_topup_orders_store_paid ON member_topup_orders (store_id, paid_at_utc DESC);
CREATE INDEX ix_member_topup_orders_customer_paid ON member_topup_orders (customer_id, paid_at_utc DESC);
CREATE UNIQUE INDEX uq_member_account_ledgers_account_command
    ON member_account_ledgers (account_id, command_id);
