-- Parts of positions closed before the rest (ADR 0051), one row per event, so reading an event again changes nothing.
-- A closed trade adds its parts to its last close: their volume, profit and commission, and an average close price.
create table trading_partial_closes (
    account_id text not null references trading_accounts (account_id),
    sequence bigint not null,
    position_id text not null,
    time timestamptz not null,
    volume numeric not null,
    close_price numeric not null,
    -- Before commission.
    profit numeric not null,
    commission numeric not null,
    primary key (account_id, sequence)
);

create index trading_partial_closes_position on trading_partial_closes (account_id, position_id);

-- The volume the last close closed. Less than the opened volume when parts were closed before. Null for closes recorded
-- before parts could be closed, which closed the whole volume.
alter table trading_positions add column close_volume numeric;
