-- Whether the challenge is paused while its firm's month is unpaid, so paused challenges are found quickly.
alter table challenge_accounts add column paused boolean not null default false;

create index challenge_accounts_open_by_firm on challenge_accounts (firm_id) where status not in ('Failed', 'Cancelled');

-- How a firm pays us for its slots: the number of challenges it can have open at once (ADR 0020).
create table firm_billing (
    firm_id text primary key references firms (id),
    -- Paid: the firm pays by card in advance. Complimentary: slots without charges, for configured firms.
    plan text not null,
    -- Complimentary: the slots, or null for no limit. Paid: the slots charged for from the next unpaid month.
    slots integer,
    -- Paid: how many slots are bought at once when the last free one is taken. Null for no automatic expansion.
    auto_expand_step integer,
    -- Paid: Test or Stripe, and the saved card at the provider.
    provider text,
    customer_id text,
    payment_method_id text,
    card_brand text,
    card_last4 text,
    card_exp_month integer,
    card_exp_year integer,
    -- When the slots warning was emailed, so it goes out once until usage falls again.
    slots_warned_at timestamptz,
    -- When the current month began unpaid and the firm's challenges were paused.
    unpaid_since timestamptz,
    -- When the first payment was made and the firm went live.
    activated_at timestamptz,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

-- The months a paid firm has paid for, with its slots in them. A month without a row is unpaid.
create table billing_periods (
    firm_id text not null references firms (id),
    -- The first day of the month, in UTC.
    month date not null,
    slots integer not null,
    paid_at timestamptz not null,
    primary key (firm_id, month)
);

-- What a firm is charged: the first payment that takes it live, each month in advance, and slots bought during a month.
create table billing_charges (
    id uuid primary key,
    firm_id text not null references firms (id),
    -- Shown to the firm, unique across the platform.
    number bigint not null unique,
    -- Activation, Renewal or Slots.
    kind text not null,
    -- Pending, Paid, Failed or Void.
    status text not null,
    -- The first month the charge pays for, how many months from it (1 or 2), and the slots the firm has in them once it is paid.
    month date not null,
    months integer not null,
    slots integer not null,
    -- What is charged for: description, quantity and amount of each line.
    lines jsonb not null,
    amount numeric not null,
    currency text not null,
    provider text not null,
    -- The provider's id of the payment, for example a Stripe PaymentIntent.
    payment_reference text,
    failure text,
    attempts integer not null default 0,
    -- When the saved card is charged next. Null when the charge waits for the firm to pay on a checkout page.
    next_attempt_at timestamptz,
    created_at timestamptz not null,
    paid_at timestamptz,
    failed_at timestamptz
);

create index billing_charges_by_firm on billing_charges (firm_id, created_at);
create index billing_charges_due on billing_charges (next_attempt_at) where status = 'Pending' and next_attempt_at is not null;
create index billing_charges_by_payment on billing_charges (payment_reference) where payment_reference is not null;
create sequence billing_charge_numbers start 1001;

-- Pages where the firm pays a charge or saves a card at the provider, for example a Stripe Checkout Session.
create table billing_checkouts (
    id text primary key,
    firm_id text not null references firms (id),
    provider text not null,
    -- Payment or Card.
    purpose text not null,
    charge_id uuid references billing_charges (id),
    url text not null,
    -- Open, Completed or Expired.
    status text not null,
    created_at timestamptz not null,
    expires_at timestamptz not null,
    completed_at timestamptz
);

create index billing_checkouts_open on billing_checkouts (expires_at) where status = 'Open';

-- Everything that happened to a firm's billing, with the provider's message as evidence. Rows are only added.
create table billing_events (
    id bigserial primary key,
    firm_id text not null references firms (id),
    charge_id uuid references billing_charges (id),
    type text not null,
    recorded_at timestamptz not null,
    -- Who said so: the provider, an administrator or the platform itself.
    source text not null,
    detail jsonb
);

create index billing_events_by_firm on billing_events (firm_id, id);
