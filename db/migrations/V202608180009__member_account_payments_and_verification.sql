-- V2 member balance consumption and one-time verification challenges.

ALTER TABLE payment_methods
    ADD COLUMN internal_account_type varchar(24),
    ADD CONSTRAINT ck_payment_methods_internal_account_type CHECK (
        (category = 'InternalAccount' AND internal_account_type IN ('Principal', 'Bonus')) OR
        (category <> 'InternalAccount' AND internal_account_type IS NULL));

INSERT INTO payment_methods (id, tenant_id, code, name, category, internal_account_type,
    requires_open_shift, is_enabled, created_at_utc, updated_at_utc, version)
SELECT gen_random_uuid(), tenant.id, seed.code, seed.name, 'InternalAccount', seed.account_type,
    false, true, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 0
FROM organization_tenants tenant
CROSS JOIN (VALUES
    ('MEMBER_PRINCIPAL', '会员储值本金', 'Principal'),
    ('MEMBER_BONUS', '会员奖励金', 'Bonus')
) AS seed(code, name, account_type)
WHERE NOT EXISTS (
    SELECT 1 FROM payment_methods existing
    WHERE existing.tenant_id = tenant.id AND existing.code = seed.code
);

ALTER TABLE payment_allocations
    ADD COLUMN member_account_id uuid REFERENCES member_accounts(id) ON DELETE RESTRICT,
    ADD CONSTRAINT ck_payment_allocations_member_account CHECK (
        (category = 'InternalAccount' AND member_account_id IS NOT NULL AND shift_id IS NULL) OR
        (category <> 'InternalAccount' AND member_account_id IS NULL));

CREATE INDEX ix_payment_allocations_member_account ON payment_allocations (member_account_id);

CREATE TABLE member_verification_challenges (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    customer_id uuid NOT NULL REFERENCES customers(id),
    order_id uuid NOT NULL REFERENCES service_orders(id),
    authorized_amount_minor bigint NOT NULL,
    code_salt bytea NOT NULL,
    code_hash bytea NOT NULL,
    mobile_last_four varchar(4) NOT NULL,
    requested_by uuid NOT NULL REFERENCES identity_users(id),
    status varchar(24) NOT NULL,
    attempts_remaining integer NOT NULL,
    expires_at_utc timestamptz NOT NULL,
    verified_at_utc timestamptz,
    used_at_utc timestamptz,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_member_verification_amount CHECK (
        authorized_amount_minor BETWEEN 50000 AND 10000000000),
    CONSTRAINT ck_member_verification_code_material CHECK (
        octet_length(code_salt) = 16 AND octet_length(code_hash) = 32),
    CONSTRAINT ck_member_verification_mobile CHECK (mobile_last_four ~ '^[0-9]{4}$'),
    CONSTRAINT ck_member_verification_status CHECK (
        status IN ('Active', 'Verified', 'Used', 'Locked', 'Expired')),
    CONSTRAINT ck_member_verification_attempts CHECK (attempts_remaining BETWEEN 0 AND 5)
);

CREATE INDEX ix_member_verification_challenges_tenant_id
    ON member_verification_challenges (tenant_id);
CREATE INDEX ix_member_verification_challenges_order_status
    ON member_verification_challenges (order_id, status);
CREATE INDEX ix_member_verification_challenges_customer_created
    ON member_verification_challenges (customer_id, created_at_utc DESC);
