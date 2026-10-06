# Kronant Trader today: what a trader can do and see (codebase inventory, 2026-10-06)

All sources are files in the repo D:/software-projects/propfirm-solution, cited as repo-relative paths (line numbers where useful). Read at commit 62369ed on main. "Built" means described as implemented in a spec AND confirmed in code; "planned" means named as future work in docs; "absent" means neither built nor found in code. No tests were run for these notes.

## 1. Terminal UI: which panels exist

### Takeaway
Kronant Trader is a single-page, dark-only web terminal with five fixed regions: account bar (top), watchlist (left), chart (center), order panel (right), bottom tabs (Positions, Orders, History, Events) and a status bar. It is polished in detail (plain-language events, toasts, right-click chart menu, risk summary) but has a fixed layout with no workspaces, no detachable panels and no multi-chart.

### Cited Findings
- Layout is fixed: account bar; watchlist | chart | order panel; bottom tabs Positions/Orders/History/Events; status bar - [docs/spec/handelsterminal.md L32-51](docs/spec/handelsterminal.md); grid `lg:grid-cols-[19rem_minmax(0,1fr)_18rem]` and bottom row `13rem` - [trading/terminal/src/components/Terminal.tsx L108-109](trading/terminal/src/components/Terminal.tsx)
- "Panels have fixed size on a desktop screen" - [docs/spec/handelsterminal.md L125](docs/spec/handelsterminal.md)
- Account bar: firm logo (or name), "Back to {firm}" link to the account in the firm portal, connection indicator named "Live prices", account selector, Balance, Equity, Free margin, Margin level, profit target, loss limits, initials menu - [docs/spec/handelsterminal.md L53-56](docs/spec/handelsterminal.md); fields in [trading/terminal/src/components/AccountBar.tsx L80-86](trading/terminal/src/components/AccountBar.tsx)
- Initials menu shows email, server, "Sound on fills" toggle and Log out - [docs/spec/handelsterminal.md L20](docs/spec/handelsterminal.md)
- Status bar: Kronant mark and "Kronant Trader", firm server, clock in the account's time zone with zone name, equity, margin, free margin, margin level - [docs/spec/handelsterminal.md L83](docs/spec/handelsterminal.md); [trading/terminal/src/components/StatusBar.tsx](trading/terminal/src/components/StatusBar.tsx)
- Watchlist: alphabetical, bid, ask, 24h change, 24h sparkline (only once the service has at least 2 hours of prices), green/red flash on bid/ask change, symbol search, filter All / category group / Favorites, favorites stored in browser localStorage; opens on EURUSD - [docs/spec/handelsterminal.md L57](docs/spec/handelsterminal.md); [trading/terminal/src/components/Watchlist.tsx](trading/terminal/src/components/Watchlist.tsx); [trading/terminal/src/lib/favorites.ts](trading/terminal/src/lib/favorites.ts)
- Symbol icons are two overlapping coloured "coins" with currency signs (no flags, no descriptive instrument names in terminal code) - [trading/terminal/src/components/SymbolIcon.tsx](trading/terminal/src/components/SymbolIcon.tsx); [trading/terminal/src/lib/instruments.ts L37-57](trading/terminal/src/lib/instruments.ts)
- Chart header: symbol, bid, 24h change, 24h high/low of bid, spread in points; narrow widths show only symbol, bid and % change - [docs/spec/handelsterminal.md L58](docs/spec/handelsterminal.md)
- Bottom tabs are exactly `["Positions", "Orders", "History", "Events"]` - [trading/terminal/src/components/BottomPanel.tsx L17](trading/terminal/src/components/BottomPanel.tsx)
- Positions columns: Symbol, Side, Volume, Open price, Current price, SL, TP, Margin, Unrealized P/L, plus Edit and Close buttons - [trading/terminal/src/components/BottomPanel.tsx L87-111](trading/terminal/src/components/BottomPanel.tsx)
- Orders columns: Symbol, Type, Side, Volume, Price, SL, TP, Placed, plus Cancel (no Edit) - [trading/terminal/src/components/BottomPanel.tsx L258-276](trading/terminal/src/components/BottomPanel.tsx)
- History columns: Closed, Symbol, Side, Volume, Open price, Close price, Reason, Commission, Result (net of full open+close commission; gross on hover); deposits/withdrawals such as payouts appear as rows; close reasons in plain words (Manual, Stop loss, Take profit, Stop out, Loss limit, Account closed) - [trading/terminal/src/components/BottomPanel.tsx L287-300](trading/terminal/src/components/BottomPanel.tsx); [docs/spec/handelsterminal.md L79](docs/spec/handelsterminal.md)
- Events tab: every account event as a plain sentence, warnings (rejections, floor breaches, stop out) in yellow - [docs/spec/handelsterminal.md L80](docs/spec/handelsterminal.md); [trading/terminal/src/lib/events.ts](trading/terminal/src/lib/events.ts)
- No id columns; the position/order id appears only on hover over the symbol - [docs/spec/handelsterminal.md L75](docs/spec/handelsterminal.md)
- Right-click menu on the chart (Reset chart, New order SL/TP at that price, Set/Move/Remove SL/TP per open position) - see section 2 - [docs/spec/handelsterminal.md L63-69](docs/spec/handelsterminal.md); [trading/terminal/src/components/PriceMenu.tsx](trading/terminal/src/components/PriceMenu.tsx)
- "Ended" notice: red box "Trading on this account has ended" with the reason, time, equity vs. limit and a button to the account in the portal - [docs/spec/handelsterminal.md L56](docs/spec/handelsterminal.md); [trading/terminal/src/components/EndedNotice.tsx](trading/terminal/src/components/EndedNotice.tsx)
- Explicitly left out of the design: "menus for pages that do not exist, drawing tools and indicators, position size calculator, market sentiment, export and ping" - [docs/spec/handelsterminal.md L84](docs/spec/handelsterminal.md)
- No grep hits for price alerts, browser notifications, service worker/manifest, news, sentiment, depth of market or order book in terminal source - searched [trading/terminal/src](trading/terminal/src)
- The internal UI/UX review called the terminal "the most finished part" (chart markers, SL in money, right-click menu, account bar with limits) - [reports/Genomgång av UI och UX.md L27](reports/Genomgång%20av%20UI%20och%20UX.md)

### Inferences
- Compared with TradeLocker/cTrader-style terminals, Kronant lacks layout customization (resizable/dockable panels, saved workspaces, multiple charts), a market-watch "symbol info" panel, alerts and news. Its strength is clarity of prop-specific information directly in the top bar.

### Gaps
- No screenshot-level verification was done in this research; the description relies on the spec plus code reading.

## 2. Charting

### Takeaway
The chart uses TradingView Lightweight Charts 5.2.1: bid candlesticks only, 7 timeframes (M1-D1), tick volume, fullscreen, trade arrows, position/SL/TP lines with drag-to-modify SL/TP, ghost lines for the order being typed, and a right-click menu. There are no indicators, no drawing tools, no other chart types, no pending-order lines and no scroll-back beyond 500 bars.

### Cited Findings
- Library: `lightweight-charts` 5.2.1 - [trading/terminal/package.json](trading/terminal/package.json); decision and trade-off ("drawing tools and indicators are missing until Advanced Charts is in place"; an application for Advanced Charts "is made in parallel") - [docs/adr/0007-graf-lightweight-charts.md](docs/adr/0007-graf-lightweight-charts.md)
- TradingView logo shown in the chart per license - [docs/spec/handelsterminal.md L118](docs/spec/handelsterminal.md)
- Series are `CandlestickSeries` plus `HistogramSeries` for volume; no other chart type code - [trading/terminal/src/components/PriceChart.tsx L4-7, L152-161](trading/terminal/src/components/PriceChart.tsx)
- Timeframes M1, M5, M15, M30, H1, H4, D1 - [trading/terminal/src/lib/candles.ts L13-23](trading/terminal/src/lib/candles.ts); default `useState<Timeframe>("M1")`, not persisted - [trading/terminal/src/components/PriceChart.tsx L112](trading/terminal/src/components/PriceChart.tsx)
- Candles are of bid; volume is tick volume (number of prices per candle), shown as a low muted band (13 % of height), toggle "Volume" saved per device - [docs/spec/handelsterminal.md L58-59](docs/spec/handelsterminal.md); [trading/terminal/src/lib/settings.ts](trading/terminal/src/lib/settings.ts)
- Each chart load requests `count: 500` bars, staleTime Infinity; no paging back - [trading/terminal/src/lib/queries.ts L155-167](trading/terminal/src/lib/queries.ts)
- History depth: M1 and M5 reach 2 days and today; M15 and longer 30 days (ADR 0048); Tiingo history has no tick volume - [docs/spec/handelsterminal.md L126](docs/spec/handelsterminal.md); [docs/adr/0048-grafernas-historik-per-prisflode.md](docs/adr/0048-grafernas-historik-per-prisflode.md)
- Candles start at whole UTC hours/days; daily candle follows the UTC day, not the trading day or 17:00 New York - [docs/spec/handelsterminal.md L131](docs/spec/handelsterminal.md); [docs/spec/handelstjanst.md L403](docs/spec/handelstjanst.md)
- Trade markers: arrow at entry candle pointing at open price, arrow at exit candle pointing at close price, grey dashed line between, coloured by side; built from `PositionOpened`/`PositionClosed` events - [docs/spec/handelsterminal.md L60](docs/spec/handelsterminal.md); [trading/terminal/src/components/TradeMarkers.ts](trading/terminal/src/components/TradeMarkers.ts)
- Position lines: open price (brass), SL and TP lines labelled with estimated P/L (e.g. "SL -100.00"); SL/TP are draggable with the mouse, sent on release via `PUT /positions/{id}/stops`, clamped at least one point from the closing price, Esc cancels, rejected moves snap back with reason; drag is mouse-only (touch pans the chart) - [docs/spec/handelsterminal.md L61](docs/spec/handelsterminal.md); [trading/terminal/src/components/positionLines.ts](trading/terminal/src/components/positionLines.ts)
- Only stop kinds are draggable (`kind: StopKind` in the drag state); the open-price line is not a drag handle to create SL/TP - [trading/terminal/src/components/positionLines.ts L37-40](trading/terminal/src/components/positionLines.ts)
- Ghost lines: SL, TP and the limit/stop price of the order being filled in are drawn as half-opaque dashed lines with labels like "Buy SL -100.00"; they are not draggable - [docs/spec/handelsterminal.md L62](docs/spec/handelsterminal.md); [trading/terminal/src/lib/orderDraft.ts](trading/terminal/src/lib/orderDraft.ts)
- Pending (placed) limit/stop orders are not drawn on the chart: line code covers open/stopLoss/takeProfit of positions and the draft ghosts only - [trading/terminal/src/components/positionLines.ts L21-35](trading/terminal/src/components/positionLines.ts); [trading/terminal/src/components/PriceChart.tsx L253-265](trading/terminal/src/components/PriceChart.tsx)
- Right-click menu: Reset chart (also Alt+R anywhere), New order with Stop loss / Take profit at clicked price, per-position "Set/Move stop loss/take profit" with estimated result, Remove SL/TP when clicking a stop line; keyboard navigable - [docs/spec/handelsterminal.md L63-69](docs/spec/handelsterminal.md); Alt+R handler [trading/terminal/src/components/PriceChart.tsx L469-477](trading/terminal/src/components/PriceChart.tsx)
- Fullscreen supported - [trading/terminal/src/components/PriceChart.tsx L280](trading/terminal/src/components/PriceChart.tsx); [docs/spec/handelsterminal.md L58](docs/spec/handelsterminal.md)
- "The chart lacks drawing tools and indicators" - [docs/spec/handelsterminal.md L130](docs/spec/handelsterminal.md)

### Inferences
- Drag-to-modify SL/TP, P/L labels on lines, ghost lines and trade arrows put Kronant roughly at parity with TradeLocker/cTrader on order visualization, but the lack of indicators/drawing tools and only 500 bars is a major gap for chart-centric traders; ADR 0007 already flags this as a competitive disadvantage.
- Placing orders by dragging/clicking on the chart (e.g. "buy limit here") is only partly possible: the right-click menu fills SL/TP prices but has no "Buy limit at X"/"Sell stop at X" entry in the spec.

### Gaps
- Status of the TradingView Advanced Charts application mentioned in ADR 0007 is not recorded anywhere found.

## 3. Orders and order management

### Takeaway
Order types are Market, Limit and Stop with optional SL/TP set as price or as money in account currency. Orders submit immediately on Buy/Sell (no confirmation). The order panel has a strong pre-trade summary (margin, pip value, risk as % of the nearest loss limit's room). Missing: stop-limit, trailing stops, partial close, close-all, reverse, pending-order modification, order expiry, OCO, SL/TP in pips, trading hotkeys.

### Cited Findings
- `const orderTypes: OrderType[] = ["Market", "Limit", "Stop"]` - [trading/terminal/src/components/OrderPanel.tsx L22](trading/terminal/src/components/OrderPanel.tsx); engine `PlaceOrder` = "market, limit or stop order with optional stop loss and take profit" - [docs/spec/handelsmotor.md L31](docs/spec/handelsmotor.md)
- Buy/Sell call `submit(side)` which validates and calls `placeOrder.mutate` directly; no confirmation dialog - [trading/terminal/src/components/OrderPanel.tsx L83-117, L294](trading/terminal/src/components/OrderPanel.tsx)
- Client-generated UUID order ids make retries idempotent (`409 DuplicateId`) - [trading/terminal/src/components/OrderPanel.tsx L100](trading/terminal/src/components/OrderPanel.tsx); [docs/adr/0006-api-mellan-terminal-och-tjanst.md](docs/adr/0006-api-mellan-terminal-och-tjanst.md)
- Volume steppers use the instrument step and limits; last valid volume remembered per symbol per device (so gold does not default to 1 lot ~240,000 USD); contract value in base currency shown under volume; price steppers move one pip - [docs/spec/handelsterminal.md L70](docs/spec/handelsterminal.md); [trading/terminal/src/lib/volumes.ts](trading/terminal/src/lib/volumes.ts)
- SL/TP entry as "Price" or money in account currency (e.g. "USD"); amount converted to a price from ask/bid/order price, distance rounded down to whole points; the other representation (prices or estimated amounts) previewed under SELL and BUY - [docs/spec/handelsterminal.md L70](docs/spec/handelsterminal.md); [trading/terminal/src/components/StopUnitToggle.tsx](trading/terminal/src/components/StopUnitToggle.tsx); [trading/terminal/src/lib/stops.ts](trading/terminal/src/lib/stops.ts)
- No pips/points input unit for SL/TP (toggle is price vs. money only) - [trading/terminal/src/components/StopUnitToggle.tsx](trading/terminal/src/components/StopUnitToggle.tsx)
- Pre-trade summary "What this order means": Margin (estimate; red if above free margin), Pip value at volume, Risk at stop loss in account currency and as share of the room to the nearest loss limit, e.g. "Risks 18.00 USD, 0.4% of today's room", with a bar yellow from 50 % and red at 100 %; prompts "Set a stop loss to see what the order risks." - [docs/spec/handelsterminal.md L71-74](docs/spec/handelsterminal.md); [trading/terminal/src/lib/orderSummary.ts](trading/terminal/src/lib/orderSummary.ts)
- Conditions shown in the panel for the selected symbol: leverage, spread markup (points), commission per lot and side, contract size - [trading/terminal/src/components/OrderPanel.tsx L324-333](trading/terminal/src/components/OrderPanel.tsx)
- After a successful order, price/SL/TP clear while volume, type and unit stay; a rejected order keeps all fields - [docs/spec/handelsterminal.md L70](docs/spec/handelsterminal.md)
- Position management: Edit (SL/TP, also as amounts from open price) and full Close per position; pending orders can only be cancelled - [trading/terminal/src/components/BottomPanel.tsx L109-111, L275](trading/terminal/src/components/BottomPanel.tsx); REST surface is `POST /orders`, `DELETE /orders/{id}`, `POST /positions/{id}/close`, `PUT /positions/{id}/stops` only - [docs/spec/handelstjanst.md L312-315](docs/spec/handelstjanst.md)
- Engine inputs contain no ModifyOrder, partial close or close-all - [docs/spec/handelsmotor.md L25-40](docs/spec/handelsmotor.md)
- Explicitly outside phase 1: "partial close of positions, netting, order validity time (all are good until cancelled) and minimum distance to price for stop loss" - [docs/spec/handelsmotor.md L184](docs/spec/handelsmotor.md)
- Hedging account mode: each fill is its own position; buy and sell in the same symbol allowed; netting "can be added later" - [docs/spec/handelsmotor.md L64-66](docs/spec/handelsmotor.md)
- Trailing exists only as an equity floor type for prop rules (`TrailingFloor`), not as a trailing stop order - [trading/terminal/src/lib/api/schema.ts L1972-1995](trading/terminal/src/lib/api/schema.ts); grep for "trailing/partial/close all/one-click" in terminal source found nothing else
- Keyboard: only Alt+R (reset chart), Esc (cancel drag/close menu) and arrow keys in the right-click menu; no trading hotkeys - [trading/terminal/src/components/PriceChart.tsx L469-477](trading/terminal/src/components/PriceChart.tsx); [trading/terminal/src/components/positionLines.ts L314-330](trading/terminal/src/components/positionLines.ts); [trading/terminal/src/components/PriceMenu.tsx L156](trading/terminal/src/components/PriceMenu.tsx)
- "Position size calculator" explicitly left out, although the order summary shows what a chosen volume and SL mean - [docs/spec/handelsterminal.md L84](docs/spec/handelsterminal.md)
- Input validated client-side (decimals, volume limits/steps) and again by the engine - [docs/spec/handelsterminal.md L104](docs/spec/handelsterminal.md)

### Inferences
- The panel is effectively "one-click" (no confirm step), but there is no one-click trading on the chart and no quick buy/sell buttons outside the order panel.
- The risk-as-%-of-daily-room readout is a prop-specific feature not typical of generic terminals; a "size from risk" calculator (enter risk, get lots) is the natural next step and is explicitly missing.
- Missing partial close/close-all/trailing stops are table-stakes features in TradeLocker and cTrader; they require engine changes (new inputs), not just UI.

### Gaps
- No evidence found about bracket/OCO semantics beyond SL/TP attached to an order.

## 4. Instruments, price feeds, spreads and symbol info

### Takeaway
23 instruments in 5 categories (10 FX pairs, gold, silver, 6 indices, 3 energies, BTC and ETH). Three feeds: Synthetic (default), Tiingo (FX/metals) and Capital.com (all categories), all development-only and not licensed for display to firms' traders. Per-firm conditions (leverage, spread markup, commission) are set by the firm and shown to the trader.

### Cited Findings
- Instruments: EURUSD, GBPUSD, USDJPY, AUDUSD, NZDUSD, USDCAD, USDCHF, EURGBP, EURJPY, GBPJPY (Forex); XAUUSD, XAGUSD (Metals); US100, US500, US30, DE40, UK100, JP225 (Indices); USOIL, UKOIL, NATGAS (Commodities); BTCUSD, ETHUSD (Crypto) - [trading/src/Trading.Service/appsettings.json L17-39](trading/src/Trading.Service/appsettings.json); [docs/adr/0049-capital-com-och-fler-instrument.md](docs/adr/0049-capital-com-och-fler-instrument.md)
- Contract sizes, digits and conditions for indices, commodities and crypto "are example values"; firms created before ADR 0049 keep groups without the new instruments - [docs/spec/handelstjanst.md L398](docs/spec/handelstjanst.md)
- Default group conditions: FX leverage 1:100, markup 2 points, commission 3.50 per lot per side; metals 1:30, markup 10 points; indices 1:20, no commission; energies 1:10; crypto 1:2 - [trading/src/Trading.Service/appsettings.json L47-69](trading/src/Trading.Service/appsettings.json)
- Firms change leverage, spread markup and commission per symbol in the portal (`/admin/trading`), applying immediately to all accounts - [docs/spec/portal.md L43](docs/spec/portal.md); `PUT /groups/{groupId}/symbols` - [docs/spec/handelstjanst.md L339](docs/spec/handelstjanst.md)
- Feeds: `SyntheticPriceFeed` (random walk, default), `TiingoPriceFeed` (FX and metals only), `CapitalComPriceFeed` (all categories, max 40 instruments per stream) - [docs/spec/handelstjanst.md L227-229](docs/spec/handelstjanst.md)
- "The Tiingo and Capital.com feeds may not be shown to others. A provider for production awaits licence terms, and Nasdaq-100 and other indices require a separate licence" - [docs/spec/handelstjanst.md L396](docs/spec/handelstjanst.md); [reports/Licens för prisdata i produktion.md](reports/Licens%20för%20prisdata%20i%20produktion.md)
- No trading hours/sessions: with real feeds no prices arrive when markets close, so orders are rejected `StalePrice` after `MaxQuoteAge` (5 s) - [docs/spec/handelstjanst.md L397](docs/spec/handelstjanst.md); `"MaxQuoteAge": "00:00:05"` [trading/src/Trading.Service/appsettings.json L15](trading/src/Trading.Service/appsettings.json)
- Watchlist groups by category in order Forex, Metals, Indices, Commodities, Crypto - [trading/terminal/src/lib/instruments.ts L7](trading/terminal/src/lib/instruments.ts)
- Spread shown in points in chart header; markup/commission/leverage/contract size shown in the order panel ("part of the openness towards traders") - [docs/spec/handelsterminal.md L58, L70](docs/spec/handelsterminal.md)
- Price push throttled to at most one price per symbol per 100 ms to the terminal - [docs/spec/handelsterminal.md L94, L128](docs/spec/handelsterminal.md)
- Product plan V1 was "forex and gold"; indices "only when data licences are solved" - [docs/produktplan-handelsplattform-propfirm.md L148-159](docs/produktplan-handelsplattform-propfirm.md)

### Inferences
- There is no symbol-specification dialog (trading hours, swap, margin requirement table, tick size) as in cTrader/TradeLocker; the order panel's conditions block is the only "symbol info".
- Instrument breadth in dev (23) is broader than V1 plans, but production breadth depends on unresolved data licensing; futures (TopstepX's domain) are not covered at all.

### Gaps
- No production price feed is chosen; licensing status is open per the product plan's open questions.

## 5. Execution model and transparency

### Takeaway
Execution is a deterministic, in-house simulation: fills at the group-marked-up bid/ask, gaps filled at the gapped price, no added slippage, commission both sides, stale-price guard, equity floors checked on every tick. Every input price and event is journaled in Postgres, so the audit data exists server-side, but the trader-facing "price history behind every fill" promised in the product plan is not built; only conditions and breach-proof prices are shown.

### Cited Findings
- Engine is deterministic ("same input always gives the same events"), never reads the system clock, bans floats - [docs/spec/handelsmotor.md L9, L166-169, L199](docs/spec/handelsmotor.md); [docs/adr/0005-deterministisk-handelsmotor.md](docs/adr/0005-deterministisk-handelsmotor.md)
- Markup: bid lowered by half the markup (rounded down to whole points), ask raised by the rest - [docs/spec/handelsmotor.md L76-78](docs/spec/handelsmotor.md)
- Fill table: market buy at ask, sell at bid; limit buy when ask <= limit, filled at ask; stop buy when ask >= stop, filled at ask "even if the price jumped past"; SL on a buy when bid <= SL, filled at bid, etc. - [docs/spec/handelsmotor.md L80-93](docs/spec/handelsmotor.md)
- "On price gaps limit orders and take profit fill at a better price, and stop orders and stop loss at a worse price... No extra slippage is added in phase 1" - [docs/spec/handelsmotor.md L95](docs/spec/handelsmotor.md)
- Commission per lot and side, charged at open and at close - [docs/spec/handelsmotor.md L157-159](docs/spec/handelsmotor.md)
- Stale price guard: market orders, pending orders, closes and modifications rejected `StalePrice` when the latest price is older than the configured limit - [docs/spec/handelsmotor.md L161-163](docs/spec/handelsmotor.md)
- Prices are timestamped on receipt by the service, not with provider time - [docs/spec/handelstjanst.md L245](docs/spec/handelstjanst.md)
- Margin = volume x contract size x rate to account currency / leverage; opposite positions do not reduce margin; order fills only if margin + commission fit in free margin; stop out closes the largest loser first - [docs/spec/handelsmotor.md L106-115](docs/spec/handelsmotor.md)
- Every input (each price with its feed name) and event is stored in `engine_inputs` / `engine_events` in Postgres; snapshots; replay on restart must reproduce identical events - [docs/spec/handelstjanst.md L250-277](docs/spec/handelstjanst.md); [docs/adr/0008-journal-av-indata.md](docs/adr/0008-journal-av-indata.md)
- Golden replay test of 3,000 synthetic EURUSD prices covering orders, SL/TP, floors, stop out - [docs/spec/handelsmotor.md L195](docs/spec/handelsmotor.md)
- Product plan promise: "Traders see the firm's settings for spread, fees and slippage, and the price history behind every fill" and technical requirement "Traders see ... spread, fees, swap and slippage, and can see the price history behind every fill" - [docs/produktplan-handelsplattform-propfirm.md L100, L169](docs/produktplan-handelsplattform-propfirm.md)
- Built transparency in the terminal: order panel conditions (leverage, markup, commission, contract size) - [trading/terminal/src/components/OrderPanel.tsx L324-333](trading/terminal/src/components/OrderPanel.tsx); breach events show the proof prices "Prices: SYMBOL bid/ask, ..." - [trading/terminal/src/components/BottomPanel.tsx L353-356](trading/terminal/src/components/BottomPanel.tsx); breach evidence (floor level, equity, prices, open positions) stored in `EquityFloorBreached` - [docs/spec/handelsmotor.md L55](docs/spec/handelsmotor.md)
- Licensing report: "save exactly which price lay behind every fill (the journal already does)" - [reports/Licens för prisdata i produktion.md L89](reports/Licens%20för%20prisdata%20i%20produktion.md)
- Not built: swap/rollover, slippage beyond gaps, wider spread at news/night, FX-rate freshness check - [docs/spec/handelsmotor.md L180-186](docs/spec/handelsmotor.md); product plan still lists "swap and slippage" and "wider spread at news and night" as requirements - [docs/produktplan-handelsplattform-propfirm.md L166](docs/produktplan-handelsplattform-propfirm.md)
- No trader-facing endpoint for raw ticks or per-fill price evidence: trader REST has only account, instruments, point-value, prices, candles, events and the four commands - [docs/spec/handelstjanst.md L300-315](docs/spec/handelstjanst.md)

### Inferences
- The "transparent / verifiable simulation" differentiator is half-built: the data (journal of every quote with feed name, deterministic replay) is in place, but there is no "fill receipt" view (quote at fill, raw vs. marked-up price, tick chart around the fill) for traders. Building it is mostly a read API plus UI, not engine work.
- Because there is no swap and no slippage model, there is nothing to disclose for those yet; the plan's wording overstates the current state.

### Gaps
- Whether `PositionOpened`/`PositionClosed` events carry the raw (pre-markup) quote is not confirmed; the spec says they carry "price, commission and balance after", which suggests the marked-up fill price only.

## 6. Prop rules in the terminal vs. the portal (drawdown, daily loss, target, payouts, dashboard)

### Takeaway
The terminal shows live prop status in the account bar: profit target with "to go", Daily loss limit and Max loss limit with level and equity headroom ("5,800.00 left", yellow <25 %, red <10 %, "Broken"), paused/ended states and risk as % of room in the order panel. Everything else (trading days, time limits, inactivity, consistency, payouts, statistics, certificates, balance chart, rules table) lives in the white-label portal, refreshed every 5 seconds.

### Cited Findings
- Terminal profit target: balance that passes the phase and remaining amount ("5,000.00 to go"); reached with open positions shows "Reached, close positions" - [docs/spec/handelsterminal.md L54](docs/spec/handelsterminal.md)
- Terminal loss limits named as in the portal: Daily loss limit, Max loss limit (others named from id); each with level and headroom from the engine (`headroom`); yellow below 25 %, red below 10 %; never negative, "Broken" instead - [docs/spec/handelsterminal.md L55](docs/spec/handelsterminal.md); [trading/terminal/src/lib/account.ts](trading/terminal/src/lib/account.ts)
- Ended account: red box with reason and time, e.g. daily loss limit broken at 17:38:12 with equity vs. level, "Every position was closed at the next price." - [docs/spec/handelsterminal.md L56](docs/spec/handelsterminal.md)
- Paused account: "Paused", no new orders, can still close and move SL/TP - [docs/spec/handelsterminal.md L78](docs/spec/handelsterminal.md)
- Account label, profit target, time zone and portal URL come from `GET /api/auth/me` (`accountDetails`) set by the prop platform via `PUT /accounts/{id}/details` - [docs/spec/handelstjanst.md L296, L334](docs/spec/handelstjanst.md); [docs/adr/0035-terminalen-visar-kontot-som-portalen.md](docs/adr/0035-terminalen-visar-kontot-som-portalen.md)
- Terminal UI has no trading-day count, consistency, time-limit or payout display (no matches in terminal components) - searched [trading/terminal/src/components](trading/terminal/src/components)
- Rules supported by the rule engine: profit target (on balance, all positions closed), minimum trading days, max daily loss (from balance or max(balance, equity) at day start), max total loss fixed or trailing locked at initial balance, optional phase time limit, inactivity days, funded profit split and optional consistency rule (best day share) - [docs/spec/regelmotor.md L24-44](docs/spec/regelmotor.md)
- Templates One-step, Two-step, Three-step, Instant funded - [docs/spec/regelmotor.md L46-55](docs/spec/regelmotor.md)
- Enforcement split: the trading engine checks equity floors on every price in the same step (no delay) and closes everything on breach; the rule engine sets/resets floors daily - [docs/spec/regelmotor.md L13-17](docs/spec/regelmotor.md); [docs/spec/handelsmotor.md L117-127](docs/spec/handelsmotor.md); [docs/adr/0011-regelmotorn-satter-golv.md](docs/adr/0011-regelmotorn-satter-golv.md)
- Not supported: news-trading rule, consistency on open positions, partial payouts, funded scaling - [docs/spec/regelmotor.md L174-180](docs/spec/regelmotor.md)
- Portal trader home `/`: "needs you" items (near a loss limit, payout available, <=5 days to trade/pass, paused, under review), big account card with phases, live equity with pulsing dot, progress to target, meters for Daily and Max loss limit "with the same words and colours as the terminal", Open terminal and Request payout - [docs/spec/portal.md L83-89](docs/spec/portal.md)
- Portal account page `/accounts/{id}`: milestone celebrations, "What happened" timeline for failed accounts with Try again, phases, figures with next steps ("Trade on 2 more days"), next payout, targets and limits with meters, payouts, certificates (PNG download, share to X/LinkedIn), per-stage history with balance chart, day-by-day, statistics, closed trades with "Show more" and CSV, and the rules table - [docs/spec/portal.md L91-104](docs/spec/portal.md)
- Portal figures refresh every 5 seconds; history refetched when it changes - [docs/spec/portal.md L62](docs/spec/portal.md); equity is not stored over time, so the chart shows balance steps plus current equity - [docs/spec/portal.md L147](docs/spec/portal.md); [docs/adr/0022-handelshistorik-for-traderns-oversikt.md](docs/adr/0022-handelshistorik-for-traderns-oversikt.md)
- `/payouts`: payout method (bank, crypto, other), ID check, payouts across accounts with progress, totals per currency - [docs/spec/portal.md L24](docs/spec/portal.md); payout = whole profit withdrawn immediately, account resets to start balance - [docs/spec/regelmotor.md L144-152](docs/spec/regelmotor.md); [docs/adr/0015-utbetalningar-tar-ut-vinsten-direkt.md](docs/adr/0015-utbetalningar-tar-ut-vinsten-direkt.md)
- Request payout requires confirmation in a dialog because the whole profit leaves the account - [docs/spec/portal.md L69](docs/spec/portal.md)
- One time zone: all terminal times in the account's trading-day zone, matching the portal - [docs/spec/handelsterminal.md L82](docs/spec/handelsterminal.md); [docs/adr/0038-en-tidszon-for-kontots-tider.md](docs/adr/0038-en-tidszon-for-kontots-tider.md)

### Inferences
- The live headroom to each limit, coloured by distance and computed by the engine, plus risk-as-%-of-room per order, is a genuine prop-native differentiator vs. generic terminals (TradeLocker/cTrader need prop add-ons; TopstepX shows similar live risk data natively).
- Trading-days progress, time limit and consistency status are not visible in the terminal; a trader must switch to the portal to see them.

### Gaps
- Not verified whether the terminal reflects the trailing max-loss floor's moving level live (spec says headroom comes from the engine, so likely yes, but no explicit test cited for trailing in the terminal).

## 7. Multi-account, mobile, i18n, themes, white-label, notifications, sound, reconnect, performance

### Takeaway
One account at a time with a dropdown switcher; a tabbed phone layout below 1024 px; English only; dark only; the terminal is Kronant-branded (not white-label) with the firm's logo/name in the account bar; toasts and an optional fill chime; robust SignalR reconnect with event catch-up. No alerts, no push notifications, no PWA/native app.

### Cited Findings
- Account switcher: `<select>` in the account bar, also `?account=`; label from portal e.g. "#1001 Two-step 100K · Phase 1" - [trading/terminal/src/components/AccountBar.tsx L78, L139-145](trading/terminal/src/components/AccountBar.tsx); [docs/spec/handelsterminal.md L19](docs/spec/handelsterminal.md)
- Login: server + email + password like MetaTrader/TradeLocker; firms with a `loginUrl` send traders through their portal with a one-time 2-minute link; expired sessions bounce back to the portal - [docs/spec/handelsterminal.md L14-17](docs/spec/handelsterminal.md); [docs/adr/0027-handelsvillkor-och-inloggning-genom-portalen.md](docs/adr/0027-handelsvillkor-och-inloggning-genom-portalen.md)
- No password change/reset in the terminal - [docs/spec/handelsterminal.md L129](docs/spec/handelsterminal.md)
- Mobile: below 1,024 px one part at a time via bottom tabs Chart, Trade, Watchlist, Positions; status bar hidden; positions table scrolls sideways - [docs/spec/handelsterminal.md L125](docs/spec/handelsterminal.md); [trading/terminal/src/components/Terminal.tsx L145-163](trading/terminal/src/components/Terminal.tsx)
- Chart SL/TP dragging is mouse-only; touch pans - [docs/spec/handelsterminal.md L61](docs/spec/handelsterminal.md)
- Native mobile app is "later" - [docs/produktplan-handelsplattform-propfirm.md L160](docs/produktplan-handelsplattform-propfirm.md); no manifest/service worker found in [trading/terminal](trading/terminal)
- i18n: `<html lang="en">`, number formats fixed `Intl.NumberFormat("en-US")`, dates `en-GB` - [trading/terminal/src/app/layout.tsx L25](trading/terminal/src/app/layout.tsx); [trading/terminal/src/lib/format.ts L3-5, L49](trading/terminal/src/lib/format.ts); portal notifications also English with fixed text - [docs/spec/portal.md L144](docs/spec/portal.md)
- Theme: Kronant graphite with brass accent from `@kronant/design`; no light theme or theme switch in terminal code - [docs/spec/handelsterminal.md L24](docs/spec/handelsterminal.md); [trading/terminal/src/app/globals.css](trading/terminal/src/app/globals.css); [docs/adr/0047-ett-gemensamt-designsystem.md](docs/adr/0047-ett-gemensamt-designsystem.md)
- Not white label: "The terminal is our brand and not white label (ADR 0009)"; firm shows as server on login and logo/name in account bar - [docs/spec/handelsterminal.md L13, L53](docs/spec/handelsterminal.md); [docs/adr/0009-inloggning-och-firmor.md L8, L24](docs/adr/0009-inloggning-och-firmor.md). The portal IS white label (logo, colours, theme dark/light/navy, custom domain) - [docs/spec/portal.md L41, L121-125](docs/spec/portal.md)
- Notifications: `sonner` toasts for fills, placed orders, closes (including SL/TP/stop out/loss-limit closes) with net result after commission; rejections as red toasts for 8 s in plain words ("Refused: not enough free margin"); events fetched at start/reconnect do not toast - [docs/spec/handelsterminal.md L76, L81](docs/spec/handelsterminal.md); [trading/terminal/src/lib/notices.ts](trading/terminal/src/lib/notices.ts)
- Sound: "Sound on fills", off by default, two Web Audio tones, saved per device - [docs/spec/handelsterminal.md L77](docs/spec/handelsterminal.md); [trading/terminal/src/lib/sound.ts](trading/terminal/src/lib/sound.ts)
- Reconnect: SignalR reconnects immediately then every 2 s; after reconnect refetches charts and 24h data and missed events via `GET /events?after=`; >1,000 missed events -> latest 1,000; terminal keeps the latest 1,000 events - [docs/spec/handelsterminal.md L106-107](docs/spec/handelsterminal.md); [trading/terminal/src/lib/eventSync.ts](trading/terminal/src/lib/eventSync.ts); [trading/terminal/src/lib/realtime.ts](trading/terminal/src/lib/realtime.ts)
- Performance: prices pushed at most every 100 ms, account every 250 ms; point value refetched on whole 10 s and 24h data on whole 5 min so components share fetches; React Query pauses when the window is inactive - [docs/spec/handelsterminal.md L88-100](docs/spec/handelsterminal.md)
- Known scale limitation: each watchlist row fetches its own 24h candles; "with many symbols and traders a combined summary from the service is needed" - [docs/spec/handelsterminal.md L127](docs/spec/handelsterminal.md)
- Engine evaluates all accounts with positions/orders on every price (no per-symbol index), "enough for small and medium firms" - [docs/spec/handelsmotor.md L186](docs/spec/handelsmotor.md)
- Accessibility: keyboard focus ring, reduced-motion respected, aria labels - [docs/spec/handelsterminal.md L28-29](docs/spec/handelsterminal.md)
- Ping/latency display explicitly left out - [docs/spec/handelsterminal.md L84](docs/spec/handelsterminal.md)

### Inferences
- No simultaneous multi-account view (e.g. trade copier across accounts, or several accounts' P/L at once) as some prop traders expect; switching reloads the page context.
- English-only and dark-only limit appeal in non-English markets; no settings page exists beyond two toggles (volume band, fill sound).

### Gaps
- No load test results exist; the pricing report notes server cost per user "is an estimate, not measured" - [reports/Konkurrenters priser och vårt pris.md L134](reports/Konkurrenters%20priser%20och%20vårt%20pris.md)

## 8. APIs for traders, copy trading, journal/statistics, CSV export

### Takeaway
There is no public trader API (no algo trading, no API keys, no FIX, no bots) and no copy trading. Statistics and CSV export exist only in the portal per challenge stage. The only external APIs are for firms (trading admin API, prop Firm API with webhooks).

### Cited Findings
- Trader endpoints exist (REST + SignalR at `/hubs/trading`) but use cookie sessions and serve the terminal; ADR 0006: "If a public API for algo trading is built, amounts may need to be sent as text" - [docs/spec/handelstjanst.md L300-370](docs/spec/handelstjanst.md); [docs/adr/0006-api-mellan-terminal-och-tjanst.md](docs/adr/0006-api-mellan-terminal-och-tjanst.md)
- "API for algo trading" and "Mobile app" are listed under "Later" for Kronant Trader - [docs/produktplan-handelsplattform-propfirm.md L157-162](docs/produktplan-handelsplattform-propfirm.md)
- Copy engine in plan is firm-side: "a copy engine that mirrors proven traders to a real account at a broker" (later), not trader social/copy trading - [docs/produktplan-handelsplattform-propfirm.md L162, L398](docs/produktplan-handelsplattform-propfirm.md)
- Firm-facing APIs: trading admin API `/api/admin/v1` with `X-Api-Key` (users, accounts, floors, login links, event stream) - [docs/spec/handelstjanst.md L317-339](docs/spec/handelstjanst.md); prop Firm API and webhooks (`account.passed`, `account.breached`, `payout.requested`, etc.) - [docs/spec/regelmotor.md L101-121](docs/spec/regelmotor.md); [docs/spec/portal.md L46](docs/spec/portal.md)
- Portal statistics per stage: Closed trades, Winning trades and win rate, Average win, Average loss, Best trade, Worst trade, Profit factor, Lots traded, Result, Commission - [prop/portal/src/components/AccountHistory.tsx L126-140](prop/portal/src/components/AccountHistory.tsx)
- Portal "Download CSV" of a stage's closed trades (`/api/portal/accounts/{id}/trades.csv?stage=`) - [prop/portal/src/components/AccountHistory.tsx L165-166](prop/portal/src/components/AccountHistory.tsx); [prop/portal/src/lib/queries.ts L214-216](prop/portal/src/lib/queries.ts)
- Day-by-day results and balance chart with floors and target in the portal - [docs/spec/portal.md L103, L112](docs/spec/portal.md)
- Terminal "export" explicitly left out - [docs/spec/handelsterminal.md L84](docs/spec/handelsterminal.md)
- No trade journal (notes, tags, screenshots) found in terminal or portal code; statistics are computed by the prop service from the event stream - [docs/adr/0022-handelshistorik-for-traderns-oversikt.md](docs/adr/0022-handelshistorik-for-traderns-oversikt.md)

### Inferences
- Lack of any algo/bot API is a clear gap vs. cTrader (cBots/Open API) and TradeLocker (REST API, TradingView integration); it also means no TradingView broker integration.
- Statistics are basic (no drawdown curve, no per-symbol breakdown, no equity curve because equity is not stored over time).

### Gaps
- No doc decides whether prop firms would allow algo trading via a future API; not addressed in found ADRs.

## 9. Explicitly left out, future work and production readiness

### Takeaway
Explicit exclusions are well documented: no indicators/drawing tools, no position size calculator, no sentiment/export/ping, no swap/slippage/news spreads, no partial close/netting/expiry, no trading hours, no algo API, no mobile app. The trading service refuses to start outside the Development environment, so Kronant Trader is not production-ready yet (pending HTTPS, secrets and a licensed price feed).

### Cited Findings
- Terminal exclusions: menus for non-existent pages, drawing tools and indicators, position size calculator, market sentiment, export, ping - [docs/spec/handelsterminal.md L84](docs/spec/handelsterminal.md)
- Terminal limitations: fixed panels; shorter history on low timeframes; per-row watchlist fetching; tick volume of latest candle may lag; no password change; no drawing tools/indicators; UTC candles; account name only refreshed at reload - [docs/spec/handelsterminal.md L123-132](docs/spec/handelsterminal.md)
- Engine "outside phase 1": swap and rollover; slippage beyond gaps and wider spread at news/night; partial close, netting, order expiry, minimum stop distance; FX-rate freshness check; per-symbol index - [docs/spec/handelsmotor.md L180-186](docs/spec/handelsmotor.md)
- Service limitations: only starts in Development; "Before production HTTPS, secret handling and a licensed price feed are needed"; no trading hours; journal grows without archiving; events to firms via stream not webhooks; trader cannot change/reset own password - [docs/spec/handelstjanst.md L393-404](docs/spec/handelstjanst.md)
- Code guard: `throw new InvalidOperationException("Trading.Service is not yet ready for production and may only run in the Development environment.")` - [trading/src/Trading.Service/Program.cs L180-182](trading/src/Trading.Service/Program.cs)
- Product plan "Later" for the trading platform: more asset classes (indices after licences), mobile app, algo API, copy engine to a real broker - [docs/produktplan-handelsplattform-propfirm.md L157-162](docs/produktplan-handelsplattform-propfirm.md)
- Product plan risk: "Traders often prefer MT5, cTrader or TradeLocker. Our platform must be really smooth." - [docs/produktplan-handelsplattform-propfirm.md L406](docs/produktplan-handelsplattform-propfirm.md)
- Prop rule engine gaps: no news rule, no scaling, whole-profit payouts only - [docs/spec/regelmotor.md L174-180](docs/spec/regelmotor.md); anti-fraud detection (multi-account, mirrored trades, latency arbitrage, news trading) is "later" - [docs/produktplan-handelsplattform-propfirm.md L214](docs/produktplan-handelsplattform-propfirm.md)
- UI/UX review terminal items 97-104 (volume bars, empty order panel space -> risk summary, hex id column, watchlist flash, toasts with sound, per-symbol volume, Kronant colours, net result) are all reflected as built in the current terminal spec - [reports/Genomgång av UI och UX.md L168-179](reports/Genomgång%20av%20UI%20och%20UX.md); [docs/spec/handelsterminal.md](docs/spec/handelsterminal.md); commit e55b29c "Redesign the portal, terminal and emails with a shared design system" is on main (git log)
- Earlier walkthrough terminal items (login via portal, firm listing, commission per trade, account naming, plain-word events, breach box) are also reflected as built - [reports/Genomgång som ny firma.md L26, L42, L48, L128-156](reports/Genomgång%20som%20ny%20firma.md); [docs/spec/handelsterminal.md L15, L55-56, L79-80](docs/spec/handelsterminal.md)
- Test coverage: Vitest unit tests for order summary, stops, notices, events etc., and Playwright e2e covering login, buy/close, SL drag, right-click menu, limits, account switching and isolation - [docs/spec/handelsterminal.md L134-138](docs/spec/handelsterminal.md); [trading/terminal/e2e/trading.spec.ts](trading/terminal/e2e/trading.spec.ts)

### Inferences
- Priority gap list vs. TradeLocker/cTrader/TopstepX, ranked by how often traders expect them: (1) indicators and drawing tools (TradingView Advanced Charts or equivalent); (2) partial close, close all, reverse, trailing stop, pending-order edit and chart-drawn pending orders; (3) trading hotkeys / one-click chart trading; (4) symbol info with trading hours; (5) trader API/TradingView integration; (6) i18n and light theme; (7) alerts and push notifications; (8) the promised per-fill price audit view.
- Things Kronant already does that are worth keeping as differentiators: engine-computed headroom to each prop limit live in the top bar, risk as % of today's room before each order, SL/TP in account currency with ghost lines, plain-language events and breach proof with prices, deterministic journaled engine, and portal-side certificates/milestones.

### Gaps
- Several ADRs that the specs describe as implemented still carry "Status: Föreslagen" (e.g. ADR 0006, 0009, 0022, 0027, 0035, 0047); status labels lag the code and were not used as evidence of what is built.
- No user research or trader feedback data exists in the repo to rank the gaps; the ranking above is an inference.
