-- Traders log in to the firm's portal once they have chosen a password through an invitation.
alter table traders add column password_hash text;

-- The firm's staff who use the admin panel. An email address is unique within a firm.
create table firm_admins (
    id uuid primary key,
    firm_id text not null,
    email text not null,
    normalized_email text not null,
    password_hash text not null,
    created_at timestamptz not null,
    constraint firm_admins_email_per_firm unique (firm_id, normalized_email)
);

-- One-time links that let a trader choose a password. Only a hash of the token is kept.
create table portal_invites (
    token_hash bytea primary key,
    trader_id uuid not null references traders (id),
    expires_at timestamptz not null,
    used_at timestamptz
);

create index portal_invites_by_expiry on portal_invites (expires_at);

-- Keys that protect the portal's login cookies, shared by every instance and kept across restarts.
create table data_protection_keys (
    id text primary key,
    xml text not null,
    created_at timestamptz not null default now()
);
