-- When we suspended the firm and why. Its challenges are paused and none can start while it is suspended (ADR 0021).
alter table firms add column suspended_at timestamptz;
alter table firms add column suspension_reason text;

-- Our review of a firm that signed up, before it may go live (ADR 0021).
create table firm_reviews (
    firm_id text primary key references firms (id),
    -- Draft, Submitted, ChangesRequested, Approved or Rejected.
    status text not null,
    -- The firm's application: its company, owners and links.
    application jsonb not null,
    -- Our latest message to the firm: the changes we need, or why it was not approved.
    message text,
    submitted_at timestamptz,
    decided_at timestamptz,
    -- The email of the staff member who decided last.
    decided_by text,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

create index firm_reviews_by_status on firm_reviews (status, submitted_at);

-- Documents the firm added to its application, encrypted with AES-GCM. Only small files, so they are kept here.
create table firm_documents (
    id uuid primary key,
    firm_id text not null references firms (id),
    file_name text not null,
    -- application/pdf, image/png or image/jpeg, from the file's content.
    content_type text not null,
    size integer not null,
    sha256 bytea not null,
    content bytea not null,
    uploaded_by text not null,
    uploaded_at timestamptz not null
);

create index firm_documents_by_firm on firm_documents (firm_id, uploaded_at);

-- Everything that happened in the firm's review and its suspensions, with who did it. Rows are only added.
create table firm_events (
    id bigserial primary key,
    firm_id text not null references firms (id),
    type text not null,
    recorded_at timestamptz not null,
    -- An administrator's or a staff member's email, or platform.
    actor text not null,
    detail jsonb
);

create index firm_events_by_firm on firm_events (firm_id, id);

-- Our own staff, who review firms in our admin view. Not the firms' administrators.
create table staff_users (
    id uuid primary key,
    email text not null,
    normalized_email text not null unique,
    password_hash text not null,
    created_at timestamptz not null
);
