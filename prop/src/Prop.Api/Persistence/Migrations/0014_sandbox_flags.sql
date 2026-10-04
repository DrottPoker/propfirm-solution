-- Whether the order or the account was made while the firm was in its sandbox. The firm's figures leave them out:
-- test purchases move no money, and test accounts end when the firm goes live.
alter table orders add column sandbox boolean not null default false;
alter table challenge_accounts add column sandbox boolean not null default false;

-- Those from before, of firms that signed up, made before the firm went live. Configured firms have no sandbox.
update orders o set sandbox = true
from firms f left join firm_billing b on b.firm_id = f.id
where o.firm_id = f.id and not f.configured and (b.activated_at is null or o.created_at < b.activated_at);

update challenge_accounts a set sandbox = true
from firms f left join firm_billing b on b.firm_id = f.id
where a.firm_id = f.id and not f.configured and (b.activated_at is null or a.created_at < b.activated_at);
