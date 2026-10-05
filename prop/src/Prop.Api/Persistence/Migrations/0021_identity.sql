-- How a firm checks its traders' IDs (ADR 0042). A firm without a row checks by hand, as before.
create table firm_identity_settings (
    firm_id text primary key references firms (id),
    -- Manual, BuiltIn (our checks through Didit) or External (the firm's own service).
    mode text not null,
    -- FirstPayout or Funding: what waits until the trader is verified.
    required_before text not null,
    -- Extra checks with the built-in ones.
    check_address boolean not null default false,
    check_sanctions boolean not null default false,
    -- The firm's own page where its traders are checked, with {traderId} and {email} filled in.
    external_url text,
    updated_at timestamptz not null,
    updated_by text not null
);

-- Each check started with a provider. Only the outcome is kept here: the document and the pictures stay with the provider.
create table identity_sessions (
    id uuid primary key,
    firm_id text not null references firms (id),
    trader_id uuid not null references traders (id),
    -- Didit or Test.
    provider text not null,
    provider_session_id text not null,
    url text not null,
    check_address boolean not null,
    check_sanctions boolean not null,
    -- Pending, InReview, Approved, Declined or Expired.
    status text not null,
    created_at timestamptz not null,
    -- When the provider last told us about it, or we last asked.
    checked_at timestamptz,
    decided_at timestamptz,
    -- When the trader sent the check in, which is when the provider charges for it.
    submitted_at timestamptz,
    -- The monthly charge that billed the check to the firm.
    billed_charge_id uuid,
    constraint identity_sessions_provider_session unique (provider, provider_session_id)
);

create index identity_sessions_unbilled on identity_sessions (firm_id) where submitted_at is not null and billed_charge_id is null;

-- Where each trader's ID check is, from the firm's provider: the latest check's outcome, and what the document said.
create table trader_identity (
    trader_id uuid primary key references traders (id),
    firm_id text not null references firms (id),
    -- Didit, Test or External.
    provider text not null,
    -- The latest session, for checks through us.
    session_id uuid references identity_sessions (id),
    status text not null,
    full_name text,
    date_of_birth date,
    country text,
    address_checked boolean not null default false,
    sanctions_checked boolean not null default false,
    reason text,
    decided_at timestamptz,
    updated_at timestamptz not null
);
