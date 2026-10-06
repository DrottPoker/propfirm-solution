-- When each of the firm's administrators last logged in to its admin panel, which its Team page shows. Empty for one who
-- has not logged in since this arrived. Traders' logins are not kept.
alter table firm_admins add column last_login_at timestamptz;
