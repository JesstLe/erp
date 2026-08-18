CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE INDEX ix_catalog_service_items_code_trgm
    ON catalog_service_items USING gin (code gin_trgm_ops);
CREATE INDEX ix_catalog_service_items_name_trgm
    ON catalog_service_items USING gin (name gin_trgm_ops);

CREATE INDEX ix_catalog_product_items_code_trgm
    ON catalog_product_items USING gin (code gin_trgm_ops);
CREATE INDEX ix_catalog_product_items_name_trgm
    ON catalog_product_items USING gin (name gin_trgm_ops);

CREATE INDEX ix_customers_name_trgm
    ON customers USING gin (name gin_trgm_ops);

CREATE INDEX ix_organization_employees_no_trgm
    ON organization_employees USING gin (employee_no gin_trgm_ops);
CREATE INDEX ix_organization_employees_name_trgm
    ON organization_employees USING gin (display_name gin_trgm_ops);
CREATE INDEX ix_organization_employees_position_trgm
    ON organization_employees USING gin (position_code gin_trgm_ops);

CREATE INDEX ix_identity_users_username_trgm
    ON identity_users USING gin (user_name gin_trgm_ops)
    WHERE user_name IS NOT NULL;

CREATE INDEX ix_organization_stores_code_trgm
    ON organization_stores USING gin (code gin_trgm_ops);
CREATE INDEX ix_organization_stores_name_trgm
    ON organization_stores USING gin (name gin_trgm_ops);
