-- Optional descriptive fields for store facility configuration.
-- Reference price is informational only and must never become an order price automatically.

ALTER TABLE facilities
    ADD COLUMN service_name varchar(120),
    ADD COLUMN equipment_name varchar(120),
    ADD COLUMN reference_price_minor bigint;

ALTER TABLE facilities
    ADD CONSTRAINT ck_facilities_service_name
        CHECK (service_name IS NULL OR length(btrim(service_name)) BETWEEN 1 AND 120),
    ADD CONSTRAINT ck_facilities_equipment_name
        CHECK (equipment_name IS NULL OR length(btrim(equipment_name)) BETWEEN 1 AND 120),
    ADD CONSTRAINT ck_facilities_reference_price
        CHECK (reference_price_minor IS NULL OR reference_price_minor BETWEEN 0 AND 10000000000);

COMMENT ON COLUMN facilities.reference_price_minor IS
    'Optional informational reference only; charging remains explicitly entered on the service order.';
