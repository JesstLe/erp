-- Generate immutable brand and store codes with database-serialized counters.
-- Historical codes are preserved; store counters start after each tenant's largest numeric S-code.

CREATE TABLE platform_code_sequences (
    sequence_name varchar(32) NOT NULL,
    scope_key varchar(64) NOT NULL,
    current_value bigint NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    PRIMARY KEY (sequence_name, scope_key),
    CONSTRAINT ck_platform_code_sequences_value CHECK (current_value >= 0)
);

INSERT INTO platform_code_sequences (sequence_name, scope_key, current_value, updated_at_utc)
SELECT 'STORE', replace(tenant_id::text, '-', ''),
       COALESCE(MAX(substring(code FROM '^S([0-9]+)$')::bigint), 0), now()
FROM organization_stores
WHERE code ~ '^S[0-9]+$'
GROUP BY tenant_id;
