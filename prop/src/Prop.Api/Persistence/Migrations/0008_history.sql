-- The trading history behind the trader's dashboard (ADR 0022), read from each firm's event stream with its own
-- cursor. Reading the stream again from the start rebuilds it.
create table trading_history_cursors (
    firm_id text primary key,
    after_sequence bigint not null
);

-- Every change to the balance of a trading account the service opened: the opening balance, commissions, closed
-- positions, and deposits or withdrawals such as a payout's.
create table trading_balance_changes (
    account_id text not null references trading_accounts (account_id),
    sequence bigint not null,
    time timestamptz not null,
    -- Created, Opened, Closed or Adjusted.
    kind text not null,
    change numeric not null,
    balance_after numeric not null,
    primary key (account_id, sequence)
);

-- Positions on those accounts, from when they open until they close. Commissions are charged on both.
create table trading_positions (
    account_id text not null references trading_accounts (account_id),
    position_id text not null,
    symbol text not null,
    side text not null,
    volume numeric not null,
    open_price numeric not null,
    opened_at timestamptz,
    open_commission numeric not null default 0,
    close_price numeric,
    closed_at timestamptz,
    close_commission numeric,
    -- Before commissions.
    profit numeric,
    close_reason text,
    close_sequence bigint,
    primary key (account_id, position_id)
);

create index trading_positions_closed on trading_positions (account_id, close_sequence) where close_sequence is not null;

-- The levels the trading platform set for the floors, for example the daily loss limit at the start of each day.
create table trading_floor_levels (
    account_id text not null references trading_accounts (account_id),
    sequence bigint not null,
    time timestamptz not null,
    floor_id text not null,
    level numeric not null,
    primary key (account_id, sequence)
);

-- When each stage started and how it was passed, saved with the rule engine's decision.
alter table trading_accounts
    add column started_at timestamptz,
    add column passed_at timestamptz,
    add column passed_balance numeric,
    add column passed_trading_days int;

-- The rule engine's decision that ended the challenge: a breached floor, time running out or a cancellation.
alter table challenge_accounts add column ending jsonb;

-- Accounts from before get the same from their steps.
update trading_accounts ta
set started_at = (o ->> 'time')::timestamptz
from challenge_steps s, jsonb_array_elements(s.outputs) o
where s.challenge_account_id = ta.challenge_account_id
  and o ->> 'kind' = 'StageStarted'
  and o ->> 'accountId' = ta.account_id;

update trading_accounts ta
set passed_at = (o ->> 'time')::timestamptz,
    passed_balance = (o ->> 'balance')::numeric,
    passed_trading_days = (o ->> 'tradingDays')::int
from challenge_steps s, jsonb_array_elements(s.outputs) o
where s.challenge_account_id = ta.challenge_account_id
  and o ->> 'kind' = 'StagePassed'
  and o ->> 'accountId' = ta.account_id;

update challenge_accounts a
set ending = (
    select o
    from challenge_steps s, jsonb_array_elements(s.outputs) o
    where s.challenge_account_id = a.id and o ->> 'kind' in ('ChallengeFailed', 'ChallengeExpired', 'ChallengeCancelled')
    order by s.step desc
    limit 1)
where a.status in ('Failed', 'Cancelled');
