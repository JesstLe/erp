-- P2-02 partial stored-value refunds. Principal is refundable only with proportional bonus revocation.

ALTER TABLE member_topup_orders
    ADD COLUMN refunded_principal_minor bigint NOT NULL DEFAULT 0,
    ADD COLUMN revoked_bonus_minor bigint NOT NULL DEFAULT 0;

UPDATE member_topup_orders topup
SET refunded_principal_minor = LEAST(topup.principal_minor, payment.refunded_minor),
    revoked_bonus_minor = CASE WHEN payment.refunded_minor >= topup.principal_minor THEN topup.bonus_minor ELSE 0 END
FROM payments payment
WHERE payment.business_type = 'MemberTopup' AND payment.business_id = topup.id;

ALTER TABLE member_topup_orders ADD CONSTRAINT ck_member_topup_orders_refund_projection CHECK (
    refunded_principal_minor BETWEEN 0 AND principal_minor AND
    revoked_bonus_minor BETWEEN 0 AND bonus_minor AND
    revoked_bonus_minor = CEIL(
        bonus_minor::numeric * refunded_principal_minor::numeric / principal_minor::numeric
    )::bigint AND
    ((status = 'Paid' AND refunded_principal_minor = 0 AND revoked_bonus_minor = 0) OR
     (status = 'PartiallyRefunded' AND refunded_principal_minor > 0 AND
      refunded_principal_minor < principal_minor) OR
     (status = 'Refunded' AND refunded_principal_minor = principal_minor AND
      revoked_bonus_minor = bonus_minor) OR
     (status = 'Cancelled' AND refunded_principal_minor = 0 AND revoked_bonus_minor = 0))
);

CREATE INDEX ix_member_topup_orders_refundable
    ON member_topup_orders (store_id, paid_at_utc DESC)
    WHERE status IN ('Paid', 'PartiallyRefunded');
