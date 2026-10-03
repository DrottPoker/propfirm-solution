-- Traders who can log in to the terminal. An email address is unique within a firm, not across firms.
create table users (
    id uuid primary key,
    tenant_id text not null,
    email text not null,
    normalized_email text not null,
    password_hash text not null,
    created_at timestamptz not null default now(),
    constraint users_email_per_tenant unique (tenant_id, normalized_email)
);

-- Which user owns each trading account.
create table account_owners (
    account_id text primary key,
    user_id uuid not null references users (id),
    created_at timestamptz not null default now()
);

create index account_owners_by_user on account_owners (user_id);

-- Keys that protect login cookies, shared by every instance and kept across restarts.
create table data_protection_keys (
    id text primary key,
    xml text not null,
    created_at timestamptz not null default now()
);
