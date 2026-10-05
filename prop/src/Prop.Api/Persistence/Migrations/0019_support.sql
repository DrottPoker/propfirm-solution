-- Traders' questions to their firm, each a conversation in the portal (ADR 0041).
create table support_tickets (
    id uuid primary key,
    firm_id text not null references firms (id),
    -- Shown to the trader and the firm, counted per firm from 1.
    number bigint not null,
    trader_id uuid not null references traders (id),
    -- The account the question is about, when the trader chose one.
    account_id uuid references challenge_accounts (id),
    subject text not null,
    -- Open (waits for the firm), Answered (waits for the trader) or Closed.
    status text not null,
    created_at timestamptz not null,
    -- When the last message was written, or the ticket was closed. Lists show the latest first.
    updated_at timestamptz not null,
    -- Since when an open ticket has waited for the firm. The firm's queue shows the oldest first.
    waiting_since timestamptz,
    -- When the firm last answered. The trader has an unread answer while it is after trader_read_at.
    answered_at timestamptz,
    trader_read_at timestamptz,
    closed_at timestamptz,
    -- trader, or the email of the administrator who closed it.
    closed_by text,
    -- How many messages the ticket has, which numbers the next one.
    messages int not null default 0,
    constraint support_tickets_number unique (firm_id, number)
);

create index support_tickets_by_firm_status on support_tickets (firm_id, status, waiting_since);
create index support_tickets_by_firm_updated on support_tickets (firm_id, updated_at);
create index support_tickets_by_trader on support_tickets (trader_id, updated_at);

-- The next ticket number per firm.
create table support_ticket_counters (
    firm_id text primary key,
    next_number bigint not null
);

-- The messages of a ticket, in the order they were written.
create table support_messages (
    id uuid primary key,
    ticket_id uuid not null references support_tickets (id),
    position int not null,
    -- Trader or Firm.
    author text not null,
    -- The administrator who wrote for the firm. Traders only see the firm's name.
    admin_email text,
    body text not null,
    created_at timestamptz not null,
    constraint support_messages_position unique (ticket_id, position)
);

-- Files added to a message: PDF, PNG or JPEG, known from their content, and encrypted with AES-GCM like the firms'
-- documents, since a screenshot or a document can be personal.
create table support_attachments (
    id uuid primary key,
    message_id uuid not null references support_messages (id),
    ticket_id uuid not null references support_tickets (id),
    -- The order of the files in the message.
    position int not null,
    file_name text not null,
    content_type text not null,
    size integer not null,
    sha256 bytea not null,
    content bytea not null,
    created_at timestamptz not null
);

create index support_attachments_by_ticket on support_attachments (ticket_id);
