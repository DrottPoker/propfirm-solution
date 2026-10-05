-- Who opened a ticket: the trader, or the firm writing to one of its traders (ADR 0041). Only tickets the trader opened
-- count toward the trader's limit of tickets that are not closed.
alter table support_tickets add column opened_by text not null default 'Trader';
