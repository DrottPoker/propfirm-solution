-- Funded traders' payouts, as the rule engine decided them. The steps of the challenge account are the
-- audit trail; this table is for finding payouts, for example those waiting for the firm.
create table payouts (
    id uuid primary key,
    firm_id text not null,
    challenge_account_id uuid not null references challenge_accounts (id),
    -- The trading account the profit was withdrawn from.
    trading_account_id text not null,
    status text not null,
    -- The whole profit withdrawn from the trading account, and the trader's share of it.
    profit numeric not null,
    profit_split_percent numeric not null,
    amount numeric not null,
    currency text not null,
    requested_at timestamptz not null,
    withdrawn_at timestamptz,
    approved_at timestamptz,
    paid_at timestamptz,
    rejected_at timestamptz,
    failed_at timestamptz,
    -- Why the firm rejected the payout or the trading platform refused the withdrawal.
    reason text,
    -- The firm's own reference for the payment, for example a bank transfer id.
    reference text
);

create index payouts_by_firm on payouts (firm_id, requested_at);
create index payouts_by_account on payouts (challenge_account_id, requested_at);
