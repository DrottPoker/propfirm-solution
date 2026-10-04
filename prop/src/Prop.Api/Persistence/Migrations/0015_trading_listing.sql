-- What the trading platform last heard about the firm's server: whether it is listed for traders, and where its traders
-- log in, which is the portal's terminal page. Kept so it is told again only when it changes (ADR 0027).
alter table firms add column trading_listing jsonb;
