-- Service staff attribution and immutable commission snapshots.
-- Commission rules are configured on service master data; checkout only selects an eligible store employee.

ALTER TABLE catalog_service_items
    ADD COLUMN commission_mode varchar(24) NOT NULL DEFAULT 'None',
    ADD COLUMN commission_rate_basis_points integer,
    ADD COLUMN commission_fixed_minor bigint,
    ADD CONSTRAINT ck_catalog_service_items_commission CHECK (
        (commission_mode = 'None' AND commission_rate_basis_points IS NULL AND commission_fixed_minor IS NULL) OR
        (commission_mode = 'Percentage' AND commission_rate_basis_points BETWEEN 1 AND 10000 AND
            commission_fixed_minor IS NULL) OR
        (commission_mode = 'FixedAmount' AND commission_rate_basis_points IS NULL AND
            commission_fixed_minor BETWEEN 1 AND 10000000000)
    );

ALTER TABLE service_order_lines
    ADD COLUMN service_employee_id uuid REFERENCES organization_employees(id) ON DELETE RESTRICT,
    ADD COLUMN employee_no_snapshot varchar(32),
    ADD COLUMN employee_name_snapshot varchar(100),
    ADD COLUMN commission_mode_snapshot varchar(24) NOT NULL DEFAULT 'None',
    ADD COLUMN commission_rate_basis_points integer,
    ADD COLUMN commission_fixed_minor bigint,
    ADD COLUMN commission_basis_minor bigint NOT NULL DEFAULT 0,
    ADD COLUMN commission_amount_minor bigint NOT NULL DEFAULT 0;

UPDATE service_order_lines
SET commission_basis_minor = line_amount_minor
WHERE line_type = 'Service';

ALTER TABLE service_order_lines
    ADD CONSTRAINT ck_service_order_lines_employee_snapshot CHECK (
        (service_employee_id IS NULL AND employee_no_snapshot IS NULL AND employee_name_snapshot IS NULL) OR
        (service_employee_id IS NOT NULL AND char_length(employee_no_snapshot) BETWEEN 2 AND 32 AND
            char_length(employee_name_snapshot) BETWEEN 2 AND 100)
    ),
    ADD CONSTRAINT ck_service_order_lines_commission_snapshot CHECK (
        (commission_mode_snapshot = 'None' AND commission_rate_basis_points IS NULL AND
            commission_fixed_minor IS NULL AND commission_amount_minor = 0) OR
        (line_type = 'Service' AND service_employee_id IS NOT NULL AND
            commission_mode_snapshot = 'Percentage' AND commission_rate_basis_points BETWEEN 1 AND 10000 AND
            commission_fixed_minor IS NULL AND commission_amount_minor BETWEEN 0 AND commission_basis_minor) OR
        (line_type = 'Service' AND service_employee_id IS NOT NULL AND
            commission_mode_snapshot = 'FixedAmount' AND commission_rate_basis_points IS NULL AND
            commission_fixed_minor BETWEEN 1 AND 10000000000 AND
            commission_amount_minor = commission_fixed_minor * quantity AND
            commission_amount_minor BETWEEN 0 AND commission_basis_minor)
    ),
    ADD CONSTRAINT ck_service_order_lines_commission_basis CHECK (
        commission_basis_minor >= 0 AND
        ((line_type = 'Service' AND commission_basis_minor = line_amount_minor) OR
         (line_type = 'Product' AND service_employee_id IS NULL AND commission_mode_snapshot = 'None' AND
            commission_basis_minor = 0 AND commission_amount_minor = 0))
    );

CREATE INDEX ix_service_order_lines_employee
    ON service_order_lines (service_employee_id, created_at_utc)
    WHERE service_employee_id IS NOT NULL;

COMMENT ON COLUMN catalog_service_items.commission_rate_basis_points IS
    '10000 basis points = 100%; configured only by OWNER.';
COMMENT ON COLUMN catalog_service_items.commission_fixed_minor IS
    'Fixed commission per service unit in minor currency units.';
COMMENT ON COLUMN service_order_lines.commission_amount_minor IS
    'Immutable gross commission snapshot; refunds are reported as separate proportional deductions.';
