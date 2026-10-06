-- The account's rules as the firm's system sees them now, for the terminal (ADR 0052): trading days, when the stage
-- must be passed by, when a new position must be opened by, and the consistency rule.
create table account_rules (
    account_id text primary key,
    rules jsonb not null,
    updated_at timestamptz not null
);

-- The trader's choices in the terminal, such as favorites and indicators, kept on their login so they follow them to
-- another device (ADR 0052). Each value is the JSON the terminal stored under the key.
create table user_settings (
    user_id uuid not null references users (id),
    key text not null,
    value jsonb not null,
    updated_at timestamptz not null,
    primary key (user_id, key)
);
