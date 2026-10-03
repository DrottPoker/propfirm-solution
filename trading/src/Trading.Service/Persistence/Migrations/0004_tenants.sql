-- The firms on the platform. Configured firms are saved here at startup; partners create the others.
create table tenants (
    -- The server traders log in to.
    id text primary key,
    name text not null,
    admin_api_key_sha256 bytea not null,
    -- The partner that created the firm, or null for a configured firm.
    partner_id text,
    -- Whether the server is on the public list traders choose from.
    listed boolean not null,
    created_at timestamptz not null
);

-- The trading groups each firm's accounts may be in. A group belongs to one firm.
create table tenant_groups (
    group_id text primary key,
    tenant_id text not null references tenants (id)
);

create index tenant_groups_by_tenant on tenant_groups (tenant_id);
