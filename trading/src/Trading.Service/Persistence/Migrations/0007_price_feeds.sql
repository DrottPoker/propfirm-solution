-- The feed each price came from, so a real feed's charts never show made-up prices (ADR 0048). Prices recorded before
-- this have none, since their feed is unknown.
alter table engine_inputs add column feed text;
alter table engine_inputs add constraint engine_inputs_feed_only_on_quotes check (kind = 'Quote' or feed is null);
