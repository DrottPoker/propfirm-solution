-- Firms no longer check their traders' IDs by hand (ADR 0042). A firm that did has not chosen a service, and chooses
-- our built-in checks or its own before it goes live. Ticking "ID checked" on a trader's card stays, as an exception.
delete from firm_identity_settings where mode = 'Manual';

-- A firm's own service must work through the whole flow before the firm goes live: a trader started the check in the
-- portal after the service's address was last set, and the service reported the outcome through the firm API.
alter table firm_identity_settings add column external_since timestamptz;
alter table firm_identity_settings add column external_tested_at timestamptz;

update firm_identity_settings set external_since = updated_at where mode = 'External';
