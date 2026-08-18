-- Member service passes and expiring point grants. Financial and benefit history remains append-only.

CREATE TABLE member_service_passes (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    customer_id uuid NOT NULL REFERENCES customers(id),
    card_id uuid NOT NULL REFERENCES membership_cards(id),
    service_item_id uuid NOT NULL REFERENCES catalog_service_items(id),
    pass_name varchar(100) NOT NULL,
    purchased_uses integer NOT NULL,
    bonus_uses integer NOT NULL,
    remaining_purchased_uses integer NOT NULL,
    remaining_bonus_uses integer NOT NULL,
    valid_from date NOT NULL,
    valid_to date,
    issue_reason varchar(500) NOT NULL,
    status varchar(24) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_member_service_pass_uses CHECK (
        purchased_uses >= 0 AND bonus_uses >= 0 AND purchased_uses + bonus_uses BETWEEN 1 AND 100000 AND
        remaining_purchased_uses BETWEEN 0 AND purchased_uses AND
        remaining_bonus_uses BETWEEN 0 AND bonus_uses),
    CONSTRAINT ck_member_service_pass_dates CHECK (valid_to IS NULL OR valid_to >= valid_from),
    CONSTRAINT ck_member_service_pass_status CHECK (status IN ('Active', 'Exhausted', 'Expired', 'Cancelled')),
    CONSTRAINT ck_member_service_pass_projection CHECK (
        (status = 'Active' AND remaining_purchased_uses + remaining_bonus_uses > 0) OR
        (status IN ('Exhausted', 'Expired', 'Cancelled') AND remaining_purchased_uses + remaining_bonus_uses = 0))
);
CREATE INDEX ix_member_service_passes_customer ON member_service_passes
    (tenant_id, store_id, customer_id, created_at_utc DESC);
CREATE INDEX ix_member_service_passes_card_status ON member_service_passes (card_id, status);

CREATE TABLE member_service_pass_ledgers (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    pass_id uuid NOT NULL REFERENCES member_service_passes(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    customer_id uuid NOT NULL REFERENCES customers(id),
    action varchar(24) NOT NULL,
    purchased_uses_delta integer NOT NULL,
    bonus_uses_delta integer NOT NULL,
    purchased_uses_after integer NOT NULL,
    bonus_uses_after integer NOT NULL,
    service_order_id uuid REFERENCES service_orders(id),
    reversed_ledger_id uuid REFERENCES member_service_pass_ledgers(id),
    command_id uuid NOT NULL,
    operator_id uuid NOT NULL REFERENCES identity_users(id),
    reason varchar(500) NOT NULL,
    occurred_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_member_service_pass_ledger_command UNIQUE (pass_id, command_id),
    CONSTRAINT uq_member_service_pass_ledger_reversal UNIQUE (reversed_ledger_id),
    CONSTRAINT ck_member_service_pass_ledger_action CHECK (action IN ('Issue', 'Redeem', 'Reverse', 'Expire', 'Cancel')),
    CONSTRAINT ck_member_service_pass_ledger_after CHECK (purchased_uses_after >= 0 AND bonus_uses_after >= 0),
    CONSTRAINT ck_member_service_pass_ledger_delta CHECK (
        (action IN ('Issue', 'Reverse') AND purchased_uses_delta + bonus_uses_delta > 0) OR
        (action IN ('Redeem', 'Expire', 'Cancel') AND purchased_uses_delta + bonus_uses_delta < 0))
);
CREATE INDEX ix_member_service_pass_ledgers_pass_time ON member_service_pass_ledgers
    (pass_id, occurred_at_utc, id);

CREATE TABLE member_point_grants (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    customer_id uuid NOT NULL REFERENCES customers(id),
    card_id uuid NOT NULL REFERENCES membership_cards(id),
    account_id uuid NOT NULL REFERENCES member_accounts(id),
    original_units bigint NOT NULL,
    remaining_units bigint NOT NULL,
    expires_on date,
    source_type varchar(40) NOT NULL,
    source_id uuid NOT NULL,
    status varchar(24) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_member_point_grant_units CHECK (
        original_units BETWEEN 1 AND 1000000000 AND remaining_units BETWEEN 0 AND original_units),
    CONSTRAINT ck_member_point_grant_status CHECK (status IN ('Active', 'Exhausted', 'Expired')),
    CONSTRAINT ck_member_point_grant_projection CHECK (
        (status = 'Active' AND remaining_units > 0) OR
        (status IN ('Exhausted', 'Expired') AND remaining_units = 0))
);
CREATE INDEX ix_member_point_grants_fifo ON member_point_grants
    (account_id, status, expires_on NULLS LAST, created_at_utc, id);
CREATE INDEX ix_member_point_grants_due ON member_point_grants
    (tenant_id, store_id, expires_on) WHERE status = 'Active' AND expires_on IS NOT NULL;

CREATE TABLE member_point_use_allocations (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    debit_ledger_id uuid NOT NULL REFERENCES member_account_ledgers(id),
    grant_id uuid NOT NULL REFERENCES member_point_grants(id),
    units bigint NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_member_point_allocation_grant UNIQUE (debit_ledger_id, grant_id),
    CONSTRAINT ck_member_point_allocation_units CHECK (units > 0)
);
CREATE INDEX ix_member_point_allocations_ledger ON member_point_use_allocations (debit_ledger_id);

CREATE TRIGGER trg_member_service_pass_ledgers_immutable
    BEFORE UPDATE OR DELETE ON member_service_pass_ledgers
    FOR EACH ROW EXECUTE FUNCTION reject_member_account_ledger_mutation();

CREATE TRIGGER trg_member_point_use_allocations_immutable
    BEFORE UPDATE OR DELETE ON member_point_use_allocations
    FOR EACH ROW EXECUTE FUNCTION reject_member_account_ledger_mutation();
