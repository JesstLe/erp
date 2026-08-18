-- V2 product sales and store-level inventory. Quantities are whole units in this version.
-- Sales reserve stock at order confirmation and post immutable movements only at settlement.

ALTER TABLE service_order_lines
    ADD COLUMN line_type varchar(16) NOT NULL DEFAULT 'Service',
    ADD COLUMN product_item_id uuid REFERENCES catalog_product_items(id),
    ADD COLUMN unit_name_snapshot varchar(20),
    ADD COLUMN returned_quantity integer NOT NULL DEFAULT 0;

ALTER TABLE service_order_lines ALTER COLUMN service_item_id DROP NOT NULL;
ALTER TABLE service_order_lines DROP CONSTRAINT uq_service_order_lines_item;
ALTER TABLE service_order_lines
    ADD CONSTRAINT ck_service_order_lines_type CHECK (line_type IN ('Service', 'Product')),
    ADD CONSTRAINT ck_service_order_lines_catalog_reference CHECK (
        (line_type = 'Service' AND service_item_id IS NOT NULL AND product_item_id IS NULL AND
            unit_name_snapshot IS NULL) OR
        (line_type = 'Product' AND service_item_id IS NULL AND product_item_id IS NOT NULL AND
            char_length(unit_name_snapshot) BETWEEN 1 AND 20)),
    ADD CONSTRAINT ck_service_order_lines_product_duration CHECK (
        line_type = 'Service' OR actual_seconds IS NULL),
    ADD CONSTRAINT ck_service_order_lines_returned_quantity CHECK (
        returned_quantity BETWEEN 0 AND quantity);

CREATE UNIQUE INDEX uq_service_order_lines_service
    ON service_order_lines (order_id, service_item_id) WHERE line_type = 'Service';
CREATE UNIQUE INDEX uq_service_order_lines_product
    ON service_order_lines (order_id, product_item_id) WHERE line_type = 'Product';
CREATE INDEX ix_service_order_lines_product_item
    ON service_order_lines (product_item_id) WHERE product_item_id IS NOT NULL;

CREATE TABLE inventory_balances (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    product_item_id uuid NOT NULL REFERENCES catalog_product_items(id),
    on_hand_quantity integer NOT NULL DEFAULT 0,
    reserved_quantity integer NOT NULL DEFAULT 0,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_inventory_balances_store_product UNIQUE (store_id, product_item_id),
    CONSTRAINT ck_inventory_balances_quantities CHECK (
        on_hand_quantity >= 0 AND reserved_quantity >= 0 AND reserved_quantity <= on_hand_quantity)
);
CREATE INDEX ix_inventory_balances_tenant_store ON inventory_balances (tenant_id, store_id);

CREATE TABLE inventory_documents (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    document_no varchar(40) NOT NULL,
    document_type varchar(24) NOT NULL,
    reason varchar(500) NOT NULL,
    posted_by uuid NOT NULL REFERENCES identity_users(id),
    posted_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_inventory_documents_tenant_no UNIQUE (tenant_id, document_no),
    CONSTRAINT ck_inventory_documents_type CHECK (
        document_type IN ('Opening', 'Receipt', 'AdjustmentIn', 'AdjustmentOut')),
    CONSTRAINT ck_inventory_documents_reason CHECK (char_length(reason) BETWEEN 1 AND 500)
);
CREATE INDEX ix_inventory_documents_store_posted
    ON inventory_documents (store_id, posted_at_utc DESC);

CREATE TABLE inventory_document_lines (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    document_id uuid NOT NULL REFERENCES inventory_documents(id) ON DELETE RESTRICT,
    product_item_id uuid NOT NULL REFERENCES catalog_product_items(id),
    quantity integer NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_inventory_document_lines_product UNIQUE (document_id, product_item_id),
    CONSTRAINT ck_inventory_document_lines_quantity CHECK (quantity BETWEEN 1 AND 1000000000)
);

CREATE TABLE inventory_reservations (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    order_id uuid NOT NULL REFERENCES service_orders(id) ON DELETE RESTRICT,
    order_line_id uuid NOT NULL REFERENCES service_order_lines(id) ON DELETE RESTRICT,
    product_item_id uuid NOT NULL REFERENCES catalog_product_items(id),
    balance_id uuid NOT NULL REFERENCES inventory_balances(id) ON DELETE RESTRICT,
    quantity integer NOT NULL,
    status varchar(16) NOT NULL,
    reserved_at_utc timestamptz NOT NULL,
    consumed_at_utc timestamptz,
    released_at_utc timestamptz,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_inventory_reservations_order_line UNIQUE (order_line_id),
    CONSTRAINT ck_inventory_reservations_quantity CHECK (quantity BETWEEN 1 AND 999),
    CONSTRAINT ck_inventory_reservations_status CHECK (status IN ('Active', 'Consumed', 'Released')),
    CONSTRAINT ck_inventory_reservations_completion CHECK (
        (status = 'Active' AND consumed_at_utc IS NULL AND released_at_utc IS NULL) OR
        (status = 'Consumed' AND consumed_at_utc IS NOT NULL AND released_at_utc IS NULL) OR
        (status = 'Released' AND released_at_utc IS NOT NULL AND consumed_at_utc IS NULL))
);
CREATE INDEX ix_inventory_reservations_order_status ON inventory_reservations (order_id, status);
CREATE UNIQUE INDEX uq_inventory_reservations_active_line
    ON inventory_reservations (order_line_id) WHERE status = 'Active';

CREATE TABLE product_returns (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    order_id uuid NOT NULL REFERENCES service_orders(id) ON DELETE RESTRICT,
    order_line_id uuid NOT NULL REFERENCES service_order_lines(id) ON DELETE RESTRICT,
    product_item_id uuid NOT NULL REFERENCES catalog_product_items(id),
    quantity integer NOT NULL,
    reason varchar(500) NOT NULL,
    command_id uuid NOT NULL UNIQUE,
    returned_by uuid NOT NULL REFERENCES identity_users(id),
    returned_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_product_returns_quantity CHECK (quantity BETWEEN 1 AND 999),
    CONSTRAINT ck_product_returns_reason CHECK (char_length(reason) BETWEEN 1 AND 500)
);
CREATE INDEX ix_product_returns_order_line ON product_returns (order_line_id, returned_at_utc);

CREATE TABLE inventory_movements (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    product_item_id uuid NOT NULL REFERENCES catalog_product_items(id),
    balance_id uuid NOT NULL REFERENCES inventory_balances(id) ON DELETE RESTRICT,
    movement_type varchar(24) NOT NULL,
    direction varchar(8) NOT NULL,
    quantity integer NOT NULL,
    on_hand_before integer NOT NULL,
    on_hand_after integer NOT NULL,
    source_type varchar(40) NOT NULL,
    source_id uuid NOT NULL,
    source_line_id uuid NOT NULL,
    command_id uuid NOT NULL,
    operator_id uuid REFERENCES identity_users(id),
    occurred_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_inventory_movements_source UNIQUE (movement_type, source_line_id),
    CONSTRAINT uq_inventory_movements_command_line UNIQUE (command_id, source_line_id),
    CONSTRAINT ck_inventory_movements_type CHECK (movement_type IN (
        'Opening', 'Receipt', 'SaleIssue', 'SalesReturn', 'AdjustmentIn', 'AdjustmentOut')),
    CONSTRAINT ck_inventory_movements_direction CHECK (direction IN ('In', 'Out')),
    CONSTRAINT ck_inventory_movements_balances CHECK (
        quantity > 0 AND on_hand_before >= 0 AND on_hand_after >= 0 AND
        ((direction = 'In' AND on_hand_after - on_hand_before = quantity) OR
         (direction = 'Out' AND on_hand_before - on_hand_after = quantity)))
);
CREATE INDEX ix_inventory_movements_store_product_time
    ON inventory_movements (store_id, product_item_id, occurred_at_utc DESC);

-- Inventory movement facts are append-only. Corrections use reversing documents/movements.
CREATE OR REPLACE FUNCTION prevent_inventory_movement_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'inventory movements are append-only' USING ERRCODE = '55000';
END;
$$;

CREATE TRIGGER trg_inventory_movements_no_update
BEFORE UPDATE ON inventory_movements FOR EACH ROW EXECUTE FUNCTION prevent_inventory_movement_mutation();
CREATE TRIGGER trg_inventory_movements_no_delete
BEFORE DELETE ON inventory_movements FOR EACH ROW EXECUTE FUNCTION prevent_inventory_movement_mutation();
