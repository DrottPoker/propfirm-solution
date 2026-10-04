-- What the terminal shows about an account, set by the firm's systems: a name for the trader, the balance that passes
-- its target, the time zone of its trading day, and where the trader sees more about it, for example the firm's portal.
create table account_details (
    account_id text primary key,
    label text,
    profit_target numeric,
    time_zone text,
    details_url text,
    updated_at timestamptz not null
);

-- The firm's logo, shown with its name in the terminal.
alter table tenants add column logo_url text;
