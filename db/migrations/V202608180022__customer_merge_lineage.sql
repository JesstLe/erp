-- Logical customer merge lineage. Historical orders, visits, service archives and ledgers keep their
-- original customer_id; application reads resolve the lineage so immutable facts are never rewritten.

ALTER TABLE customers
    ADD COLUMN merged_into_customer_id uuid REFERENCES customers(id) ON DELETE RESTRICT,
    ADD COLUMN merged_at_utc timestamptz,
    ADD COLUMN merged_by uuid REFERENCES identity_users(id) ON DELETE RESTRICT,
    ADD COLUMN merge_reason varchar(500),
    ADD CONSTRAINT ck_customers_merge_target CHECK (
        merged_into_customer_id IS NULL OR merged_into_customer_id <> id),
    ADD CONSTRAINT ck_customers_merge_metadata CHECK (
        (status <> 'Merged' AND merged_into_customer_id IS NULL AND merged_at_utc IS NULL AND
            merged_by IS NULL AND merge_reason IS NULL)
        OR
        (status = 'Merged' AND (
            (merged_into_customer_id IS NULL AND merged_at_utc IS NULL AND merged_by IS NULL AND
                merge_reason IS NULL)
            OR
            (merged_into_customer_id IS NOT NULL AND merged_at_utc IS NOT NULL AND merged_by IS NOT NULL AND
                char_length(merge_reason) BETWEEN 2 AND 500))));

CREATE INDEX ix_customers_merged_into
    ON customers (tenant_id, merged_into_customer_id)
    WHERE merged_into_customer_id IS NOT NULL;
