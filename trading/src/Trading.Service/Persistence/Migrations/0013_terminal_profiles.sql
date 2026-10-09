-- How each server's terminal works for its traders (ADR 0058): the kind of business, the parts shown, how orders and
-- tickets start, password login, the firm's own pages and a risk warning. Null until the firm sets one, which gives the
-- default for its kind.
alter table tenants add column terminal_profile jsonb;

-- The trader's name as the firm knows it, for the terminal's initials and menu. Null when the firm has not told it.
alter table users add column name text;
