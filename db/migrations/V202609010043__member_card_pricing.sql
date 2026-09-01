ALTER TABLE membership_card_types
    ADD COLUMN service_discount_basis_points integer NOT NULL DEFAULT 10000,
    ADD COLUMN product_discount_basis_points integer NOT NULL DEFAULT 10000,
    ADD CONSTRAINT ck_membership_card_types_service_discount
        CHECK (service_discount_basis_points BETWEEN 1000 AND 10000),
    ADD CONSTRAINT ck_membership_card_types_product_discount
        CHECK (product_discount_basis_points BETWEEN 1000 AND 10000);

ALTER TABLE service_order_lines
    ADD COLUMN pricing_source varchar(24) NOT NULL DEFAULT 'ListPrice',
    ADD COLUMN member_discount_basis_points integer NULL,
    ADD COLUMN member_card_type_id uuid NULL,
    ADD COLUMN member_card_type_name_snapshot varchar(80) NULL,
    ADD CONSTRAINT ck_service_order_lines_pricing_source
        CHECK (pricing_source IN ('ListPrice', 'MemberDiscount', 'ManualOverride')),
    ADD CONSTRAINT ck_service_order_lines_member_discount
        CHECK (member_discount_basis_points IS NULL OR
            member_discount_basis_points BETWEEN 1000 AND 9999);

UPDATE service_order_lines
SET pricing_source = 'ManualOverride'
WHERE entered_price_minor <> reference_price_minor;

CREATE INDEX ix_membership_cards_member_pricing
    ON membership_cards (tenant_id, customer_id, status, valid_from, valid_to, card_type_id);
