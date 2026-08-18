-- Append-only corrections for immutable customer service records.
CREATE TABLE customer_service_record_corrections (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    service_record_id uuid NOT NULL REFERENCES customer_service_records(id) ON DELETE RESTRICT,
    reason varchar(500) NOT NULL,
    condition_notes varchar(2000),
    service_content varchar(4000),
    follow_up_notes varchar(2000),
    command_id uuid NOT NULL UNIQUE,
    corrected_by uuid NOT NULL REFERENCES identity_users(id) ON DELETE RESTRICT,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_service_record_correction_reason CHECK (char_length(reason) BETWEEN 2 AND 500),
    CONSTRAINT ck_service_record_correction_condition CHECK (
        condition_notes IS NULL OR char_length(condition_notes) BETWEEN 1 AND 2000),
    CONSTRAINT ck_service_record_correction_content CHECK (
        service_content IS NULL OR char_length(service_content) BETWEEN 1 AND 4000),
    CONSTRAINT ck_service_record_correction_follow_up CHECK (
        follow_up_notes IS NULL OR char_length(follow_up_notes) BETWEEN 1 AND 2000)
);
CREATE INDEX ix_service_record_corrections_record_time
    ON customer_service_record_corrections (service_record_id, created_at_utc);

CREATE TRIGGER trg_customer_service_record_corrections_no_update
BEFORE UPDATE ON customer_service_record_corrections FOR EACH ROW
EXECUTE FUNCTION prevent_service_archive_mutation();
CREATE TRIGGER trg_customer_service_record_corrections_no_delete
BEFORE DELETE ON customer_service_record_corrections FOR EACH ROW
EXECUTE FUNCTION prevent_service_archive_mutation();
