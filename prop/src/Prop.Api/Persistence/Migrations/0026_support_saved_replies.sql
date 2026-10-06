-- Answers a firm's administrators save, to start an answer in a support ticket from instead of writing it anew (ADR
-- 0041). They belong to the firm, so all its administrators share them.
create table support_saved_replies (
    id uuid primary key,
    firm_id text not null references firms (id),
    -- What the list in the admin panel shows, on one line.
    title text not null,
    -- The text, with {trader} and {firm} where the portal fills in the trader's and the firm's name.
    body text not null,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

-- Two replies with the same title, in any case, would look the same in the list.
create unique index support_saved_replies_title on support_saved_replies (firm_id, lower(title));
