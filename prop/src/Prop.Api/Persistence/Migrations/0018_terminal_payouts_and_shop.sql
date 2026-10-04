-- The trading account the trading platform was last told how to show (ADR 0035). Accounts opened before are told once
-- at startup.
alter table challenge_accounts add column described_account_id text;

-- A rejected payout whose profit went back on the trader's account, when the firm chose so.
alter table payouts add column profit_returned boolean not null default false;

-- The firm's discount codes for its shop. A code takes a percentage or an amount off; an amount only off prices in its
-- currency. A code for retries is only for buyers with an account at the firm that failed.
create table discount_codes (
    id uuid primary key,
    firm_id text not null references firms (id),
    code text not null,
    normalized_code text not null,
    percent_off numeric,
    amount_off numeric,
    currency text,
    -- Null means every challenge.
    challenge_ids text[],
    max_uses int,
    expires_at timestamptz,
    for_retries boolean not null default false,
    active boolean not null default true,
    created_at timestamptz not null,
    constraint discount_codes_code unique (firm_id, normalized_code),
    constraint discount_codes_one_kind check ((percent_off is null) <> (amount_off is null)),
    constraint discount_codes_amount_currency check ((amount_off is null) = (currency is null))
);

-- The code an order used, as typed, and the price before it. The order's amount is what the buyer pays.
alter table orders add column discount_code_id uuid references discount_codes (id);
alter table orders add column discount_code text;
alter table orders add column list_amount numeric;
create index orders_by_discount_code on orders (discount_code_id) where discount_code_id is not null;

-- The firm's own checks of a trader before funding or a payout, such as that it has seen their ID, until a verification
-- provider does them. Who ticked it is the administrator's email.
create table trader_checks (
    trader_id uuid not null references traders (id),
    item text not null,
    checked_at timestamptz not null,
    checked_by text not null,
    primary key (trader_id, item)
);

-- A firm's own domain for its portal, proved with a TXT record holding the token. Once its records are right it is one
-- of the firm's hosts and the portal's address; until then the portal stays on its address with us.
create table firm_domains (
    firm_id text primary key references firms (id),
    domain text not null unique,
    token text not null,
    status text not null,
    created_at timestamptz not null,
    checked_at timestamptz,
    active_at timestamptz,
    problem text
);
