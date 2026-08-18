-- Persist cash tender and calculated change for receipt reproduction and shift review.
ALTER TABLE payments
    ADD COLUMN cash_tendered_minor bigint,
    ADD COLUMN cash_change_minor bigint;

ALTER TABLE payments ADD CONSTRAINT ck_payments_cash_tender_change CHECK (
    (cash_tendered_minor IS NULL AND cash_change_minor IS NULL) OR
    (cash_tendered_minor IS NOT NULL AND cash_change_minor IS NOT NULL AND
     cash_tendered_minor >= 0 AND cash_change_minor >= 0 AND
     cash_tendered_minor <= 10000000000 AND cash_change_minor <= cash_tendered_minor)
);
