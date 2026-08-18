-- Platform control plane and immutable login security events.

CREATE TABLE platform_admin_users (
    id uuid PRIMARY KEY,
    account varchar(100) NOT NULL,
    normalized_account varchar(100) NOT NULL UNIQUE,
    display_name varchar(100) NOT NULL,
    password_hash text NOT NULL,
    is_enabled boolean NOT NULL DEFAULT true,
    must_change_password boolean NOT NULL DEFAULT true,
    access_failed_count integer NOT NULL DEFAULT 0,
    lockout_end_utc timestamptz,
    security_stamp varchar(64) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_platform_admin_users_failed_count CHECK (access_failed_count >= 0)
);

CREATE TABLE merchant_registration_applications (
    id uuid PRIMARY KEY,
    application_no varchar(32) NOT NULL UNIQUE,
    merchant_name varchar(100) NOT NULL,
    store_name varchar(100) NOT NULL,
    contact_name varchar(60) NOT NULL,
    contact_mobile_ciphertext text NOT NULL,
    contact_mobile_hash bytea NOT NULL,
    contact_mobile_last_four char(4) NOT NULL,
    contact_email_ciphertext text,
    contact_email_hash bytea,
    desired_owner_account varchar(100) NOT NULL,
    normalized_desired_owner_account varchar(100) NOT NULL,
    note varchar(500),
    source_ip varchar(64) NOT NULL,
    status varchar(24) NOT NULL,
    reviewed_by_platform_user_id uuid REFERENCES platform_admin_users(id) ON DELETE RESTRICT,
    reviewed_at_utc timestamptz,
    review_reason varchar(500),
    tenant_id uuid REFERENCES organization_tenants(id) ON DELETE RESTRICT,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_registration_mobile_hash CHECK (octet_length(contact_mobile_hash) = 32),
    CONSTRAINT ck_registration_email_hash CHECK (contact_email_hash IS NULL OR octet_length(contact_email_hash) = 32),
    CONSTRAINT ck_registration_status CHECK (status IN ('PendingReview', 'Approved', 'Rejected')),
    CONSTRAINT ck_registration_review CHECK (
        (status = 'PendingReview' AND reviewed_by_platform_user_id IS NULL AND reviewed_at_utc IS NULL AND tenant_id IS NULL)
        OR (status = 'Approved' AND reviewed_by_platform_user_id IS NOT NULL AND reviewed_at_utc IS NOT NULL AND tenant_id IS NOT NULL)
        OR (status = 'Rejected' AND reviewed_by_platform_user_id IS NOT NULL AND reviewed_at_utc IS NOT NULL AND tenant_id IS NULL)
    )
);

CREATE UNIQUE INDEX uq_registration_pending_account
    ON merchant_registration_applications (normalized_desired_owner_account)
    WHERE status = 'PendingReview';
CREATE UNIQUE INDEX uq_registration_pending_mobile
    ON merchant_registration_applications (contact_mobile_hash)
    WHERE status = 'PendingReview';
CREATE INDEX ix_registration_status_time
    ON merchant_registration_applications (status, created_at_utc DESC);

CREATE TABLE login_security_events (
    id uuid PRIMARY KEY,
    scope varchar(16) NOT NULL,
    tenant_id uuid REFERENCES organization_tenants(id) ON DELETE RESTRICT,
    merchant_user_id uuid REFERENCES identity_users(id) ON DELETE RESTRICT,
    platform_user_id uuid REFERENCES platform_admin_users(id) ON DELETE RESTRICT,
    event_type varchar(40) NOT NULL,
    result_code varchar(64) NOT NULL,
    account_hash bytea NOT NULL,
    account_mask varchar(100) NOT NULL,
    ip_address varchar(64) NOT NULL,
    user_agent_summary varchar(200) NOT NULL,
    trace_id varchar(64) NOT NULL,
    occurred_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_login_security_scope CHECK (scope IN ('Merchant', 'Platform')),
    CONSTRAINT ck_login_security_hash CHECK (octet_length(account_hash) = 32),
    CONSTRAINT ck_login_security_actor_scope CHECK (
        (scope = 'Merchant' AND platform_user_id IS NULL)
        OR (scope = 'Platform' AND tenant_id IS NULL AND merchant_user_id IS NULL)
    )
);

CREATE INDEX ix_login_security_events_time ON login_security_events (occurred_at_utc DESC);
CREATE INDEX ix_login_security_events_scope_result_time
    ON login_security_events (scope, result_code, occurred_at_utc DESC);
CREATE INDEX ix_login_security_events_tenant_time
    ON login_security_events (tenant_id, occurred_at_utc DESC) WHERE tenant_id IS NOT NULL;
CREATE INDEX ix_login_security_events_account_hash_time
    ON login_security_events (account_hash, occurred_at_utc DESC);

CREATE TABLE platform_audit_events (
    id uuid PRIMARY KEY,
    platform_user_id uuid NOT NULL REFERENCES platform_admin_users(id) ON DELETE RESTRICT,
    action varchar(128) NOT NULL,
    entity_type varchar(80) NOT NULL,
    entity_id uuid,
    previous_state varchar(40),
    current_state varchar(40),
    reason varchar(500),
    trace_id varchar(64) NOT NULL,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    occurred_at_utc timestamptz NOT NULL
);

CREATE INDEX ix_platform_audit_events_time ON platform_audit_events (occurred_at_utc DESC);
CREATE INDEX ix_platform_audit_events_entity ON platform_audit_events (entity_type, entity_id);

CREATE FUNCTION reject_platform_append_only_mutation() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'platform security and audit events are immutable' USING ERRCODE = '55000';
END;
$$;

CREATE TRIGGER trg_login_security_events_immutable
    BEFORE UPDATE OR DELETE ON login_security_events
    FOR EACH ROW EXECUTE FUNCTION reject_platform_append_only_mutation();

CREATE TRIGGER trg_platform_audit_events_immutable
    BEFORE UPDATE OR DELETE ON platform_audit_events
    FOR EACH ROW EXECUTE FUNCTION reject_platform_append_only_mutation();

COMMENT ON TABLE login_security_events IS 'Append-only login security events; never store passwords, cookies or request bodies.';
COMMENT ON TABLE platform_audit_events IS 'Append-only platform control-plane audit trail.';
