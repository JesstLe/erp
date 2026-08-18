-- P2-01 appointments and employee scheduling. Cancellations remain auditable history; no physical delete.

CREATE TABLE employee_shifts (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    employee_id uuid NOT NULL REFERENCES organization_employees(id),
    starts_at_utc timestamptz NOT NULL,
    ends_at_utc timestamptz NOT NULL,
    note varchar(500),
    status varchar(24) NOT NULL,
    created_by uuid NOT NULL REFERENCES identity_users(id),
    create_command_id uuid NOT NULL,
    cancelled_at_utc timestamptz,
    cancelled_by uuid REFERENCES identity_users(id),
    cancellation_reason varchar(500),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_employee_shifts_create_command UNIQUE (create_command_id),
    CONSTRAINT ck_employee_shifts_period CHECK (
        ends_at_utc >= starts_at_utc + interval '30 minutes' AND
        ends_at_utc <= starts_at_utc + interval '24 hours'),
    CONSTRAINT ck_employee_shifts_status CHECK (status IN ('Scheduled', 'Cancelled')),
    CONSTRAINT ck_employee_shifts_cancellation CHECK (
        (status = 'Scheduled' AND cancelled_at_utc IS NULL AND cancelled_by IS NULL AND cancellation_reason IS NULL) OR
        (status = 'Cancelled' AND cancelled_at_utc IS NOT NULL AND cancelled_by IS NOT NULL AND
         length(trim(cancellation_reason)) BETWEEN 1 AND 500))
);

CREATE INDEX ix_employee_shifts_tenant_id ON employee_shifts (tenant_id);
CREATE INDEX ix_employee_shifts_store_period ON employee_shifts (store_id, starts_at_utc, ends_at_utc);
CREATE INDEX ix_employee_shifts_employee_period ON employee_shifts (employee_id, starts_at_utc, ends_at_utc)
    WHERE status = 'Scheduled';

CREATE TABLE appointments (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    appointment_no varchar(40) NOT NULL,
    customer_id uuid NOT NULL REFERENCES customers(id),
    service_item_id uuid NOT NULL REFERENCES catalog_service_items(id),
    employee_id uuid REFERENCES organization_employees(id),
    facility_id uuid REFERENCES facilities(id),
    starts_at_utc timestamptz NOT NULL,
    ends_at_utc timestamptz NOT NULL,
    note varchar(500),
    status varchar(24) NOT NULL,
    created_by uuid NOT NULL REFERENCES identity_users(id),
    create_command_id uuid NOT NULL,
    visit_id uuid REFERENCES visits(id),
    arrived_by uuid REFERENCES identity_users(id),
    arrived_at_utc timestamptz,
    cancelled_at_utc timestamptz,
    cancelled_by uuid REFERENCES identity_users(id),
    cancellation_reason varchar(500),
    no_show_at_utc timestamptz,
    no_show_by uuid REFERENCES identity_users(id),
    no_show_reason varchar(500),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_appointments_tenant_no UNIQUE (tenant_id, appointment_no),
    CONSTRAINT uq_appointments_create_command UNIQUE (create_command_id),
    CONSTRAINT uq_appointments_visit UNIQUE (visit_id),
    CONSTRAINT ck_appointments_period CHECK (
        ends_at_utc >= starts_at_utc + interval '5 minutes' AND
        ends_at_utc <= starts_at_utc + interval '24 hours'),
    CONSTRAINT ck_appointments_status CHECK (status IN ('Scheduled', 'Arrived', 'Cancelled', 'NoShow')),
    CONSTRAINT ck_appointments_state_details CHECK (
        (status = 'Scheduled' AND visit_id IS NULL AND arrived_at_utc IS NULL AND arrived_by IS NULL AND
         cancelled_at_utc IS NULL AND cancelled_by IS NULL AND cancellation_reason IS NULL AND
         no_show_at_utc IS NULL AND no_show_by IS NULL AND no_show_reason IS NULL) OR
        (status = 'Arrived' AND visit_id IS NOT NULL AND arrived_at_utc IS NOT NULL AND arrived_by IS NOT NULL AND
         cancelled_at_utc IS NULL AND cancelled_by IS NULL AND cancellation_reason IS NULL AND
         no_show_at_utc IS NULL AND no_show_by IS NULL AND no_show_reason IS NULL) OR
        (status = 'Cancelled' AND visit_id IS NULL AND arrived_at_utc IS NULL AND arrived_by IS NULL AND
         cancelled_at_utc IS NOT NULL AND cancelled_by IS NOT NULL AND
         length(trim(cancellation_reason)) BETWEEN 1 AND 500 AND no_show_at_utc IS NULL AND no_show_by IS NULL AND
         no_show_reason IS NULL) OR
        (status = 'NoShow' AND visit_id IS NULL AND arrived_at_utc IS NULL AND arrived_by IS NULL AND
         cancelled_at_utc IS NULL AND cancelled_by IS NULL AND cancellation_reason IS NULL AND
         no_show_at_utc IS NOT NULL AND no_show_by IS NOT NULL))
);

CREATE INDEX ix_appointments_tenant_id ON appointments (tenant_id);
CREATE INDEX ix_appointments_store_period ON appointments (store_id, starts_at_utc, ends_at_utc);
CREATE INDEX ix_appointments_customer_period ON appointments (customer_id, starts_at_utc DESC);
CREATE INDEX ix_appointments_employee_period ON appointments (employee_id, starts_at_utc, ends_at_utc)
    WHERE status = 'Scheduled';
CREATE INDEX ix_appointments_facility_period ON appointments (facility_id, starts_at_utc, ends_at_utc)
    WHERE status = 'Scheduled';
