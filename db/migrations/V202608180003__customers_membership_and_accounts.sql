-- Customer privacy, membership cards, separated accounts and immutable account ledgers.

CREATE TABLE customers (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    home_store_id uuid NOT NULL REFERENCES organization_stores(id),
    name varchar(100) NOT NULL,
    mobile_ciphertext varchar(2048) NOT NULL,
    mobile_lookup_hash bytea NOT NULL,
    mobile_last_four varchar(4) NOT NULL,
    gender varchar(16) NOT NULL,
    birth_date date,
    source_code varchar(40),
    service_notification_consent boolean NOT NULL DEFAULT false,
    marketing_consent boolean NOT NULL DEFAULT false,
    status varchar(24) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_customers_mobile_hash CHECK (octet_length(mobile_lookup_hash) = 32),
    CONSTRAINT ck_customers_mobile_last_four CHECK (mobile_last_four ~ '^[0-9]{4}$'),
    CONSTRAINT ck_customers_gender CHECK (gender IN ('Unknown', 'Female', 'Male', 'Other')),
    CONSTRAINT ck_customers_status CHECK (status IN ('Active', 'Disabled', 'Merged'))
);
CREATE INDEX ix_customers_tenant_id ON customers (tenant_id);
CREATE INDEX ix_customers_tenant_mobile_hash ON customers (tenant_id, mobile_lookup_hash);
CREATE INDEX ix_customers_store_created ON customers (home_store_id, created_at_utc DESC);
CREATE INDEX ix_customers_store_mobile_last_four ON customers (home_store_id, mobile_last_four);

ALTER TABLE visits
    ADD CONSTRAINT fk_visits_customer_id FOREIGN KEY (customer_id) REFERENCES customers(id);

CREATE TABLE membership_card_types (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    code varchar(40) NOT NULL,
    name varchar(80) NOT NULL,
    validity_days integer,
    status varchar(24) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_membership_card_types_tenant_code UNIQUE (tenant_id, code),
    CONSTRAINT ck_membership_card_types_validity CHECK (validity_days IS NULL OR validity_days BETWEEN 1 AND 3650),
    CONSTRAINT ck_membership_card_types_status CHECK (status IN ('Published', 'Disabled'))
);
CREATE INDEX ix_membership_card_types_tenant_id ON membership_card_types (tenant_id);

CREATE TABLE membership_cards (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    customer_id uuid NOT NULL REFERENCES customers(id),
    card_type_id uuid NOT NULL REFERENCES membership_card_types(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    card_no varchar(40) NOT NULL,
    valid_from date NOT NULL,
    valid_to date,
    note varchar(500),
    status varchar(24) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_membership_cards_tenant_card_no UNIQUE (tenant_id, card_no),
    CONSTRAINT ck_membership_cards_validity CHECK (valid_to IS NULL OR valid_to >= valid_from),
    CONSTRAINT ck_membership_cards_status CHECK (status IN ('Active', 'Expired', 'Disabled'))
);
CREATE INDEX ix_membership_cards_tenant_id ON membership_cards (tenant_id);
CREATE INDEX ix_membership_cards_customer ON membership_cards (customer_id, created_at_utc DESC);

CREATE TABLE member_accounts (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    customer_id uuid NOT NULL REFERENCES customers(id),
    card_id uuid NOT NULL REFERENCES membership_cards(id),
    account_type varchar(24) NOT NULL,
    balance_units bigint NOT NULL DEFAULT 0,
    status varchar(24) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_member_accounts_card_type UNIQUE (card_id, account_type),
    CONSTRAINT ck_member_accounts_type CHECK (account_type IN ('Principal', 'Bonus', 'Points')),
    CONSTRAINT ck_member_accounts_balance CHECK (balance_units >= 0),
    CONSTRAINT ck_member_accounts_status CHECK (status IN ('Active', 'Frozen', 'Closed'))
);
CREATE INDEX ix_member_accounts_tenant_id ON member_accounts (tenant_id);
CREATE INDEX ix_member_accounts_customer ON member_accounts (customer_id);

CREATE TABLE member_account_ledgers (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    account_id uuid NOT NULL REFERENCES member_accounts(id),
    business_type varchar(40) NOT NULL,
    business_id uuid NOT NULL,
    direction varchar(12) NOT NULL,
    units bigint NOT NULL,
    balance_before bigint NOT NULL,
    balance_after bigint NOT NULL,
    command_id uuid NOT NULL,
    occurred_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_member_account_ledgers_direction CHECK (direction IN ('Credit', 'Debit')),
    CONSTRAINT ck_member_account_ledgers_units CHECK (units > 0),
    CONSTRAINT ck_member_account_ledgers_balances CHECK (balance_before >= 0 AND balance_after >= 0)
);
CREATE INDEX ix_member_account_ledgers_tenant_id ON member_account_ledgers (tenant_id);
CREATE INDEX ix_member_account_ledgers_account_time ON member_account_ledgers (account_id, occurred_at_utc, id);
CREATE INDEX ix_member_account_ledgers_business ON member_account_ledgers (business_type, business_id);
CREATE INDEX ix_member_account_ledgers_command ON member_account_ledgers (command_id);

CREATE FUNCTION reject_member_account_ledger_mutation() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'member account ledger entries are immutable' USING ERRCODE = '55000';
END;
$$;

CREATE TRIGGER trg_member_account_ledgers_immutable
    BEFORE UPDATE OR DELETE ON member_account_ledgers
    FOR EACH ROW EXECUTE FUNCTION reject_member_account_ledger_mutation();
