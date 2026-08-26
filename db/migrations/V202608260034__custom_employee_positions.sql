-- Brand-scoped, customizable employee positions. Authorization roles remain a separate security concept.

CREATE TABLE organization_employee_positions (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    code varchar(40) NOT NULL,
    name varchar(60) NOT NULL,
    sort_order integer NOT NULL DEFAULT 0,
    status varchar(24) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_organization_employee_positions_tenant_code UNIQUE (tenant_id, code),
    CONSTRAINT ck_organization_employee_positions_sort_order CHECK (sort_order BETWEEN 0 AND 9999),
    CONSTRAINT ck_organization_employee_positions_status CHECK (status IN ('Enabled', 'Disabled'))
);

CREATE INDEX ix_organization_employee_positions_tenant_id
    ON organization_employee_positions (tenant_id);

-- Preserve every position already referenced by an employee. Known legacy/system codes receive neutral labels.
INSERT INTO organization_employee_positions
    (id, tenant_id, code, name, sort_order, status, created_at_utc, updated_at_utc, version)
SELECT gen_random_uuid(), employee.tenant_id, employee.position_code,
       CASE employee.position_code
           WHEN 'OWNER' THEN '负责人'
           WHEN 'STORE_MANAGER' THEN '门店负责人'
           WHEN 'FRONT_DESK' THEN '前台'
           WHEN 'CASHIER' THEN '收银员'
           WHEN 'TECHNICIAN' THEN '服务人员'
           WHEN 'OTHER' THEN '其他岗位'
           ELSE employee.position_code
       END,
       100, 'Enabled', now(), now(), 0
FROM organization_employees employee
GROUP BY employee.tenant_id, employee.position_code;

-- Give every existing brand a small, industry-neutral starting dictionary.
INSERT INTO organization_employee_positions
    (id, tenant_id, code, name, sort_order, status, created_at_utc, updated_at_utc, version)
SELECT gen_random_uuid(), tenant.id, defaults.code, defaults.name, defaults.sort_order,
       'Enabled', now(), now(), 0
FROM organization_tenants tenant
CROSS JOIN (VALUES
    ('OWNER', '负责人', 10),
    ('STORE_MANAGER', '门店负责人', 20),
    ('STAFF', '员工', 30),
    ('OTHER', '其他岗位', 999)
) AS defaults(code, name, sort_order)
ON CONFLICT (tenant_id, code) DO NOTHING;
