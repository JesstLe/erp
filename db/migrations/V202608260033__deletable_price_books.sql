ALTER TABLE service_orders
    ALTER COLUMN price_book_id DROP NOT NULL;

ALTER TABLE service_orders
    DROP CONSTRAINT IF EXISTS service_orders_price_book_id_fkey;

ALTER TABLE service_orders
    ADD CONSTRAINT service_orders_price_book_id_fkey
        FOREIGN KEY (price_book_id) REFERENCES catalog_price_books(id) ON DELETE SET NULL;
