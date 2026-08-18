-- P2-03 suppliers, purchase receipts, cost/expiry lots, stocktakes and inter-store transfers.

ALTER TABLE inventory_movements DROP CONSTRAINT ck_inventory_movements_type;
ALTER TABLE inventory_movements ADD CONSTRAINT ck_inventory_movements_type CHECK (movement_type IN (
    'Opening', 'Receipt', 'PurchaseReceipt', 'SaleIssue', 'SalesReturn', 'AdjustmentIn', 'AdjustmentOut',
    'StocktakeGain', 'StocktakeLoss', 'TransferOut', 'TransferIn'));

CREATE TABLE suppliers (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    code varchar(40) NOT NULL,
    name varchar(120) NOT NULL,
    contact_name varchar(80),
    mobile varchar(32),
    settlement_terms varchar(500),
    status varchar(24) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_suppliers_tenant_code UNIQUE (tenant_id, code),
    CONSTRAINT ck_suppliers_status CHECK (status IN ('Active', 'Disabled'))
);
CREATE INDEX ix_suppliers_tenant_status_name ON suppliers (tenant_id, status, name);

CREATE TABLE purchase_receipts (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    supplier_id uuid NOT NULL REFERENCES suppliers(id),
    receipt_no varchar(40) NOT NULL,
    external_no varchar(80),
    note varchar(500) NOT NULL,
    posted_by uuid NOT NULL REFERENCES identity_users(id),
    posted_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_purchase_receipts_tenant_no UNIQUE (tenant_id, receipt_no)
);
CREATE INDEX ix_purchase_receipts_store_time ON purchase_receipts (store_id, posted_at_utc DESC);
CREATE INDEX ix_purchase_receipts_supplier_time ON purchase_receipts (supplier_id, posted_at_utc DESC);

CREATE TABLE purchase_receipt_lines (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    receipt_id uuid NOT NULL REFERENCES purchase_receipts(id) ON DELETE RESTRICT,
    product_item_id uuid NOT NULL REFERENCES catalog_product_items(id),
    quantity integer NOT NULL,
    unit_cost_minor bigint NOT NULL,
    batch_no varchar(80) NOT NULL,
    expires_on date,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_purchase_receipt_line_batch UNIQUE (receipt_id, product_item_id, batch_no),
    CONSTRAINT ck_purchase_receipt_line_quantity CHECK (quantity BETWEEN 1 AND 1000000000),
    CONSTRAINT ck_purchase_receipt_line_cost CHECK (unit_cost_minor BETWEEN 0 AND 10000000000)
);

CREATE TABLE inventory_lots (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    product_item_id uuid NOT NULL REFERENCES catalog_product_items(id),
    batch_no varchar(80) NOT NULL,
    expires_on date,
    unit_cost_minor bigint NOT NULL,
    original_quantity integer NOT NULL,
    remaining_quantity integer NOT NULL,
    source_type varchar(40) NOT NULL,
    source_line_id uuid NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_inventory_lots_source UNIQUE (source_type, source_line_id),
    CONSTRAINT ck_inventory_lots_quantity CHECK (
        original_quantity BETWEEN 1 AND 1000000000 AND remaining_quantity BETWEEN 0 AND original_quantity),
    CONSTRAINT ck_inventory_lots_cost CHECK (unit_cost_minor BETWEEN 0 AND 10000000000)
);
CREATE INDEX ix_inventory_lots_fifo ON inventory_lots
    (store_id, product_item_id, expires_on NULLS LAST, created_at_utc, id)
    WHERE remaining_quantity > 0;
CREATE INDEX ix_inventory_lots_expiry ON inventory_lots (store_id, expires_on)
    WHERE remaining_quantity > 0 AND expires_on IS NOT NULL;

INSERT INTO inventory_lots (id, tenant_id, store_id, product_item_id, batch_no, expires_on,
    unit_cost_minor, original_quantity, remaining_quantity, source_type, source_line_id,
    created_at_utc, updated_at_utc, version)
SELECT gen_random_uuid(), tenant_id, store_id, product_item_id,
       'LEGACY-' || upper(substr(replace(id::text, '-', ''), 1, 12)), NULL, 0,
       on_hand_quantity, on_hand_quantity, 'LegacyBalance', id, now(), now(), 0
FROM inventory_balances WHERE on_hand_quantity > 0;

CREATE TABLE inventory_lot_allocations (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    movement_id uuid NOT NULL REFERENCES inventory_movements(id) ON DELETE RESTRICT,
    lot_id uuid NOT NULL REFERENCES inventory_lots(id) ON DELETE RESTRICT,
    quantity integer NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_inventory_lot_allocation UNIQUE (movement_id, lot_id),
    CONSTRAINT ck_inventory_lot_allocation_quantity CHECK (quantity > 0)
);
CREATE INDEX ix_inventory_lot_allocations_lot ON inventory_lot_allocations (lot_id);

CREATE TABLE stocktakes (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    stocktake_no varchar(40) NOT NULL,
    reason varchar(500) NOT NULL,
    requested_by uuid NOT NULL REFERENCES identity_users(id),
    frozen_at_utc timestamptz NOT NULL,
    status varchar(24) NOT NULL,
    approved_by uuid REFERENCES identity_users(id),
    posted_at_utc timestamptz,
    decision_reason varchar(500),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_stocktakes_tenant_no UNIQUE (tenant_id, stocktake_no),
    CONSTRAINT ck_stocktakes_status CHECK (status IN ('PendingApproval', 'Posted', 'Cancelled')),
    CONSTRAINT ck_stocktakes_projection CHECK (
        (status = 'PendingApproval' AND approved_by IS NULL AND posted_at_utc IS NULL) OR
        (status = 'Posted' AND approved_by IS NOT NULL AND posted_at_utc IS NOT NULL) OR
        (status = 'Cancelled' AND approved_by IS NOT NULL AND posted_at_utc IS NULL))
);
CREATE INDEX ix_stocktakes_store_status_time ON stocktakes (store_id, status, frozen_at_utc DESC);

CREATE TABLE stocktake_lines (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    stocktake_id uuid NOT NULL REFERENCES stocktakes(id) ON DELETE RESTRICT,
    product_item_id uuid NOT NULL REFERENCES catalog_product_items(id),
    book_quantity integer NOT NULL,
    counted_quantity integer NOT NULL,
    difference_quantity integer NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_stocktake_lines_product UNIQUE (stocktake_id, product_item_id),
    CONSTRAINT ck_stocktake_lines_quantity CHECK (
        book_quantity >= 0 AND counted_quantity >= 0 AND
        difference_quantity = counted_quantity - book_quantity)
);

CREATE TABLE inventory_transfers (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    source_store_id uuid NOT NULL REFERENCES organization_stores(id),
    destination_store_id uuid NOT NULL REFERENCES organization_stores(id),
    transfer_no varchar(40) NOT NULL,
    reason varchar(500) NOT NULL,
    requested_by uuid NOT NULL REFERENCES identity_users(id),
    requested_at_utc timestamptz NOT NULL,
    status varchar(24) NOT NULL,
    shipped_by uuid REFERENCES identity_users(id),
    shipped_at_utc timestamptz,
    received_by uuid REFERENCES identity_users(id),
    received_at_utc timestamptz,
    decision_reason varchar(500),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_inventory_transfers_tenant_no UNIQUE (tenant_id, transfer_no),
    CONSTRAINT ck_inventory_transfers_stores CHECK (source_store_id <> destination_store_id),
    CONSTRAINT ck_inventory_transfers_status CHECK (status IN ('Requested', 'InTransit', 'Received', 'Cancelled')),
    CONSTRAINT ck_inventory_transfers_projection CHECK (
        (status = 'Requested' AND shipped_at_utc IS NULL AND received_at_utc IS NULL) OR
        (status = 'InTransit' AND shipped_at_utc IS NOT NULL AND received_at_utc IS NULL) OR
        (status = 'Received' AND shipped_at_utc IS NOT NULL AND received_at_utc IS NOT NULL) OR
        (status = 'Cancelled' AND shipped_at_utc IS NULL AND received_at_utc IS NULL))
);
CREATE INDEX ix_inventory_transfers_source_status ON inventory_transfers
    (source_store_id, status, requested_at_utc DESC);
CREATE INDEX ix_inventory_transfers_destination_status ON inventory_transfers
    (destination_store_id, status, requested_at_utc DESC);

CREATE TABLE inventory_transfer_lines (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    transfer_id uuid NOT NULL REFERENCES inventory_transfers(id) ON DELETE RESTRICT,
    product_item_id uuid NOT NULL REFERENCES catalog_product_items(id),
    quantity integer NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_inventory_transfer_lines_product UNIQUE (transfer_id, product_item_id),
    CONSTRAINT ck_inventory_transfer_lines_quantity CHECK (quantity BETWEEN 1 AND 1000000000)
);

CREATE TABLE inventory_transfer_lots (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    transfer_line_id uuid NOT NULL REFERENCES inventory_transfer_lines(id) ON DELETE RESTRICT,
    source_lot_id uuid NOT NULL REFERENCES inventory_lots(id) ON DELETE RESTRICT,
    batch_no varchar(80) NOT NULL,
    expires_on date,
    unit_cost_minor bigint NOT NULL,
    quantity integer NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_inventory_transfer_lots_source UNIQUE (transfer_line_id, source_lot_id),
    CONSTRAINT ck_inventory_transfer_lots_quantity CHECK (quantity > 0),
    CONSTRAINT ck_inventory_transfer_lots_cost CHECK (unit_cost_minor BETWEEN 0 AND 10000000000)
);

CREATE TRIGGER trg_inventory_lot_allocations_immutable
    BEFORE UPDATE OR DELETE ON inventory_lot_allocations
    FOR EACH ROW EXECUTE FUNCTION prevent_inventory_movement_mutation();
CREATE TRIGGER trg_purchase_receipts_immutable
    BEFORE UPDATE OR DELETE ON purchase_receipts
    FOR EACH ROW EXECUTE FUNCTION prevent_inventory_movement_mutation();
CREATE TRIGGER trg_purchase_receipt_lines_immutable
    BEFORE UPDATE OR DELETE ON purchase_receipt_lines
    FOR EACH ROW EXECUTE FUNCTION prevent_inventory_movement_mutation();
CREATE TRIGGER trg_stocktake_lines_immutable
    BEFORE UPDATE OR DELETE ON stocktake_lines
    FOR EACH ROW EXECUTE FUNCTION prevent_inventory_movement_mutation();
CREATE TRIGGER trg_inventory_transfer_lines_immutable
    BEFORE UPDATE OR DELETE ON inventory_transfer_lines
    FOR EACH ROW EXECUTE FUNCTION prevent_inventory_movement_mutation();
CREATE TRIGGER trg_inventory_transfer_lots_immutable
    BEFORE UPDATE OR DELETE ON inventory_transfer_lots
    FOR EACH ROW EXECUTE FUNCTION prevent_inventory_movement_mutation();
