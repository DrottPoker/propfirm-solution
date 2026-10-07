-- The days new orders were locked on a trading account until the next trading day, by the trader's own daily loss
-- limit or profit target, or by the trader (ADR 0054). Read from the event stream like the rest of the history.
create table trading_locks (
    account_id text not null references trading_accounts (account_id),
    sequence bigint not null,
    time timestamptz not null,
    -- Trader, DailyLoss or DailyTarget.
    reason text not null,
    until timestamptz not null,
    -- The amount of the own limit that was reached, null when the trader locked the day.
    limit_amount numeric,
    -- Equity at the lock less the balance the trading day started with.
    day_result numeric not null,
    positions_closed int not null,
    primary key (account_id, sequence)
);

-- Every open account is described again, so the trading platform learns when its trading day starts.
update challenge_accounts set described_account_id = null;
