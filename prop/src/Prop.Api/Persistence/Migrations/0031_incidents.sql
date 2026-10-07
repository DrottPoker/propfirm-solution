-- Outages of the trading platform and what was done about them (ADR 0053). Our staff write them, or the platform finds a
-- price feed that stopped by itself and our staff publish it. Published incidents show on the firms' status pages and in
-- their terminals.
create table incidents (
    id uuid primary key,
    -- PriceFeedOutage, PlatformDown, SlowPrices or Other.
    kind text not null,
    title text not null,
    -- What traders and firms read.
    public_text text not null,
    -- For our staff only.
    internal_note text not null default '',
    started_at timestamptz not null,
    ended_at timestamptz,
    -- Draft until published, then Open and finally Resolved.
    status text not null,
    published_at timestamptz,
    -- The firms it concerns, or null for every firm.
    firms text[],
    -- Found by the platform, rather than written by our staff.
    detected boolean not null,
    -- The staff member who wrote or published it, or null when it was found.
    created_by text,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    -- A draft our staff dismissed, such as a false alarm. Kept, so a gap the platform found is not found again.
    dismissed_at timestamptz
);

create index incidents_by_start on incidents (started_at desc);

-- What was said about an incident as it went on, oldest first.
create table incident_updates (
    id bigserial primary key,
    incident_id uuid not null references incidents (id) on delete cascade,
    posted_at timestamptz not null,
    status text not null,
    text text not null,
    posted_by text
);

create index incident_updates_by_incident on incident_updates (incident_id, id);

-- A firm's own words about an incident, for its traders.
create table incident_firm_notes (
    incident_id uuid not null references incidents (id) on delete cascade,
    firm_id text not null references firms (id),
    text text not null,
    updated_at timestamptz not null,
    updated_by text not null,
    primary key (incident_id, firm_id)
);

-- What a firm did for an account after an incident: reinstated it or credited it. Rows are only added.
create table incident_decisions (
    id uuid primary key,
    incident_id uuid not null references incidents (id),
    firm_id text not null references firms (id),
    challenge_account_id uuid not null references challenge_accounts (id),
    -- Reinstated or Credited.
    kind text not null,
    amount numeric not null,
    reason text not null,
    decided_by text not null,
    decided_at timestamptz not null
);

create index incident_decisions_by_incident on incident_decisions (incident_id, firm_id, decided_at);

-- A firm's notice for its terminals is a command for the firm, not for one of its accounts.
alter table trading_commands alter column challenge_account_id drop not null;
