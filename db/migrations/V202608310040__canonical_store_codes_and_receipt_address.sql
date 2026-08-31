-- Canonicalize the five legacy rehearsal stores and add the address used by customer receipts.
-- The renumbering is deliberately scoped to B01 and accepts only the known pre-migration or final state.

ALTER TABLE organization_stores
    ADD COLUMN address varchar(300);

DO $$
DECLARE
    rehearsal_tenant_id uuid;
    store_count integer;
    canonical_count integer;
    legacy_count integer;
BEGIN
    SELECT id INTO rehearsal_tenant_id
    FROM organization_tenants
    WHERE code = 'B01';

    IF rehearsal_tenant_id IS NULL THEN
        RETURN;
    END IF;

    SELECT count(*) INTO store_count
    FROM organization_stores
    WHERE tenant_id = rehearsal_tenant_id;

    SELECT count(*) INTO canonical_count
    FROM organization_stores
    WHERE tenant_id = rehearsal_tenant_id
      AND (code, name) IN (
          ('S001', '博足修脚'),
          ('S002', '交通局店2店'),
          ('S003', '金辰公馆4店'),
          ('S004', '水木清华3店'),
          ('S005', '王电街状元府5店')
      );

    IF store_count = 5 AND canonical_count = 5 THEN
        INSERT INTO platform_code_sequences (sequence_name, scope_key, current_value, updated_at_utc)
        VALUES ('STORE', replace(rehearsal_tenant_id::text, '-', ''), 5, CURRENT_TIMESTAMP)
        ON CONFLICT (sequence_name, scope_key)
        DO UPDATE SET current_value = GREATEST(platform_code_sequences.current_value, 5),
                      updated_at_utc = EXCLUDED.updated_at_utc;
        RETURN;
    END IF;

    SELECT count(*) INTO legacy_count
    FROM organization_stores
    WHERE tenant_id = rehearsal_tenant_id
      AND (code, name) IN (
          ('S01', '博足修脚'),
          ('S003', '交通局店2店'),
          ('S004', '金辰公馆4店'),
          ('S005', '水木清华3店'),
          ('S006', '王电街状元府5店')
      );

    IF store_count <> 5 OR legacy_count <> 5 THEN
        RAISE EXCEPTION 'B01 store-code state is not the reviewed five-store mapping; refusing automatic renumbering';
    END IF;

    UPDATE organization_stores
    SET code = 'TMP-' || left(md5(id::text), 28),
        updated_at_utc = CURRENT_TIMESTAMP,
        version = version + 1
    WHERE tenant_id = rehearsal_tenant_id;

    UPDATE organization_stores
    SET code = CASE name
            WHEN '博足修脚' THEN 'S001'
            WHEN '交通局店2店' THEN 'S002'
            WHEN '金辰公馆4店' THEN 'S003'
            WHEN '水木清华3店' THEN 'S004'
            WHEN '王电街状元府5店' THEN 'S005'
        END,
        updated_at_utc = CURRENT_TIMESTAMP
    WHERE tenant_id = rehearsal_tenant_id;

    INSERT INTO platform_code_sequences (sequence_name, scope_key, current_value, updated_at_utc)
    VALUES ('STORE', replace(rehearsal_tenant_id::text, '-', ''), 5, CURRENT_TIMESTAMP)
    ON CONFLICT (sequence_name, scope_key)
    DO UPDATE SET current_value = 5, updated_at_utc = EXCLUDED.updated_at_utc;
END $$;
