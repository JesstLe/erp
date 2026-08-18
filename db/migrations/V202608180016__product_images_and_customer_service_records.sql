-- Optional product images and immutable customer service records.
-- File bytes stay outside PostgreSQL; this table stores only encrypted-file metadata and integrity digests.

CREATE TABLE stored_files (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid REFERENCES organization_stores(id),
    purpose varchar(32) NOT NULL,
    storage_key varchar(260) NOT NULL UNIQUE,
    original_file_name varchar(180) NOT NULL,
    content_type varchar(40) NOT NULL,
    size_bytes bigint NOT NULL,
    sha256 bytea NOT NULL,
    created_by uuid NOT NULL REFERENCES identity_users(id),
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_stored_files_purpose CHECK (purpose IN ('ProductImage', 'ServiceRecordImage')),
    CONSTRAINT ck_stored_files_content_type CHECK (content_type IN ('image/jpeg', 'image/png', 'image/webp')),
    CONSTRAINT ck_stored_files_size CHECK (size_bytes BETWEEN 1 AND 5242880),
    CONSTRAINT ck_stored_files_sha256 CHECK (octet_length(sha256) = 32),
    CONSTRAINT ck_stored_files_store_scope CHECK (
        (purpose = 'ProductImage' AND store_id IS NULL) OR
        (purpose = 'ServiceRecordImage' AND store_id IS NOT NULL))
);
CREATE INDEX ix_stored_files_scope_time
    ON stored_files (tenant_id, store_id, purpose, created_at_utc DESC);

ALTER TABLE catalog_product_items
    ADD COLUMN image_file_id uuid REFERENCES stored_files(id) ON DELETE RESTRICT;
CREATE INDEX ix_catalog_product_items_image ON catalog_product_items (image_file_id)
    WHERE image_file_id IS NOT NULL;

CREATE TABLE customer_service_records (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    customer_id uuid NOT NULL REFERENCES customers(id) ON DELETE RESTRICT,
    service_order_id uuid REFERENCES service_orders(id) ON DELETE RESTRICT,
    service_occurred_at_utc timestamptz NOT NULL,
    condition_notes varchar(2000),
    service_content varchar(4000),
    follow_up_notes varchar(2000),
    command_id uuid NOT NULL UNIQUE,
    created_by uuid NOT NULL REFERENCES identity_users(id),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_customer_service_records_condition CHECK (
        condition_notes IS NULL OR char_length(condition_notes) BETWEEN 1 AND 2000),
    CONSTRAINT ck_customer_service_records_content CHECK (
        service_content IS NULL OR char_length(service_content) BETWEEN 1 AND 4000),
    CONSTRAINT ck_customer_service_records_follow_up CHECK (
        follow_up_notes IS NULL OR char_length(follow_up_notes) BETWEEN 1 AND 2000)
);
CREATE INDEX ix_customer_service_records_customer_time
    ON customer_service_records (store_id, customer_id, service_occurred_at_utc DESC);
CREATE INDEX ix_customer_service_records_order ON customer_service_records (service_order_id)
    WHERE service_order_id IS NOT NULL;

CREATE TABLE customer_service_record_attachments (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    service_record_id uuid NOT NULL REFERENCES customer_service_records(id) ON DELETE RESTRICT,
    file_id uuid NOT NULL REFERENCES stored_files(id) ON DELETE RESTRICT,
    sort_order integer NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_customer_service_record_attachment UNIQUE (service_record_id, file_id),
    CONSTRAINT uq_customer_service_record_sort UNIQUE (service_record_id, sort_order),
    CONSTRAINT ck_customer_service_record_sort CHECK (sort_order BETWEEN 0 AND 5)
);

-- Service archives and file metadata are append-only. Corrections use a new supplemental service record.
CREATE OR REPLACE FUNCTION prevent_service_archive_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'service archive records are append-only' USING ERRCODE = '55000';
END;
$$;

CREATE TRIGGER trg_stored_files_no_update
BEFORE UPDATE ON stored_files FOR EACH ROW EXECUTE FUNCTION prevent_service_archive_mutation();
CREATE TRIGGER trg_stored_files_no_delete
BEFORE DELETE ON stored_files FOR EACH ROW EXECUTE FUNCTION prevent_service_archive_mutation();
CREATE TRIGGER trg_customer_service_records_no_update
BEFORE UPDATE ON customer_service_records FOR EACH ROW EXECUTE FUNCTION prevent_service_archive_mutation();
CREATE TRIGGER trg_customer_service_records_no_delete
BEFORE DELETE ON customer_service_records FOR EACH ROW EXECUTE FUNCTION prevent_service_archive_mutation();
CREATE TRIGGER trg_customer_service_record_attachments_no_update
BEFORE UPDATE ON customer_service_record_attachments FOR EACH ROW EXECUTE FUNCTION prevent_service_archive_mutation();
CREATE TRIGGER trg_customer_service_record_attachments_no_delete
BEFORE DELETE ON customer_service_record_attachments FOR EACH ROW EXECUTE FUNCTION prevent_service_archive_mutation();
