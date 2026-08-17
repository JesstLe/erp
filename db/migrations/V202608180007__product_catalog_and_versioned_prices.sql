-- V1 product master and versioned standard prices. Inventory movements and product sales remain out of scope.

CREATE TABLE catalog_product_items (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    code varchar(40) NOT NULL,
    name varchar(120) NOT NULL,
    unit_name varchar(20) NOT NULL,
    track_inventory boolean NOT NULL DEFAULT false,
    status varchar(24) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_catalog_product_items_tenant_code UNIQUE (tenant_id, code),
    CONSTRAINT ck_catalog_product_items_status CHECK (status IN ('Enabled', 'Disabled'))
);

CREATE INDEX ix_catalog_product_items_tenant_id ON catalog_product_items (tenant_id);

CREATE TABLE catalog_price_book_product_lines (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    price_book_id uuid NOT NULL REFERENCES catalog_price_books(id) ON DELETE CASCADE,
    product_item_id uuid NOT NULL REFERENCES catalog_product_items(id),
    unit_price_minor bigint NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_catalog_price_book_product_lines UNIQUE (price_book_id, product_item_id),
    CONSTRAINT ck_catalog_price_book_product_lines_price CHECK (unit_price_minor >= 0)
);

CREATE INDEX ix_catalog_price_book_product_lines_tenant_id ON catalog_price_book_product_lines (tenant_id);
CREATE INDEX ix_catalog_price_book_product_lines_product ON catalog_price_book_product_lines (product_item_id);
