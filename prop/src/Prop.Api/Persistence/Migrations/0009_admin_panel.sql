-- The admin panel's overview, account search and activity (ADR 0023). They read the firm's newest accounts,
-- orders and payouts by time.
create index challenge_accounts_by_firm_created on challenge_accounts (firm_id, created_at);
create index challenge_accounts_by_firm_updated on challenge_accounts (firm_id, updated_at);
create index orders_by_firm_paid on orders (firm_id, paid_at) where paid_at is not null;
create index payouts_by_firm_paid on payouts (firm_id, paid_at) where paid_at is not null;

-- The logo a firm uploaded for its portal, served from the portal's own address. Its SHA-256 is part of that
-- address, so a new logo gets a new address and the old one can be cached for good.
create table firm_logos (
    firm_id text primary key references firms (id),
    content_type text not null,
    content bytea not null,
    sha256 bytea not null,
    updated_at timestamptz not null
);
