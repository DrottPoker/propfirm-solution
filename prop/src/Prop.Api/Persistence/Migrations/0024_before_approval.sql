-- Until we approve a firm, its emails go only to its administrators (ADR 0043). One to anyone else is kept with the time
-- it was withheld, so it can be seen what was not sent, but it is never sent.
alter table email_outbox add column withheld_at timestamptz;

drop index email_outbox_due;
create index email_outbox_due on email_outbox (next_attempt_at) where sent_at is null and failed_at is null and withheld_at is null;

-- The invitations to a firm's team are counted until we approve it, so those taken back or sent again are kept until
-- they are cleaned up, a month after they expired.
create index admin_invites_by_firm_created on admin_invites (firm_id, created_at);
