-- The currency of the accounts a firm chose when it signed up. The firm's trading server is created with it, and
-- trading_currency is what the trading platform made.
alter table firm_signups add column currency text not null default 'USD';
alter table firms add column account_currency text not null default 'USD';
