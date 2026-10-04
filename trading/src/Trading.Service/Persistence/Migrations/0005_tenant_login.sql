-- Where the firm's traders log in, for example its portal, which opens the terminal with a one-time link.
-- The terminal sends traders of the firm there instead of asking for a password. Empty for none.
alter table tenants add column login_url text;
