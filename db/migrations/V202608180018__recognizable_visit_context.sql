ALTER TABLE visits
    ADD COLUMN planned_service_item_id uuid;

ALTER TABLE visits
    ADD CONSTRAINT fk_visits_planned_service_item_id
        FOREIGN KEY (planned_service_item_id) REFERENCES catalog_service_items(id) ON DELETE RESTRICT;

CREATE INDEX ix_visits_planned_service_item
    ON visits (tenant_id, planned_service_item_id)
    WHERE planned_service_item_id IS NOT NULL;

COMMENT ON COLUMN visits.planned_service_item_id IS
    'Optional reception hint used to identify a visit. It never creates a charge or fixes the final service order.';
