-- The rules last told to the terminal for the challenge's trading account (ADR 0052), so they are told again only when
-- they change.
alter table challenge_accounts add column described_rules jsonb;
