-- The trading group of each event's account, so a firm's systems read only the firm's own events.
alter table engine_events add column group_id text;

update engine_events e
set group_id = created.payload ->> 'groupId'
from engine_events created
where created.kind = 'AccountCreated' and created.account_id = e.account_id;

create index engine_events_by_group on engine_events (group_id, sequence) where group_id is not null;

-- One-time links that log a trader in to the terminal, created by the firm's systems. Only a hash of the token is kept.
create table login_links (
    token_hash bytea primary key,
    user_id uuid not null references users (id),
    account_id text,
    expires_at timestamptz not null,
    used_at timestamptz
);

create index login_links_by_expiry on login_links (expires_at);
