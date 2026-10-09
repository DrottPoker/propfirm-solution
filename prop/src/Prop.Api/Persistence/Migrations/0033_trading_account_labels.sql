-- The name the trading platform was last told for each trading account, such as "#1001 Two-step 100K, Phase 1". Null
-- for accounts named before names were kept, and for earlier stages' accounts that were never named, so every one is
-- named again once with the current names (ADR 0058).
alter table trading_accounts add column described_label text;
