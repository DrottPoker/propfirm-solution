-- How far back a feed's loaded history reaches (ADR 0051). When the charts are set to reach further back, the history
-- is loaded again. Histories loaded before this column have none, so they are loaded again once.
alter table chart_histories add column reach timestamptz;
