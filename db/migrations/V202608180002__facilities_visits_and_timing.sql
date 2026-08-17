-- Facility configuration, visits, server-side timing, pauses and cleaning projection.

CREATE TABLE facility_groups (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    display_name varchar(50) NOT NULL,
    sort_order integer NOT NULL DEFAULT 0,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_facility_groups_store_name UNIQUE (store_id, display_name)
);
CREATE INDEX ix_facility_groups_tenant_id ON facility_groups (tenant_id);
CREATE INDEX ix_facility_groups_store_sort ON facility_groups (store_id, sort_order, display_name);

CREATE TABLE facility_types (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    display_name varchar(50) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_facility_types_tenant_name UNIQUE (tenant_id, display_name)
);
CREATE INDEX ix_facility_types_tenant_id ON facility_types (tenant_id);

CREATE TABLE facilities (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    group_id uuid NOT NULL REFERENCES facility_groups(id),
    facility_type_id uuid NOT NULL REFERENCES facility_types(id),
    code varchar(40) NOT NULL,
    display_name varchar(50) NOT NULL,
    sort_order integer NOT NULL DEFAULT 0,
    default_cleaning_minutes integer NOT NULL DEFAULT 0,
    allow_reservation boolean NOT NULL DEFAULT false,
    lifecycle_status varchar(24) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_facilities_store_code UNIQUE (store_id, code),
    CONSTRAINT ck_facilities_cleaning_minutes CHECK (default_cleaning_minutes BETWEEN 0 AND 1440),
    CONSTRAINT ck_facilities_lifecycle CHECK (lifecycle_status IN ('Enabled', 'Maintenance', 'Disabled'))
);
CREATE INDEX ix_facilities_tenant_id ON facilities (tenant_id);
CREATE INDEX ix_facilities_store_group_sort ON facilities (store_id, group_id, sort_order, display_name);

CREATE TABLE visits (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    visit_no varchar(40) NOT NULL,
    customer_id uuid,
    expected_duration_minutes integer,
    note varchar(500),
    arrived_at_utc timestamptz NOT NULL,
    service_ended_at_utc timestamptz,
    status varchar(32) NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_visits_tenant_no UNIQUE (tenant_id, visit_no),
    CONSTRAINT ck_visits_expected_duration CHECK (expected_duration_minutes IS NULL OR expected_duration_minutes BETWEEN 1 AND 1440),
    CONSTRAINT ck_visits_status CHECK (status IN ('Arrived', 'InService', 'ServiceEnded', 'LeftNoConsumption', 'Completed', 'Cancelled'))
);
CREATE INDEX ix_visits_tenant_id ON visits (tenant_id);
CREATE INDEX ix_visits_store_status ON visits (store_id, status, arrived_at_utc DESC);

CREATE TABLE facility_sessions (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    facility_id uuid NOT NULL REFERENCES facilities(id),
    visit_id uuid NOT NULL REFERENCES visits(id),
    status varchar(24) NOT NULL,
    started_at_utc timestamptz NOT NULL,
    ended_at_utc timestamptz,
    started_by_user_id uuid NOT NULL REFERENCES identity_users(id),
    start_command_id uuid NOT NULL,
    end_reason varchar(24),
    switch_group_id uuid,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_facility_sessions_start_command UNIQUE (start_command_id),
    CONSTRAINT ck_facility_sessions_status CHECK (status IN ('Active', 'Paused', 'Ended', 'Cancelled')),
    CONSTRAINT ck_facility_sessions_end_reason CHECK (end_reason IS NULL OR end_reason IN ('Completed', 'Switched', 'Mistaken'))
);
CREATE INDEX ix_facility_sessions_tenant_id ON facility_sessions (tenant_id);
CREATE INDEX ix_facility_sessions_visit ON facility_sessions (visit_id, started_at_utc);
CREATE UNIQUE INDEX uq_facility_sessions_active_facility
    ON facility_sessions (facility_id)
    WHERE status IN ('Active', 'Paused');

CREATE TABLE facility_session_pauses (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    session_id uuid NOT NULL REFERENCES facility_sessions(id) ON DELETE CASCADE,
    started_at_utc timestamptz NOT NULL,
    ended_at_utc timestamptz,
    started_by_user_id uuid NOT NULL REFERENCES identity_users(id),
    command_id uuid NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT uq_facility_session_pauses_command UNIQUE (command_id)
);
CREATE INDEX ix_facility_session_pauses_tenant_id ON facility_session_pauses (tenant_id);
CREATE UNIQUE INDEX uq_facility_session_pauses_open
    ON facility_session_pauses (session_id)
    WHERE ended_at_utc IS NULL;

CREATE TABLE facility_cleaning_tasks (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization_tenants(id),
    store_id uuid NOT NULL REFERENCES organization_stores(id),
    facility_id uuid NOT NULL REFERENCES facilities(id),
    session_id uuid NOT NULL REFERENCES facility_sessions(id),
    status varchar(24) NOT NULL,
    due_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz,
    completed_by_user_id uuid REFERENCES identity_users(id),
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_facility_cleaning_tasks_status CHECK (status IN ('Pending', 'Completed'))
);
CREATE INDEX ix_facility_cleaning_tasks_tenant_id ON facility_cleaning_tasks (tenant_id);
CREATE UNIQUE INDEX uq_facility_cleaning_tasks_pending
    ON facility_cleaning_tasks (facility_id)
    WHERE status = 'Pending';
