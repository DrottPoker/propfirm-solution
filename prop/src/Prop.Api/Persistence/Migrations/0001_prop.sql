-- The challenges each firm sells. Accounts keep a copy of the definition they were bought with.
create table challenge_definitions (
    firm_id text not null,
    id text not null,
    definition jsonb not null,
    updated_at timestamptz not null,
    primary key (firm_id, id)
);

-- The firm's traders. An email address is unique within a firm, not across firms.
create table traders (
    id uuid primary key,
    firm_id text not null,
    email text not null,
    normalized_email text not null,
    -- The trader's user on the trading platform, once created.
    trading_user_id uuid,
    created_at timestamptz not null,
    constraint traders_email_per_firm unique (firm_id, normalized_email)
);

-- A trader's way through a challenge, from the first stage to the funded account.
create table challenge_accounts (
    id uuid primary key,
    firm_id text not null,
    -- Shown to traders and part of the trading account ids.
    number bigint not null,
    trader_id uuid not null references traders (id),
    definition_id text not null,
    -- The firm's own id, for example its order number. Repeating it returns the same account.
    reference text,
    status text not null,
    stage int not null,
    -- When the account's trading days start, from its definition, so they can be found without reading the state.
    day_time_zone text not null,
    day_start time not null,
    current_day date,
    state jsonb not null,
    steps int not null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    constraint challenge_accounts_number unique (firm_id, number),
    constraint challenge_accounts_reference unique (firm_id, reference)
);

create index challenge_accounts_open on challenge_accounts (firm_id) where status not in ('Failed', 'Cancelled');
create index challenge_accounts_by_trader on challenge_accounts (trader_id);

-- Every input to a challenge account and what the rule engine decided, in order: the audit trail and the evidence.
create table challenge_steps (
    challenge_account_id uuid not null references challenge_accounts (id),
    step int not null,
    recorded_at timestamptz not null,
    input jsonb not null,
    outputs jsonb not null,
    -- The trading platform's event behind the input, if any, for example a breach with its prices and positions.
    source_event jsonb,
    primary key (challenge_account_id, step)
);

-- The trading accounts opened for challenge accounts, with what the trading platform last reported.
create table trading_accounts (
    account_id text primary key,
    challenge_account_id uuid not null references challenge_accounts (id),
    stage int not null,
    created boolean not null default false,
    balance numeric,
    open_positions int not null default 0,
    daily_floor numeric,
    max_loss_floor numeric,
    disabled boolean not null default false
);

create index trading_accounts_by_challenge on trading_accounts (challenge_account_id);

-- How far each firm's event stream from the trading platform has been read.
create table trading_cursors (
    firm_id text primary key,
    after_sequence bigint not null
);

-- Commands for the trading platform, run in order per firm.
create table trading_commands (
    id bigserial primary key,
    firm_id text not null,
    challenge_account_id uuid not null references challenge_accounts (id),
    command jsonb not null,
    created_at timestamptz not null,
    attempts int not null default 0,
    last_error text,
    done_at timestamptz,
    failed_at timestamptz
);

create index trading_commands_pending on trading_commands (firm_id, id) where done_at is null and failed_at is null;

-- Webhooks to the firm's systems, retried until they are delivered or given up.
create table webhook_deliveries (
    id uuid primary key,
    firm_id text not null,
    event_type text not null,
    payload jsonb not null,
    created_at timestamptz not null,
    attempts int not null default 0,
    next_attempt_at timestamptz not null,
    last_status int,
    last_error text,
    delivered_at timestamptz,
    failed_at timestamptz
);

create index webhook_deliveries_due on webhook_deliveries (next_attempt_at) where delivered_at is null and failed_at is null;

-- The next account number per firm.
create table firm_counters (
    firm_id text primary key,
    next_number bigint not null
);
