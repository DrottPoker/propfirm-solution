-- Whether the firm's shop shows what the firm paid out to traders in the last 30 days and how soon. The firm turns it on
-- itself, since it makes its own figures public. Off until then.
alter table firms add column shop_shows_payouts boolean not null default false;
