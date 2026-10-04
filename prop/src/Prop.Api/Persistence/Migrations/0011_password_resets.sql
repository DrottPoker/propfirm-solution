-- One-time links that let someone choose a new password: a firm's trader or administrator, or one of our staff.
-- Only a hash of the token is kept. A new link replaces the person's older unused ones.
create table password_resets (
    token_hash bytea primary key,
    -- trader, admin or staff, and the id in its table.
    kind text not null,
    user_id uuid not null,
    created_at timestamptz not null,
    expires_at timestamptz not null,
    used_at timestamptz
);

create index password_resets_by_user on password_resets (kind, user_id);
create index password_resets_by_expiry on password_resets (expires_at);

-- When the password was last chosen. Sessions keep a stamp of the password itself, so a new one logs out the old ones.
alter table traders add column password_changed_at timestamptz;
alter table firm_admins add column password_changed_at timestamptz;
alter table staff_users add column password_changed_at timestamptz;
