-- V1 permission catalog. Grants are additive so existing tenant-specific records and audit history remain intact.
WITH all_actions(action) AS (
    VALUES
        ('dashboard.read'), ('catalog.read'), ('catalog.write'), ('price.publish'),
        ('facility.operate'), ('facility.configure'), ('facility.configure.all-stores'),
        ('scheduling.operate'), ('scheduling.shift.manage'),
        ('customer.read'), ('customer.write'), ('customer.manage'), ('customer.export'),
        ('customer.merge'), ('customer.export.full-mobile'),
        ('membership.open'), ('membership.card-type.manage'), ('membership.topup'),
        ('membership.manage'), ('membership.admin'), ('membership.grant-bonus'), ('membership.reverse'),
        ('service-record.manage'), ('cashier.checkout'), ('cashier.approve-price'),
        ('refund.approve'), ('refund.request'), ('shift.review'),
        ('inventory.read'), ('inventory.write'), ('supply-chain.read'), ('supply-chain.operate'), ('supply-chain.manage'),
        ('report.read'), ('audit.read'), ('organization.manage'), ('employee.manage'),
        ('payment-channel.read'), ('payment-channel.manage')
), role_defaults(role_name, action) AS (
    SELECT 'OWNER', action FROM all_actions
    UNION ALL VALUES
        ('STORE_MANAGER', 'dashboard.read'),
        ('STORE_MANAGER', 'catalog.read'),
        ('STORE_MANAGER', 'facility.operate'),
        ('STORE_MANAGER', 'facility.configure'),
        ('STORE_MANAGER', 'scheduling.operate'),
        ('STORE_MANAGER', 'scheduling.shift.manage'),
        ('STORE_MANAGER', 'customer.read'),
        ('STORE_MANAGER', 'customer.write'),
        ('STORE_MANAGER', 'customer.manage'),
        ('STORE_MANAGER', 'customer.export'),
        ('STORE_MANAGER', 'membership.open'),
        ('STORE_MANAGER', 'membership.topup'),
        ('STORE_MANAGER', 'membership.manage'),
        ('STORE_MANAGER', 'membership.admin'),
        ('STORE_MANAGER', 'service-record.manage'),
        ('STORE_MANAGER', 'cashier.checkout'),
        ('STORE_MANAGER', 'refund.request'),
        ('STORE_MANAGER', 'shift.review'),
        ('STORE_MANAGER', 'inventory.read'),
        ('STORE_MANAGER', 'supply-chain.read'),
        ('STORE_MANAGER', 'supply-chain.operate'),
        ('STORE_MANAGER', 'report.read'),
        ('STORE_MANAGER', 'audit.read'),
        ('STORE_MANAGER', 'payment-channel.read'),
        ('FRONT_DESK', 'dashboard.read'),
        ('FRONT_DESK', 'catalog.read'),
        ('FRONT_DESK', 'facility.operate'),
        ('FRONT_DESK', 'scheduling.operate'),
        ('FRONT_DESK', 'customer.read'),
        ('FRONT_DESK', 'customer.write'),
        ('CASHIER', 'dashboard.read'),
        ('CASHIER', 'catalog.read'),
        ('CASHIER', 'customer.read'),
        ('CASHIER', 'customer.write'),
        ('CASHIER', 'membership.topup'),
        ('CASHIER', 'membership.manage'),
        ('CASHIER', 'cashier.checkout'),
        ('TECHNICIAN', 'dashboard.read'),
        ('TECHNICIAN', 'catalog.read')
)
INSERT INTO authorization_role_permissions
    (id, tenant_id, role_id, action, created_at_utc, updated_at_utc, version)
SELECT gen_random_uuid(), role.tenant_id, role.id, defaults.action, now(), now(), 0
FROM identity_roles role
JOIN role_defaults defaults ON defaults.role_name = role.normalized_name
ON CONFLICT (role_id, action) DO NOTHING;
