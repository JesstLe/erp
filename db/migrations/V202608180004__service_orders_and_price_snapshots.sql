-- Service-order drafts and immutable price snapshots. Facility timing remains an independent fact.

CREATE TABLE service_orders (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    visit_id uuid NOT NULL REFERENCES visits(id),
    customer_id uuid REFERENCES customers(id),
    order_no varchar(40) NOT NULL,
    price_book_id uuid NOT NULL REFERENCES catalog_price_books(id),
    note varchar(1000),
    status varchar(32) NOT NULL,
    reference_amount_minor bigint NOT NULL,
    receivable_minor bigint NOT NULL,
    confirmed_at_utc timestamptz,
    settled_at_utc timestamptz,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_service_orders_tenant_no UNIQUE (tenant_id, order_no),
    CONSTRAINT ck_service_orders_status CHECK (status IN ('Draft', 'PendingPayment', 'Settled', 'Voided')),
    CONSTRAINT ck_service_orders_amounts CHECK (reference_amount_minor >= 0 AND receivable_minor >= 0)
);
CREATE INDEX ix_service_orders_tenant_id ON service_orders (tenant_id);
CREATE INDEX ix_service_orders_store_status_created ON service_orders (store_id, status, created_at_utc DESC);
CREATE UNIQUE INDEX uq_service_orders_active_visit ON service_orders (visit_id) WHERE status <> 'Voided';

CREATE TABLE service_order_lines (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    order_id uuid NOT NULL REFERENCES service_orders(id) ON DELETE RESTRICT,
    service_item_id uuid NOT NULL REFERENCES catalog_service_items(id),
    item_code_snapshot varchar(40) NOT NULL,
    item_name_snapshot varchar(120) NOT NULL,
    quantity integer NOT NULL,
    actual_seconds integer,
    reference_price_minor bigint NOT NULL,
    entered_price_minor bigint NOT NULL,
    reference_amount_minor bigint NOT NULL,
    line_amount_minor bigint NOT NULL,
    price_override_reason varchar(500),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_service_order_lines_item UNIQUE (order_id, service_item_id),
    CONSTRAINT ck_service_order_lines_quantity CHECK (quantity BETWEEN 1 AND 999),
    CONSTRAINT ck_service_order_lines_duration CHECK (actual_seconds IS NULL OR actual_seconds BETWEEN 0 AND 86400),
    CONSTRAINT ck_service_order_lines_amounts CHECK (
        reference_price_minor BETWEEN 0 AND 10000000000 AND
        entered_price_minor BETWEEN 0 AND 10000000000 AND
        reference_amount_minor >= 0 AND line_amount_minor >= 0),
    CONSTRAINT ck_service_order_lines_override_reason CHECK (
        entered_price_minor = reference_price_minor OR char_length(price_override_reason) BETWEEN 2 AND 500)
);
CREATE INDEX ix_service_order_lines_tenant_id ON service_order_lines (tenant_id);
CREATE INDEX ix_service_order_lines_order ON service_order_lines (order_id);
