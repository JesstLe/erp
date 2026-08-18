-- V2 original-route channel refunds. Approval only starts the provider refund; local financial
-- projections change after a trusted refund result confirms success.

ALTER TABLE payment_refunds
    DROP CONSTRAINT ck_payment_refunds_status,
    DROP CONSTRAINT ck_payment_refunds_completion,
    ADD CONSTRAINT ck_payment_refunds_status CHECK (
        status IN ('PendingApproval', 'Processing', 'Completed', 'Rejected')),
    ADD CONSTRAINT ck_payment_refunds_completion CHECK (
        (status = 'PendingApproval' AND approved_by IS NULL AND completed_at_utc IS NULL AND rejection_reason IS NULL) OR
        (status = 'Processing' AND approved_by IS NOT NULL AND completed_at_utc IS NULL AND rejection_reason IS NULL) OR
        (status = 'Completed' AND approved_by IS NOT NULL AND completed_at_utc IS NOT NULL AND rejection_reason IS NULL) OR
        (status = 'Rejected' AND approved_by IS NOT NULL AND completed_at_utc IS NULL AND rejection_reason IS NOT NULL));

ALTER TABLE payment_refund_lines
    DROP CONSTRAINT ck_payment_refund_lines_category,
    DROP CONSTRAINT ck_payment_refund_lines_route,
    DROP CONSTRAINT ck_payment_refund_lines_account,
    DROP CONSTRAINT ck_payment_refund_lines_cash_shift,
    ADD CONSTRAINT ck_payment_refund_lines_category CHECK (
        category IN ('Cash', 'InternalAccount', 'ChannelExternal')),
    ADD CONSTRAINT ck_payment_refund_lines_route CHECK (
        route IN ('OriginalCash', 'OriginalMemberAccount', 'OriginalChannel')),
    ADD CONSTRAINT ck_payment_refund_lines_account CHECK (
        (category = 'InternalAccount' AND member_account_id IS NOT NULL AND route = 'OriginalMemberAccount') OR
        (category = 'Cash' AND member_account_id IS NULL AND route = 'OriginalCash') OR
        (category = 'ChannelExternal' AND member_account_id IS NULL AND route = 'OriginalChannel')),
    ADD CONSTRAINT ck_payment_refund_lines_cash_shift CHECK (
        (category = 'Cash' AND ((completed_at_utc IS NULL AND cash_shift_id IS NULL) OR
            (completed_at_utc IS NOT NULL AND cash_shift_id IS NOT NULL))) OR
        (category IN ('InternalAccount', 'ChannelExternal') AND cash_shift_id IS NULL));

CREATE TABLE payment_channel_refunds (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    configuration_id uuid NOT NULL REFERENCES payment_channel_configurations(id) ON DELETE RESTRICT,
    refund_id uuid NOT NULL REFERENCES payment_refunds(id) ON DELETE RESTRICT,
    original_channel_order_id uuid NOT NULL REFERENCES payment_channel_orders(id) ON DELETE RESTRICT,
    provider varchar(24) NOT NULL,
    out_refund_no varchar(64) NOT NULL,
    out_trade_no varchar(64) NOT NULL,
    provider_trade_no varchar(128) NOT NULL,
    provider_refund_no varchar(128),
    amount_minor bigint NOT NULL,
    status varchar(24) NOT NULL,
    failure_code varchar(80),
    last_queried_at_utc timestamptz,
    succeeded_at_utc timestamptz,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_payment_channel_refunds_refund UNIQUE (refund_id),
    CONSTRAINT uq_payment_channel_refunds_out_no UNIQUE (configuration_id, out_refund_no),
    CONSTRAINT ck_payment_channel_refunds_provider CHECK (provider IN ('WeChatPay', 'Alipay')),
    CONSTRAINT ck_payment_channel_refunds_amount CHECK (amount_minor > 0 AND amount_minor <= 10000000000),
    CONSTRAINT ck_payment_channel_refunds_status CHECK (
        status IN ('Created', 'Processing', 'Succeeded', 'Failed')),
    CONSTRAINT ck_payment_channel_refunds_success CHECK (
        (status = 'Succeeded' AND succeeded_at_utc IS NOT NULL AND failure_code IS NULL) OR
        (status = 'Failed' AND succeeded_at_utc IS NULL AND failure_code IS NOT NULL) OR
        (status IN ('Created', 'Processing') AND succeeded_at_utc IS NULL))
);

CREATE INDEX ix_payment_channel_refunds_tenant_id ON payment_channel_refunds (tenant_id);
CREATE INDEX ix_payment_channel_refunds_status_created
    ON payment_channel_refunds (status, created_at_utc);
