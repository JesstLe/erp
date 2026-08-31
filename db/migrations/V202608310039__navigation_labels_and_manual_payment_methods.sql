-- Brand-level navigation display names and the manual payment routes demonstrated by the legacy cashier.
-- Navigation keys remain stable route identifiers; only their visible labels are configurable.

ALTER TABLE organization_tenants
    ADD COLUMN navigation_labels_json jsonb NOT NULL DEFAULT '{}'::jsonb;

INSERT INTO payment_methods (id, tenant_id, code, name, category, internal_account_type,
    channel_provider, requires_open_shift, is_enabled, created_at_utc, updated_at_utc, version)
SELECT gen_random_uuid(), tenant.id, seed.code, seed.name, 'ManualExternal', NULL,
    NULL, true, true, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 0
FROM organization_tenants tenant
CROSS JOIN (VALUES
    ('BANK_CARD_MANUAL', '银行卡人工登记'),
    ('GROUP_BUY_MANUAL', '团购平台核销')
) AS seed(code, name)
WHERE NOT EXISTS (
    SELECT 1 FROM payment_methods existing
    WHERE existing.tenant_id = tenant.id AND existing.code = seed.code
);
