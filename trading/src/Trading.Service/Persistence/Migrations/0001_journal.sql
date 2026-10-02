-- Every input to the engine, in order. Prices have their own columns since they are most of the rows.
create table engine_inputs (
    sequence bigint primary key,
    recorded_at timestamptz not null,
    kind text not null,
    symbol text,
    bid numeric,
    ask numeric,
    payload jsonb,
    constraint engine_inputs_quote_or_payload check (
        (kind = 'Quote' and symbol is not null and bid is not null and ask is not null and payload is null)
        or (kind <> 'Quote' and payload is not null)
    )
);

create index engine_inputs_quotes_by_time on engine_inputs (recorded_at) where kind = 'Quote';

-- Every event the engine produced, for history and audit.
create table engine_events (
    sequence bigint primary key,
    account_id text,
    kind text not null,
    occurred_at timestamptz not null,
    payload jsonb not null
);

create index engine_events_by_account on engine_events (account_id, sequence);

-- Engine state after an input, so a restart only replays the inputs after it.
create table engine_snapshots (
    input_sequence bigint primary key,
    event_sequence bigint not null,
    configuration_fingerprint text not null,
    created_at timestamptz not null default now(),
    state jsonb not null
);
