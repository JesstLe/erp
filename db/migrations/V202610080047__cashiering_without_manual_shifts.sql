-- Payments and approved cash refunds no longer require a manual cashier shift.
-- Historical shift links and their foreign keys remain available for audit.
ALTER TABLE payment_allocations DROP CONSTRAINT ck_payment_allocations_shift;

ALTER TABLE payment_refund_lines DROP CONSTRAINT ck_payment_refund_lines_cash_shift;
ALTER TABLE payment_refund_lines ADD CONSTRAINT ck_payment_refund_lines_cash_shift CHECK (
    (category = 'Cash' AND (completed_at_utc IS NOT NULL OR cash_shift_id IS NULL)) OR
    (category IN ('InternalAccount', 'ChannelExternal') AND cash_shift_id IS NULL));
