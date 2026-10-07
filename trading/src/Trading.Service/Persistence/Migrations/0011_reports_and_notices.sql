-- The input each event came from, so a receipt finds the price behind a fill (ADR 0053). Empty for events stored
-- before.
alter table engine_events add column input_sequence bigint;

-- A position's events for its receipt: the order it came from, its opening, its changes and its closes.
create index engine_events_by_position on engine_events (account_id, (coalesce(payload ->> 'positionId', payload ->> 'orderId')))
    where account_id is not null;

-- A firm's events since a time, for what an outage did to its accounts.
create index engine_events_by_group_time on engine_events (group_id, occurred_at) where group_id is not null;

-- A symbol's prices around a time, for the price behind a fill and the equity before a broken loss limit.
create index engine_inputs_quotes_by_symbol on engine_inputs (symbol, recorded_at) where kind = 'Quote';

-- What the firm's terminals show at the top, such as an outage and what the firm does about it (ADR 0053).
create table tenant_notices (
    tenant_id text primary key references tenants (id),
    notice jsonb not null,
    updated_at timestamptz not null
);
