-- Allow an optional store employee attribution on product order lines.
-- The existing service_employee_id snapshot columns are reused for backward-compatible API and report reads;
-- product attribution never creates a service commission snapshot.

ALTER TABLE service_order_lines
    DROP CONSTRAINT ck_service_order_lines_commission_basis;

ALTER TABLE service_order_lines
    ADD CONSTRAINT ck_service_order_lines_commission_basis CHECK (
        commission_basis_minor >= 0 AND
        ((line_type = 'Service' AND commission_basis_minor = line_amount_minor) OR
         (line_type = 'Product' AND commission_mode_snapshot = 'None' AND
            commission_basis_minor = 0 AND commission_amount_minor = 0))
    );

COMMENT ON COLUMN service_order_lines.service_employee_id IS
    'Employee attribution snapshot: service employee for service lines; optional added-by employee for product lines.';
