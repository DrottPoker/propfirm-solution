-- When the firm's administrators last used its admin panel (ADR 0045). A sandbox nobody uses for a while is warned, and
-- then closes: its test accounts end and none start until an administrator comes back, which opens it again.
alter table firms add column active_at timestamptz;
alter table firms add column idle_warned_at timestamptz;
alter table firms add column sandbox_closed_at timestamptz;

-- Firms from before count from now, so none closes on the day this arrives.
update firms set active_at = now();
