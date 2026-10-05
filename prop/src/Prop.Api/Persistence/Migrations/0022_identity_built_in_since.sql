-- Since when the firm has had our built-in ID checks on (ADR 0042). A month costs the monthly price when the checks were
-- on all of it, or used in it, so turning them off before the month is charged does not make the checks free.
alter table firm_identity_settings add column built_in_since timestamptz;

update firm_identity_settings set built_in_since = updated_at where mode = 'BuiltIn';
