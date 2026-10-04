-- Emails waiting to be sent, tried again until the mail server takes them or they are given up. Most are queued in
-- the same transaction as what they tell about, so an email goes out if and only if that change is saved.
create table email_outbox (
    id uuid primary key,
    -- The firm the email is about, if any.
    firm_id text,
    -- What the email is, for example payout.requested or password_reset.
    kind text not null,
    to_address text not null,
    -- The sender's name, for example the firm's for emails to its traders. Empty for the platform's own name.
    from_name text,
    subject text not null,
    body text not null,
    -- Makes an email that can be queued more than once, such as a reminder, go out once.
    dedupe_key text unique,
    created_at timestamptz not null,
    attempts int not null default 0,
    next_attempt_at timestamptz not null,
    last_error text,
    sent_at timestamptz,
    failed_at timestamptz
);

create index email_outbox_due on email_outbox (next_attempt_at) where sent_at is null and failed_at is null;

-- Which of the emails about its traders the firm wants, by kind (ADR 0025). A kind that is missing is on.
alter table firms add column email_settings jsonb not null default '{}';
