ALTER TABLE service_orders
    ADD COLUMN source_channel varchar(80) NULL,
    ADD COLUMN manual_ticket_no varchar(80) NULL,
    ADD COLUMN male_guest_count integer NOT NULL DEFAULT 0,
    ADD COLUMN male_age_band varchar(32) NULL,
    ADD COLUMN female_guest_count integer NOT NULL DEFAULT 0,
    ADD COLUMN female_age_band varchar(32) NULL;

ALTER TABLE service_orders
    ADD CONSTRAINT ck_service_orders_guest_counts
        CHECK (male_guest_count BETWEEN 0 AND 99 AND female_guest_count BETWEEN 0 AND 99),
    ADD CONSTRAINT ck_service_orders_male_age_band
        CHECK (male_guest_count > 0 OR male_age_band IS NULL),
    ADD CONSTRAINT ck_service_orders_female_age_band
        CHECK (female_guest_count > 0 OR female_age_band IS NULL);

CREATE INDEX ix_service_orders_store_source_created
    ON service_orders(store_id, source_channel, created_at_utc DESC)
    WHERE source_channel IS NOT NULL;
