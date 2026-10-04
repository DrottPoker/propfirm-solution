-- How each trader wants to be paid (ADR 0026), as encrypted JSON, since it is personal and leads to money.
create table trader_payout_methods (
    trader_id uuid primary key references traders (id),
    details text not null,
    updated_at timestamptz not null
);

-- The trader's payout method when the payout was asked for, encrypted the same way, so a later change does not move a
-- payout that is on its way. Empty for payouts from before, or asked for by the firm's own systems without one.
alter table payouts add column payout_details text;
