-- What the trading platform was last told about each signed-up firm's terminal, and the name it shows for each trader (ADR 0058).
alter table firms add column trading_terminal jsonb;
alter table traders add column trading_name text;
