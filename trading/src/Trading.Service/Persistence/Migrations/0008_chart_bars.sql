-- The charts' bars of bid per feed (ADR 0048): minute bars made from the feed's live prices, and the feed's history in
-- the bars it came in. Derived data, since the journal keeps every price. Bars of a feed's history have no ticks.
create table chart_bars (
    feed text not null,
    time timestamptz not null,
    symbol text not null,
    resolution text not null,
    open numeric not null,
    high numeric not null,
    low numeric not null,
    close numeric not null,
    ticks integer not null,
    primary key (feed, time, symbol, resolution)
);

-- Feeds whose history is loaded since the service last switched to them.
create table chart_histories (
    feed text primary key,
    loaded_at timestamptz not null
);
