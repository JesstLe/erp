-- V1 employees and login-account lifecycle. Employee history remains independent from Identity credentials.

ALTER TABLE identity_users
    ADD COLUMN must_change_password boolean NOT NULL DEFAULT false;

CREATE TABLE organization_employees (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    employee_no varchar(32) NOT NULL,
    display_name varchar(100) NOT NULL,
    position_code varchar(40) NOT NULL,
    user_id uuid REFERENCES identity_users(id),
    status varchar(24) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_organization_employees_tenant_no UNIQUE (tenant_id, employee_no),
    CONSTRAINT uq_organization_employees_user UNIQUE (user_id),
    CONSTRAINT ck_organization_employees_status CHECK (status IN ('Active', 'Inactive'))
);

CREATE INDEX ix_organization_employees_tenant_id ON organization_employees (tenant_id);

CREATE TABLE organization_employee_stores (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    employee_id uuid NOT NULL REFERENCES organization_employees(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    is_primary boolean NOT NULL DEFAULT false,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_organization_employee_stores UNIQUE (employee_id, store_id)
);

CREATE INDEX ix_organization_employee_stores_tenant_id ON organization_employee_stores (tenant_id);
CREATE INDEX ix_organization_employee_stores_store_id ON organization_employee_stores (store_id);
