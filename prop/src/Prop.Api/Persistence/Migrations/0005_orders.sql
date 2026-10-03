-- How the firm's portal takes payment for challenges (ADR 0019). No provider means no shop in the portal.
alter table firms add column payment_provider text;
-- The firm's own Stripe keys, encrypted like the other secrets.
alter table firms add column stripe_secret_key text;
alter table firms add column stripe_webhook_secret text;
-- The firm's own checkout page, for its own payment provider.
alter table firms add column checkout_url text;
-- The firm's terms for buyers, which they accept before paying. Empty for none.
alter table firms add column shop_terms_url text;

-- What the firm sells its challenges for in the portal. Kept apart from the challenge, which is only rules.
create table challenge_prices (
    firm_id text not null,
    challenge_id text not null,
    amount numeric not null,
    currency text not null,
    for_sale boolean not null,
    updated_at timestamptz not null,
    primary key (firm_id, challenge_id),
    foreign key (firm_id, challenge_id) references challenge_definitions (firm_id, id)
);

-- Purchases of challenges in the firm's portal. A paid order starts exactly one account.
create table orders (
    id uuid primary key,
    firm_id text not null,
    -- Shown to buyers and the firm.
    number bigint not null,
    email text not null,
    challenge_id text not null,
    -- The price when the order was made, which the payment must match.
    amount numeric not null,
    currency text not null,
    provider text not null,
    -- Pending, Paid or Expired. A pending order past expires_at counts as expired.
    status text not null,
    -- SHA-256 of the token in the buyer's link to the order. The token itself is never stored.
    access_token_sha256 bytea not null,
    -- The provider's checkout, for example a Stripe Checkout Session, and where the buyer pays.
    checkout_id text,
    checkout_url text not null,
    -- The provider's id of the payment, for example a Stripe PaymentIntent, or the firm's own reference.
    payment_reference text,
    challenge_account_id uuid references challenge_accounts (id),
    -- Why a paid order has no account, for example a sandbox without room.
    problem text,
    created_at timestamptz not null,
    expires_at timestamptz not null,
    paid_at timestamptz,
    refunded_at timestamptz,
    disputed_at timestamptz,
    -- When the platform last emailed the buyer an invitation to the portal.
    invite_sent_at timestamptz,
    constraint orders_number unique (firm_id, number)
);

create index orders_by_firm on orders (firm_id, created_at);
create unique index orders_by_checkout on orders (provider, checkout_id) where checkout_id is not null;
create index orders_by_payment on orders (firm_id, payment_reference) where payment_reference is not null;

-- Everything that happened to an order, with the provider's message as evidence.
create table order_events (
    id bigserial primary key,
    order_id uuid not null references orders (id),
    type text not null,
    recorded_at timestamptz not null,
    -- Who said so: the provider, the firm's API, an administrator or the buyer.
    source text not null,
    detail jsonb
);

create index order_events_by_order on order_events (order_id, id);

-- The next order number per firm.
create table order_counters (
    firm_id text primary key,
    next_number bigint not null
);
