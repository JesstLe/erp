-- Payment balance snapshots make receipts deterministic, while audit states must hold serialized
-- before/after catalog snapshots.
ALTER TABLE payments
    ADD COLUMN member_principal_balance_after_minor bigint NULL,
    ADD COLUMN member_bonus_balance_after_minor bigint NULL,
    ADD CONSTRAINT ck_payments_member_balance_after
        CHECK ((member_principal_balance_after_minor IS NULL AND member_bonus_balance_after_minor IS NULL) OR
               (member_principal_balance_after_minor >= 0 AND member_bonus_balance_after_minor >= 0));

ALTER TABLE audit_events
    ALTER COLUMN previous_state TYPE text,
    ALTER COLUMN current_state TYPE text;
