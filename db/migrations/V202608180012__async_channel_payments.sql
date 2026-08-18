-- V2 asynchronous WeChat Pay Native and Alipay order-code payments.
-- Channel payments start as pending and can only become confirmed after a verified provider result.

ALTER TABLE payment_methods
    DROP CONSTRAINT ck_payment_methods_category,
    ADD COLUMN channel_provider varchar(24),
    ADD CONSTRAINT ck_payment_methods_category CHECK (
        category IN ('Cash', 'ManualExternal', 'InternalAccount', 'ChannelExternal')),
    ADD CONSTRAINT ck_payment_methods_channel_provider CHECK (
        (category = 'ChannelExternal' AND channel_provider IN ('WeChatPay', 'Alipay')) OR
        (category <> 'ChannelExternal' AND channel_provider IS NULL));

INSERT INTO payment_methods (id, tenant_id, code, name, category, internal_account_type,
    channel_provider, requires_open_shift, is_enabled, created_at_utc, updated_at_utc, version)
SELECT gen_random_uuid(), tenant.id, seed.code, seed.name, 'ChannelExternal', NULL,
    seed.provider, true, false, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 0
FROM organization_tenants tenant
CROSS JOIN (VALUES
    ('WECHAT_NATIVE', '微信支付 Native', 'WeChatPay'),
    ('ALIPAY_QR', '支付宝订单码', 'Alipay')
) AS seed(code, name, provider)
WHERE NOT EXISTS (
    SELECT 1 FROM payment_methods existing
    WHERE existing.tenant_id = tenant.id AND existing.code = seed.code
);

ALTER TABLE payment_allocations
    DROP CONSTRAINT ck_payment_allocations_category,
    DROP CONSTRAINT ck_payment_allocations_confirmation,
    DROP CONSTRAINT ck_payment_allocations_shift,
    ALTER COLUMN external_reference TYPE varchar(128),
    ALTER COLUMN confirmed_at_utc DROP NOT NULL,
    ADD COLUMN channel_provider varchar(24),
    ADD CONSTRAINT ck_payment_allocations_category CHECK (
        category IN ('Cash', 'ManualExternal', 'InternalAccount', 'ChannelExternal')),
    ADD CONSTRAINT ck_payment_allocations_confirmation CHECK (confirmation_status IN
        ('CashRecorded', 'ManualPendingReconciliation', 'InternalConfirmed', 'ChannelPending',
         'ChannelConfirmed', 'Failed', 'Cancelled')),
    ADD CONSTRAINT ck_payment_allocations_shift CHECK (
        category NOT IN ('Cash', 'ManualExternal', 'ChannelExternal') OR shift_id IS NOT NULL),
    ADD CONSTRAINT ck_payment_allocations_channel_provider CHECK (
        (category = 'ChannelExternal' AND channel_provider IN ('WeChatPay', 'Alipay')) OR
        (category <> 'ChannelExternal' AND channel_provider IS NULL)),
    ADD CONSTRAINT ck_payment_allocations_channel_state CHECK (
        (category = 'ChannelExternal' AND (
            (confirmation_status = 'ChannelPending' AND external_reference IS NULL AND
             confirmed_at_utc IS NULL AND reconciliation_status = 'Pending') OR
            (confirmation_status = 'ChannelConfirmed' AND external_reference IS NOT NULL AND
             confirmed_at_utc IS NOT NULL AND reconciliation_status IN ('Pending', 'Matched', 'Difference', 'Resolved')) OR
            (confirmation_status IN ('Failed', 'Cancelled') AND confirmed_at_utc IS NULL)
        )) OR
        (category <> 'ChannelExternal' AND confirmation_status NOT IN ('ChannelPending', 'ChannelConfirmed') AND
         channel_provider IS NULL AND confirmed_at_utc IS NOT NULL));

CREATE INDEX ix_payment_methods_channel_provider
    ON payment_methods (tenant_id, channel_provider, is_enabled)
    WHERE channel_provider IS NOT NULL;
CREATE INDEX ix_payment_allocations_channel_provider
    ON payment_allocations (tenant_id, channel_provider, confirmation_status)
    WHERE channel_provider IS NOT NULL;

-- A confirmed channel close releases the business order for another payment attempt while preserving history.
DROP INDEX uq_payments_order;
DROP INDEX uq_payments_business;
CREATE UNIQUE INDEX uq_payments_order
    ON payments (order_id) WHERE order_id IS NOT NULL AND
        status IN ('Processing', 'Paid', 'PartiallyRefunded', 'Refunded');
CREATE UNIQUE INDEX uq_payments_business
    ON payments (tenant_id, business_type, business_id)
        WHERE status IN ('Processing', 'Paid', 'PartiallyRefunded', 'Refunded');
