-- Each firm's charges count on their own from now on, and a paid charge gets an invoice number in a series of the
-- firm's own, without gaps (ADR 0032). Numbers from before stay as they are.
alter table billing_charges drop constraint billing_charges_number_key;
alter table billing_charges add constraint billing_charges_number_per_firm unique (firm_id, number);
drop sequence billing_charge_numbers;

-- The VAT of each charge, as it applied when the charge was made, and who it was to, for its invoice. amount is
-- net_amount and vat_amount together, which is what is paid. Charges from before had no VAT recorded.
alter table billing_charges
    add column net_amount numeric,
    add column vat_treatment text not null default 'NotRecorded',
    add column vat_percent numeric not null default 0,
    add column vat_amount numeric not null default 0,
    add column customer jsonb,
    add column invoice_number bigint;
update billing_charges set net_amount = amount;
alter table billing_charges
    alter column net_amount set not null,
    alter column vat_treatment drop default,
    alter column vat_percent drop default,
    alter column vat_amount drop default;

update billing_charges c set invoice_number = n.number
from (select id, row_number() over (partition by firm_id order by paid_at, number) as number from billing_charges where status = 'Paid') n
where c.id = n.id;
alter table billing_charges add constraint billing_charges_invoice_per_firm unique (firm_id, invoice_number);

-- The next charge and invoice number of each firm. The row is locked while a number is taken, so two never get the same.
create table billing_counters (
    firm_id text primary key references firms (id),
    next_charge_number bigint not null,
    next_invoice_number bigint not null
);

insert into billing_counters (firm_id, next_charge_number, next_invoice_number)
select firm_id, max(number) + 1, count(*) filter (where status = 'Paid') + 1 from billing_charges group by firm_id;

-- Where replies to the emails to the firm's traders go.
alter table firms add column support_email text;

-- The buyer's name and country, asked for in the shop, and kept on the trader.
alter table orders add column buyer_name text, add column buyer_country text;
alter table traders add column name text, add column country text;

-- When the trader showed the email is theirs, by opening a link from it. A trader who chose a password with an
-- invitation or a reset link did, so those from before count from then.
alter table traders add column email_confirmed_at timestamptz;
update traders set email_confirmed_at = coalesce(password_changed_at, created_at) where password_hash is not null;

-- Emails to traders are sent in the firm's look too, and replies go to the firm.
alter table email_outbox add column html_body text, add column reply_to text;
