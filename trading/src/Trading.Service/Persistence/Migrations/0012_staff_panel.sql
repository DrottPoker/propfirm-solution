-- Our own staff, who run the platform in its staff panel (ADR 0057). Not a firm's administrators.
create table staff_users (
    id uuid primary key,
    email text not null,
    normalized_email text not null unique,
    password_hash text not null,
    created_at timestamptz not null
);

-- What happened on the platform, for the staff panel: servers made, keys replaced, listings, starts, gaps and silent
-- prices. Only added to.
create table platform_log (
    id bigint generated always as identity primary key,
    at timestamptz not null,
    kind text not null,
    server_id text,
    staff_email text,
    detail jsonb not null
);

create index platform_log_by_time on platform_log (at desc, id desc);
create index platform_log_by_server on platform_log (server_id, at desc, id desc) where server_id is not null;

-- The staff member who made the server, or null for a configured server and one a partner made.
alter table tenants add column created_by text;

-- The charts' gaps and whether they were filled (ADR 0056), kept so the staff panel shows them after a restart.
create table chart_gaps (
    id uuid primary key,
    feed text not null,
    from_time timestamptz not null,
    until_time timestamptz not null,
    found_at timestamptz not null,
    state text not null,
    tries integer not null,
    finished_at timestamptz,
    bars integer not null,
    problem text
);

create index chart_gaps_by_feed on chart_gaps (feed, found_at desc);
