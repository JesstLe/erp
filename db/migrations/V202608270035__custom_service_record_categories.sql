-- Brand-scoped service-record categories. Industry-specific legacy labels are intentionally not seeded.

CREATE TABLE customer_service_record_categories (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    code varchar(40) NOT NULL,
    name varchar(60) NOT NULL,
    sort_order integer NOT NULL DEFAULT 0,
    status varchar(24) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_customer_service_record_categories_tenant_code UNIQUE (tenant_id, code),
    CONSTRAINT uq_customer_service_record_categories_tenant_name UNIQUE (tenant_id, name),
    CONSTRAINT ck_customer_service_record_categories_sort_order CHECK (sort_order BETWEEN 0 AND 9999),
    CONSTRAINT ck_customer_service_record_categories_status CHECK (status IN ('Enabled', 'Disabled'))
);

CREATE INDEX ix_customer_service_record_categories_tenant_id
    ON customer_service_record_categories (tenant_id, sort_order, code);

ALTER TABLE customer_service_records
    ADD COLUMN category_id uuid NULL;

ALTER TABLE customer_service_records
    ADD CONSTRAINT fk_customer_service_records_category
        FOREIGN KEY (category_id) REFERENCES customer_service_record_categories(id) ON DELETE RESTRICT;

CREATE INDEX ix_customer_service_records_category_id
    ON customer_service_records (tenant_id, category_id, service_occurred_at_utc DESC);
