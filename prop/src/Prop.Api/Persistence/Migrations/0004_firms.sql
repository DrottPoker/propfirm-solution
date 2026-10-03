-- The firms on the prop platform. Configured firms are saved here at startup; the others sign up.
create table firms (
    -- The short name: the firm's subdomain and its server on the trading platform.
    id text primary key,
    name text not null,
    status text not null,
    -- Configured firms are saved from the configuration at every start. The others signed up.
    configured boolean not null,
    -- SHA-256 of the key the firm's own systems use for the firm API, once the firm has made one.
    api_key_sha256 bytea,
    -- The firm's server on the trading platform, the encrypted key to it, the group new accounts are opened
    -- in and the group's currency. Empty until the server exists.
    trading_server text,
    trading_api_key text,
    trading_group text,
    trading_currency text,
    -- Where the firm receives webhooks, and the encrypted secret that signs them.
    webhook_url text,
    webhook_secret text,
    portal_url text not null,
    logo_url text,
    colors jsonb not null,
    -- The version of the terms and the data processing agreement the firm accepted when it signed up, and when.
    terms_version text,
    terms_accepted_at timestamptz,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

create unique index firms_by_api_key on firms (api_key_sha256) where api_key_sha256 is not null;

-- The host names each firm's portal is reached on. A host belongs to one firm.
create table firm_hosts (
    host text primary key,
    firm_id text not null references firms (id)
);

create index firm_hosts_by_firm on firm_hosts (firm_id);

-- Firms that signed up and wait for the email address to be confirmed. Only a hash of the link's token is kept.
create table firm_signups (
    token_hash bytea primary key,
    firm_id text not null,
    firm_name text not null,
    email text not null,
    normalized_email text not null,
    password_hash text not null,
    terms_version text not null,
    created_at timestamptz not null,
    expires_at timestamptz not null,
    used_at timestamptz
);

create index firm_signups_by_firm on firm_signups (firm_id);
create index firm_signups_by_email on firm_signups (normalized_email);

-- Invitations for the firm's administrators. Only a hash of the token is kept.
create table admin_invites (
    token_hash bytea primary key,
    firm_id text not null references firms (id),
    email text not null,
    normalized_email text not null,
    created_at timestamptz not null,
    expires_at timestamptz not null,
    used_at timestamptz
);

create index admin_invites_by_firm on admin_invites (firm_id, normalized_email);

-- One-time links that log an administrator in on the firm's portal, for example right after the firm signed up.
create table admin_login_links (
    token_hash bytea primary key,
    admin_id uuid not null references firm_admins (id) on delete cascade,
    expires_at timestamptz not null,
    used_at timestamptz
);

create index admin_login_links_by_expiry on admin_login_links (expires_at);
