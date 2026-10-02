ALTER TABLE organization_employee_positions
    ADD COLUMN default_commission_rate_basis_points integer NULL,
    ADD CONSTRAINT ck_employee_positions_commission_rate
        CHECK (default_commission_rate_basis_points BETWEEN 0 AND 10000);

ALTER TABLE service_order_lines
    ADD COLUMN commission_position_code_snapshot varchar(40) NULL,
    ADD COLUMN commission_position_name_snapshot varchar(60) NULL,
    ADD COLUMN commission_rule_source_snapshot varchar(24) NULL,
    ADD CONSTRAINT ck_service_order_lines_commission_position CHECK (
        (commission_position_code_snapshot IS NULL AND commission_position_name_snapshot IS NULL) OR
        (commission_position_code_snapshot IS NOT NULL AND commission_position_name_snapshot IS NOT NULL)),
    ADD CONSTRAINT ck_service_order_lines_commission_source CHECK (
        commission_rule_source_snapshot IN ('ServiceItem', 'PositionDefault', 'PositionService'));

CREATE INDEX ix_service_order_lines_employee_commissions
    ON service_order_lines (tenant_id, service_employee_id, order_id)
    WHERE line_type = 'Service' AND service_employee_id IS NOT NULL;

CREATE UNIQUE INDEX uq_employee_positions_tenant_id ON organization_employee_positions (tenant_id, id);
CREATE UNIQUE INDEX uq_service_items_tenant_id ON catalog_service_items (tenant_id, id);

CREATE TABLE organization_position_service_commissions (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    position_id uuid NOT NULL,
    service_item_id uuid NOT NULL,
    rate_basis_points integer NOT NULL CHECK (rate_basis_points BETWEEN 0 AND 10000),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_position_service_commissions UNIQUE (tenant_id, position_id, service_item_id),
    FOREIGN KEY (tenant_id, position_id) REFERENCES organization_employee_positions(tenant_id, id) ON DELETE CASCADE,
    FOREIGN KEY (tenant_id, service_item_id) REFERENCES catalog_service_items(tenant_id, id) ON DELETE RESTRICT
);
