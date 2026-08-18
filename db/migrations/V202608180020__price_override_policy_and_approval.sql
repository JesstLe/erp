-- Versioned price-override policy and independent approval workflow.
-- Existing orders are preserved as legacy direct authorizations; new overrides are authorized by server-side role rules.

CREATE TABLE price_override_policies (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id) ON DELETE RESTRICT,
    policy_version integer NOT NULL,
    manager_line_discount_basis_points integer NOT NULL,
    manager_order_discount_minor bigint NOT NULL,
    allow_manager_price_increase boolean NOT NULL,
    created_by uuid NOT NULL REFERENCES identity_users(id) ON DELETE RESTRICT,
    effective_from_utc timestamptz NOT NULL,
    is_active boolean NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_price_override_policies_version UNIQUE (tenant_id, policy_version),
    CONSTRAINT ck_price_override_policies_thresholds CHECK (
        policy_version >= 1 AND
        manager_line_discount_basis_points BETWEEN 0 AND 10000 AND
        manager_order_discount_minor BETWEEN 0 AND 10000000000
    )
);

CREATE UNIQUE INDEX ux_price_override_policies_active_tenant
    ON price_override_policies (tenant_id) WHERE is_active;

INSERT INTO price_override_policies (
    id, tenant_id, policy_version, manager_line_discount_basis_points,
    manager_order_discount_minor, allow_manager_price_increase, created_by,
    effective_from_utc, is_active, created_at_utc, updated_at_utc, version)
SELECT
    md5(tenant.id::text || ':price-override-policy:v1')::uuid,
    tenant.id,
    1,
    1000,
    5000,
    false,
    (SELECT app_user.id FROM identity_users app_user
        WHERE app_user.tenant_id = tenant.id ORDER BY app_user.created_at_utc, app_user.id LIMIT 1),
    CURRENT_TIMESTAMP,
    true,
    CURRENT_TIMESTAMP,
    CURRENT_TIMESTAMP,
    0
FROM organization_tenants tenant
WHERE EXISTS (SELECT 1 FROM identity_users app_user WHERE app_user.tenant_id = tenant.id);

ALTER TABLE service_orders
    ADD COLUMN price_authorization_status varchar(32) NOT NULL DEFAULT 'NotRequired',
    ADD COLUMN price_policy_id uuid REFERENCES price_override_policies(id) ON DELETE RESTRICT,
    ADD COLUMN price_policy_version integer,
    ADD COLUMN price_authorized_by uuid REFERENCES identity_users(id) ON DELETE RESTRICT,
    ADD COLUMN price_authorized_at_utc timestamptz;

UPDATE service_orders service_order
SET price_authorization_status = 'DirectAuthorized',
    price_policy_id = policy.id,
    price_policy_version = policy.policy_version,
    price_authorized_by = (SELECT app_user.id FROM identity_users app_user
        WHERE app_user.tenant_id = service_order.tenant_id
        ORDER BY app_user.created_at_utc, app_user.id LIMIT 1),
    price_authorized_at_utc = COALESCE(service_order.confirmed_at_utc, service_order.created_at_utc)
FROM price_override_policies policy
WHERE policy.tenant_id = service_order.tenant_id
  AND policy.is_active
  AND EXISTS (
      SELECT 1 FROM service_order_lines line
      WHERE line.order_id = service_order.id
        AND line.entered_price_minor <> line.reference_price_minor
  );

ALTER TABLE service_orders
    ADD CONSTRAINT ck_service_orders_price_authorization CHECK (
        (price_authorization_status = 'NotRequired' AND price_policy_id IS NULL AND
            price_policy_version IS NULL AND price_authorized_by IS NULL AND price_authorized_at_utc IS NULL) OR
        (price_authorization_status = 'PendingApproval' AND price_policy_id IS NOT NULL AND
            price_policy_version >= 1 AND price_authorized_by IS NULL AND price_authorized_at_utc IS NULL) OR
        (price_authorization_status IN ('DirectAuthorized', 'Approved') AND price_policy_id IS NOT NULL AND
            price_policy_version >= 1 AND price_authorized_by IS NOT NULL AND price_authorized_at_utc IS NOT NULL) OR
        (price_authorization_status IN ('Rejected', 'Cancelled') AND price_policy_id IS NOT NULL AND
            price_policy_version >= 1)
    );

CREATE INDEX ix_service_orders_price_authorization
    ON service_orders (store_id, price_authorization_status, created_at_utc);

CREATE TABLE price_override_approvals (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id) ON DELETE RESTRICT,
    store_id uuid NOT NULL REFERENCES organization_stores(id) ON DELETE RESTRICT,
    service_order_id uuid NOT NULL REFERENCES service_orders(id) ON DELETE RESTRICT,
    requester_id uuid NOT NULL REFERENCES identity_users(id) ON DELETE RESTRICT,
    requester_role_snapshot varchar(64) NOT NULL,
    policy_id uuid NOT NULL REFERENCES price_override_policies(id) ON DELETE RESTRICT,
    policy_version integer NOT NULL,
    reference_amount_minor bigint NOT NULL,
    receivable_minor bigint NOT NULL,
    difference_minor bigint NOT NULL,
    maximum_line_discount_basis_points integer NOT NULL,
    manager_line_discount_basis_points integer NOT NULL,
    manager_order_discount_minor bigint NOT NULL,
    allow_manager_price_increase boolean NOT NULL,
    status varchar(24) NOT NULL,
    requested_at_utc timestamptz NOT NULL,
    decided_by uuid REFERENCES identity_users(id) ON DELETE RESTRICT,
    decided_at_utc timestamptz,
    decision_note varchar(500),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_price_override_approvals_order UNIQUE (service_order_id),
    CONSTRAINT ck_price_override_approvals_amounts CHECK (
        reference_amount_minor BETWEEN 0 AND 1000000000000 AND
        receivable_minor BETWEEN 0 AND 1000000000000 AND
        difference_minor = receivable_minor - reference_amount_minor
    ),
    CONSTRAINT ck_price_override_approvals_thresholds CHECK (
        policy_version >= 1 AND
        maximum_line_discount_basis_points BETWEEN 0 AND 10000 AND
        manager_line_discount_basis_points BETWEEN 0 AND 10000 AND
        manager_order_discount_minor BETWEEN 0 AND 10000000000
    ),
    CONSTRAINT ck_price_override_approvals_decision CHECK (
        (status = 'Pending' AND decided_by IS NULL AND decided_at_utc IS NULL) OR
        (status = 'Approved' AND decided_by IS NOT NULL AND decided_at_utc IS NOT NULL) OR
        (status = 'Rejected' AND decided_by IS NOT NULL AND decided_at_utc IS NOT NULL AND
            char_length(decision_note) BETWEEN 2 AND 500) OR
        (status = 'Cancelled' AND decided_at_utc IS NOT NULL)
    ),
    CONSTRAINT ck_price_override_approvals_no_self_review CHECK (
        decided_by IS NULL OR decided_by <> requester_id
    )
);

CREATE INDEX ix_price_override_approvals_store_status
    ON price_override_approvals (store_id, status, requested_at_utc DESC);

COMMENT ON TABLE price_override_policies IS
    'Immutable tenant-wide price authorization policy versions; exactly one version is active.';
COMMENT ON TABLE price_override_approvals IS
    'Independent owner approval with immutable price and policy snapshots; approval never changes order prices.';
