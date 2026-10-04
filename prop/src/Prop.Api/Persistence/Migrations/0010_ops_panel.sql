-- Our admin view across the firms (ADR 0024): what firms pay us, what waits for us, and our checks during a review.

-- Charges by when they were paid, for what firms paid us in a period.
create index billing_charges_by_paid on billing_charges (paid_at) where status = 'Paid';

-- Charges whose card was declined and that are not paid yet.
create index billing_charges_declined on billing_charges (firm_id) where status in ('Pending', 'Failed') and failure is not null;

-- Payouts traders wait for, across the firms.
create index payouts_waiting on payouts (requested_at) where status in ('Pending', 'Approved');

-- What happened in reviews and with suspensions and charges, across the firms, newest first.
create index firm_events_by_time on firm_events (recorded_at);
create index billing_events_by_time on billing_events (recorded_at) where type in ('paid', 'declined');

-- The checks our staff tick while they review a firm, and who ticked each and when. Unticking removes the row.
create table firm_review_checks (
    firm_id text not null references firms (id),
    -- One of the review's checks, for example vat or owners.
    item text not null,
    done_by text not null,
    done_at timestamptz not null,
    primary key (firm_id, item)
);
