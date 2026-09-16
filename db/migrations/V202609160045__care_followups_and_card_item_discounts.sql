-- Caregiver attribution and follow-up reminders for customer service archives.
ALTER TABLE customer_service_records
    ADD COLUMN caregiver_employee_id uuid NULL REFERENCES organization_employees(id) ON DELETE RESTRICT,
    ADD COLUMN caregiver_name_snapshot varchar(100) NULL,
    ADD COLUMN follow_up_at_utc timestamptz NULL,
    ADD CONSTRAINT ck_customer_service_records_caregiver CHECK (
        (caregiver_employee_id IS NULL AND caregiver_name_snapshot IS NULL) OR
        (caregiver_employee_id IS NOT NULL AND caregiver_name_snapshot IS NOT NULL));

ALTER TABLE customer_service_record_corrections
    ADD COLUMN caregiver_employee_id uuid NULL REFERENCES organization_employees(id) ON DELETE RESTRICT,
    ADD COLUMN caregiver_name_snapshot varchar(100) NULL,
    ADD COLUMN follow_up_at_utc timestamptz NULL,
    ADD CONSTRAINT ck_service_record_corrections_caregiver CHECK (
        (caregiver_employee_id IS NULL AND caregiver_name_snapshot IS NULL) OR
        (caregiver_employee_id IS NOT NULL AND caregiver_name_snapshot IS NOT NULL));

CREATE INDEX ix_customer_service_records_follow_up
    ON customer_service_records (tenant_id, store_id, follow_up_at_utc)
    WHERE follow_up_at_utc IS NOT NULL;
CREATE INDEX ix_service_record_corrections_follow_up
    ON customer_service_record_corrections (tenant_id, follow_up_at_utc)
    WHERE follow_up_at_utc IS NOT NULL;

-- Card-wide discounts remain the fallback. These rows override the fallback for one service or product.
CREATE TABLE membership_card_item_discounts (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    card_type_id uuid NOT NULL REFERENCES membership_card_types(id) ON DELETE RESTRICT,
    target_type varchar(16) NOT NULL,
    catalog_item_id uuid NOT NULL,
    discount_basis_points integer NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_membership_card_item_discounts_target CHECK (target_type IN ('Service', 'Product')),
    CONSTRAINT ck_membership_card_item_discounts_value CHECK (discount_basis_points BETWEEN 1000 AND 10000),
    CONSTRAINT uq_membership_card_item_discounts UNIQUE
        (tenant_id, card_type_id, target_type, catalog_item_id)
);
CREATE INDEX ix_membership_card_item_discounts_card
    ON membership_card_item_discounts (tenant_id, card_type_id, target_type);
