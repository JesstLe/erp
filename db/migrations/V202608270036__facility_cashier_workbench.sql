ALTER TABLE service_orders
    ADD COLUMN consultant_employee_id uuid NULL REFERENCES organization_employees(id),
    ADD COLUMN consultant_employee_no_snapshot varchar(32) NULL,
    ADD COLUMN consultant_employee_name_snapshot varchar(100) NULL;

ALTER TABLE service_orders
    ADD CONSTRAINT ck_service_orders_consultant_snapshot
    CHECK (
        (consultant_employee_id IS NULL AND consultant_employee_no_snapshot IS NULL AND consultant_employee_name_snapshot IS NULL)
        OR
        (consultant_employee_id IS NOT NULL AND consultant_employee_no_snapshot IS NOT NULL AND consultant_employee_name_snapshot IS NOT NULL)
    );

DROP INDEX IF EXISTS ix_service_order_lines_order_service;
DROP INDEX IF EXISTS ix_service_order_lines_order_product;
DROP INDEX IF EXISTS uq_service_order_lines_service;
DROP INDEX IF EXISTS uq_service_order_lines_product;
DROP INDEX IF EXISTS "IX_service_order_lines_order_id_service_item_id";
DROP INDEX IF EXISTS "IX_service_order_lines_order_id_product_item_id";

CREATE INDEX ix_service_order_lines_order_service
    ON service_order_lines(order_id, service_item_id)
    WHERE line_type = 'Service';
CREATE INDEX ix_service_order_lines_order_product
    ON service_order_lines(order_id, product_item_id)
    WHERE line_type = 'Product';

CREATE TABLE service_order_visit_links (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    order_id uuid NOT NULL REFERENCES service_orders(id),
    visit_id uuid NOT NULL REFERENCES visits(id),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_service_order_visit_links_order_visit UNIQUE(order_id, visit_id)
);

CREATE INDEX ix_service_order_visit_links_tenant ON service_order_visit_links(tenant_id);
CREATE INDEX ix_service_order_visit_links_visit ON service_order_visit_links(visit_id);

INSERT INTO service_order_visit_links (
    id, tenant_id, order_id, visit_id, created_at_utc, updated_at_utc, version
)
SELECT gen_random_uuid(), tenant_id, id, visit_id, created_at_utc, updated_at_utc, 0
FROM service_orders;

CREATE TABLE service_order_prebill_snapshots (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    order_id uuid NOT NULL REFERENCES service_orders(id),
    prebill_no varchar(40) NOT NULL,
    payload_json jsonb NOT NULL,
    generated_by uuid NOT NULL REFERENCES identity_users(id),
    generated_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_service_order_prebill_no UNIQUE(tenant_id, prebill_no)
);

CREATE INDEX ix_service_order_prebills_tenant ON service_order_prebill_snapshots(tenant_id);
CREATE INDEX ix_service_order_prebills_order_time
    ON service_order_prebill_snapshots(order_id, generated_at_utc);
