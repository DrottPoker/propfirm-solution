# TopstepX (Topstep's in-house futures platform, built on ProjectX) - state as of 2025-2026

Research date: 2026-10-06. Primary sources are Topstep's own help center (help.topstep.com, help.topstepbrokerage.com), the ProjectX feature board/changelog and Finance Magnates. Many "review" sites in this niche (proptradingvibes, h2tfunding, pickmytrade, traderspost, fortraders, phidiaspropfirm) are affiliate or competitor content; they are flagged where used. Reddit threads could not be retrieved directly (search engine did not index them; see Gaps).

## 1. What features does TopstepX offer?

### Takeaway
TopstepX is a browser-only, futures-only (CME/CBOT/NYMEX/COMEX) terminal with TradingView-based charts, a single DOM, simple order types (market/limit/stop/trailing stop + brackets), a strong set of self-imposed risk tools (personal daily loss/profit limits, trailing PDLL, contract and trade limits, symbol block, lockout, "trade clock", daily risk lock), a built-in trade copier, a performance dashboard with journal, a $29/month API, and no native mobile app and no market replay. Its risk/discipline toolset is its standout; its charting and order-type depth is shallow compared with NinjaTrader.

### Cited Findings
**Access and architecture**
- Web-based at topstepx.com/trade, launched from the Topstep Dashboard; credentials emailed at first purchase - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- "TopstepX pulls data straight from the exchange(s) - no third-party feed. Data servers sit in Northern Virginia." User-selectable data refresh speed: Slow 750 ms, Medium 250 ms (default), Fast 50 ms - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- "Chart lag does not affect trading and orders - order execution runs server-side, not through your browser." - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- Only one concurrent session across all devices ("Exchange regulations limit your access to one concurrent user session"); logging in on a new device disconnects the previous one; multiple tabs/windows on the same browser allowed - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- Auto contract rollover: every product auto-updates to front month during the 4-5 PM CT close window - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- 51 permitted futures contracts across CME, CBOT, NYMEX, COMEX (affiliate site, checked Aug 2026) - [proptradingvibes](https://proptradingvibes.com/blog/topstep-trading-platforms)

**Charting**
- Up to 8 charts at once; 16 chart types incl. Heikin Ashi, Renko, Point & Figure; Range Bars in beta; timeframes 1m-1M; history up to 1 year; TradingView drawing tools; news events auto-plotted on chart; countdown to bar close - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- Symbol/component linking via color-coded link icons; "Chart Executions" (entry/exit markers) disabled by default; position value shown in $, ticks, % or points; ETH/RTH toggle - [Topstep Brokerage Help: TopstepX platform guide](https://help.topstepbrokerage.com/en/articles/15368476-topstepx-platform-guide)
- "External platform integration and custom indicators unsupported" (i.e. no user-imported/Pine scripts) - [Topstep Brokerage Help: platform guide](https://help.topstepbrokerage.com/en/articles/15368476-topstepx-platform-guide)
- Instead, a curated built-in indicator library: daily levels (Yesterday High/Low, VWAP), opening range, Trend Magic, SSL Hybrid, T3, VIDYA, Wave Trend, TDI, Zero Lag MACD, Squeeze Momentum, VWAP bands, CVD, Fair Value Gap and other "Smart Money Concepts" tools - [Topstep Help: TopstepX indicators](https://intercom.help/topstep-llc/en/articles/8524249-topstepx-faqs)
- Price alerts (separate article) and Sound/Visual alerts - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)

**DOM and orders**
- Order types: Market, Limit, Stop Market, OCO, Trailing Stop - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx). The API supports market, limit, stop, trailing stop, no stop-limit - [TradersPost on ProjectX](https://blog.traderspost.io/article/what-is-projectx-prop-firms)
- DOM: left-click bid/ask = limit, right-click = stop, drag orders to move; active position shown in blue with live P&L; volume profile in DOM - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- Only one DOM window, cannot be moved to a secondary screen; full depth requires a paid Level II subscription - [Topstep Brokerage Help: platform guide](https://help.topstepbrokerage.com/en/articles/15368476-topstepx-platform-guide)
- Position Brackets (account-level auto SL/TP, min 4 ticks from entry, drag P&L bars on chart to adjust) and Auto-OCO Brackets (each entry gets its own TP/SL; multiple TP/SL levels) - [Topstep Brokerage Help: platform guide](https://help.topstepbrokerage.com/en/articles/15368476-topstepx-platform-guide); [ProjectX changelog](https://s2f.featurebase.app/changelog)
- Flatten All (closes positions and cancels working orders on the selected account) on Order Ticket and DOM; mobile has a one-tap "Breakeven" order that appears when unrealized P&L is positive - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- Hotkeys must be configured manually (none by default), desktop only; optional order confirmations; optional "Confirm Account Switching" - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- Position Grid: clicking a position switches the focused chart to that market - [Topstep Brokerage blog, 2026-04-24](https://topstepbrokerage.com/blog/topstepx-futures-trading-platform-funded-traders)

**Risk / discipline tools (self-imposed, on top of firm rules)**
- Personal Daily Loss Limit (PDLL) and Personal Daily Profit Target (PDPT), set in Settings -> Risk Settings, with actions "Liquidate Only" or "Liquidate & Block"; Trailing PDLL (TPDLL) that trails the highest reached balance - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx). Brokerage version also offers "Do Nothing" - [Topstep Brokerage Help](https://help.topstepbrokerage.com/en/articles/15368476-topstepx-platform-guide)
- Daily Risk Lock: "Lock your risk settings for the day. Covers PDLL, PDPT, and Trade Limits"; cannot be changed until 5 PM CT next trading day - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- Contract Limits (per symbol), Trade Limits (max trades per day/week), Symbol Block (immediately closes positions in blocked symbols), Lockout ("Once applied, it can't be canceled"; closes positions and cancels orders), Trade Clock (pauses new entries without closing positions) - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx); [Topstep Brokerage Help](https://help.topstepbrokerage.com/en/articles/15368476-topstepx-platform-guide)
- Topstep markets these as habit-building: PDLL "automatically locks you out if you hit your max loss"; Contract Limits "prevent emotional oversizing" - [Topstep Brokerage blog](https://topstepbrokerage.com/blog/topstepx-futures-trading-platform-funded-traders)
- A Discord channel #get-flat exists for manual flatten requests (account number, platform, reason) - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)

**Multi-account and copier**
- Built-in Trade Copier (Settings -> Copy Trading): Lead account -> one or more Followers; the Lead must have the lowest Maximum Position Size in the group; Followers support PDLL/PDPT but not Trade Limits/Symbol Block; turning the copier off mid-trade immediately flattens followers; if Lead hits DLL, followers flatten and stay linked; if Lead hits MLL, trader must unlink manually; requesting a payout auto-unlinks followers; not allowed on Live Funded Accounts - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- Different Scaling Plan tiers between Lead and Followers may disconnect the copier at start of a new day (third-party summary) - [search summary of TopstepX copier docs via tradecopia/syncmytrade](https://tradecopia.com/learn/copy-trade-topstep)
- Account nicknames (max 30 chars, "Nicknames aren't visible to Trader Support") to avoid trading the wrong account - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)

**Stats, journal, community**
- Performance Dashboard (desktop only): Balance High/Current/Low, Best Day %, Avg Win/Loss, Best/Worst Trade, Win Rate, Long vs Short %, P&L, Trade Log, Trader Journal (emotions/notes), Calendar with daily P&L, social sharing; trade export to spreadsheet - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- The Tilt: proprietary sentiment indicator showing long/short positioning of all Topstep funded traders in ES, NQ, CL, GC, updated every 10 s - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- Trader Hub with news and economic calendar powered by Financial Juice; integrated TopstepTV content; Training Camp education module - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx); [h2tfunding (affiliate)](https://h2tfunding.com/what-platform-does-topstep-use/); [proptradingvibes (affiliate)](https://proptradingvibes.com/blog/topstep-trading-platforms)
- Trading Tones: custom audio (mp3/wav/ogg/m4a/webm, max 3 MB) for events incl. Order Filled, TP/SL, Rejected, Market Open/Close, Position Closed, PDLL, MLL, DLL, PDPT - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- Privacy/"streamer mode" hides Balance, MLL, RP&L, UP&L, DLL - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)

**Mobile, API, replay**
- No native app; mobile-friendly web with sections Account Info, All Accounts, Positions (one-tap close), Orders, Trades, mobile DOM and charts with drag TP/SL; "We're unable to troubleshoot mobile-specific issues related to connectivity." - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- A third-party page claims a native iOS/Android app is coming "very soon" (unverified) - [h2tfunding (affiliate)](https://h2tfunding.com/what-platform-does-topstep-use/)
- API: ProjectX Gateway API (REST + WebSocket), $29/month, 50% off with code "topstep" ($14.50), billed by "Sim2Funded Solutions"; set up via dashboard.projectx.com; no sandbox (use Practice accounts); HFT prohibited; VPS/VPN prohibited; not available on Live Funded Accounts; "Orders executed via the API are final - no review, adjustment, or reversal." - [Topstep Help: API Access](https://help.topstep.com/en/articles/11187768-topstepx-api-access)
- Market replay: not available; "Market Replay Data" is a "Planned" item with 965 votes on the ProjectX feature board - [ProjectX feature board](https://s2f.featurebase.app/)
- Quantower can still log in with ProjectX credentials (Combine and XFA, not Live) - [ProjectX changelog 2025-04-17](https://s2f.featurebase.app/changelog); [proptradingvibes](https://proptradingvibes.com/blog/topstep-trading-platforms)

**Data fees**
- Level 1 (top of book) free for all 4 exchanges; Level 2 "Depth of Market Bundle" $38/month, not pro-rated, billed on the 28th, no refunds; must sign CME agreement and declare non-professional - [Topstep Help: Level 1 and Level 2 data](https://help.topstep.com/en/articles/8284120-level-1-and-level-2-market-data). A search snippet cites $36.50/month bundle or $12.10 per exchange for 2026 - unverified, conflicts with the help page.

### Inferences
- The "risk settings" panel (PDLL/PDPT/trailing PDLL/contract limit/trade limit/symbol block/lockout/trade clock/daily risk lock) is the clearest feature to copy into Kronant Trader: cheap to build in a simulated-execution engine, highly marketable to prop firms as "responsible trading".
- The trade copier with explicit rules (Lead = smallest max position, follower flatten on unlink, auto-unlink on payout) is a good reference spec for a multi-account feature.
- The deliberate limits (one DOM, no custom indicators, no stop-limit, one session, no replay) show that a prop-only terminal can win with a narrow feature set if risk UX is strong; Kronant can match this and differentiate on replay, a native-feeling mobile PWA and broader order types.

### Gaps
- No public, dated TopstepX release notes from Topstep itself; the ProjectX changelog on featurebase is the closest (entries end in Sep 2025 in what was retrievable).
- Whether a native mobile app has shipped by Oct 2026 could not be confirmed; the official help page still says there is none. The ProjectX board lists "Desktop Application" as "finished", which conflicts with help pages describing a browser-only product.

## 2. How does TopstepX show prop rules inside the platform? (most important)

### Takeaway
The trader sees the firm rules as live numbers in a header bar above the trading area: Balance (current/high/low), Maximum Loss Limit (MLL), Realized P&L, Unrealized P&L, Daily Loss Limit (DLL), and optionally "Distance to MLL" instead of the raw MLL. Account status labels (Active, Temp. Liquidation, Goal Achieved, Ineligible) appear in an "All Accounts" list. Enforcement is real-time on net P&L incl. unrealized, with automatic liquidation plus audio alerts. Deeper rule state (consistency, profit-target progress, payout eligibility) lives mainly in the separate Topstep web Dashboard, not documented as inside TopstepX.

### Cited Findings
- Header shows: Balance (Current, High, Low), Maximum Loss Limit (MLL), Realized P&L (RP&L), Unrealized P&L (UP&L), Daily Loss Limit (DLL), and an optional "Distance to MLL" swap display - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- Troubleshooting entry "Daily Loss Limit Value Not Showing": set browser zoom to 100%, maximize window, check header customization/gear, confirm correct account selected (i.e. the header is customizable and can overflow at smaller widths) - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- Balance font size is adjustable (Settings -> Misc); RP&L/UP&L background colors customizable; TPDLL balance visibility enhancements added Mar 2025 - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx); [ProjectX changelog 2025-03-12](https://s2f.featurebase.app/changelog)
- Mobile "All Accounts" view shows per account: status (Active, Temp. Liquidation, Goal Achieved, Ineligible), creation date, Win Rate, lots traded, Day P&L, Open P&L, balance. Mobile "Account Info" shows balance, UP&L, RP&L, "parameters", account switching and the Lockout trigger - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- Account panel at bottom: Accounts, Positions (editable risk/take-profit in USD), Orders (current trading day), Trades (round trips), Quotes - [Topstep Brokerage Help: platform guide](https://help.topstepbrokerage.com/en/articles/15368476-topstepx-platform-guide)
- Audio alerts fire on MLL, DLL, PDLL and PDPT events (Trading Tones) - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)
- MLL mechanics: $2,000/$3,000/$4,500 for 50K/100K/150K; trails highest end-of-day balance, never moves down, locks at starting balance; "Both realized and unrealized P&L count ... monitored in real time"; touching it liquidates immediately; Combine becomes ineligible until reset, Express Funded Account is permanently closed (Back2Funded reactivation up to 2 times within 7 days) - [Tradetanto rules guide](https://tradetanto.com/learn/topstep-rules)
- DLL is optional in Combine and XFA ($1,000/$2,000/$3,000) and automatic in Live; when hit, positions are flattened, pending orders canceled, "No new trades until 5 PM CT next session"; "Account stays eligible for funding. Resume tomorrow." (not a violation) - [Topstep Help: Daily Loss Limit](https://help.topstep.com/en/articles/10490293-daily-loss-limit-in-the-trading-combine-and-express-funded-account). This maps to the "Temp. Liquidation" status label.
- New or reset TopstepX accounts were created without a mandatory DLL from 2024-08-25 - [TradersPost (secondary)](https://blog.traderspost.io/article/automate-topstep-with-traderspost)
- Consistency target (Combine): best day cannot exceed 50% of total profit, otherwise the target rises; profit targets $3,000/$6,000/$9,000 - [Tradetanto](https://tradetanto.com/learn/topstep-rules)
- A third-party journal guide tells traders to check "the Topstep dashboard" for "account, stage, current MLL, objective state, maximum position size, optional DLL" and consistency ("best locked day, total profit, ratio, and adjusted target") - i.e. those are on the web Dashboard, and the guide does not reproduce any TopstepX UI for them - [Traders Second Brain](https://traderssecondbrain.com/guides/topstep-trading-journal)
- Copier-specific rule display: Lead account constraints (lowest MPS) and behavior on DLL/MLL hits are documented, not visually explained - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)

### Inferences
- What to copy: rules as first-class header metrics next to balance, a "distance to breach" mode (more useful than the absolute floor), status labels per account in an account switcher, audio alerts on rule events, and a clear soft-breach vs hard-breach distinction (DLL = temporary lock "resume tomorrow" vs MLL = fail).
- What to do better: TopstepX appears to split rule state between the terminal (MLL/DLL numbers) and the web Dashboard (profit target progress, consistency, payout eligibility). Kronant can show all objectives in the terminal: progress bar to profit target, consistency ratio with "best day" and adjusted target, minimum trading days, and a live warning as equity approaches the floor (e.g. color states at 50/75/90% of allowed drawdown). The header overflow issue (DLL disappears below 100% zoom) suggests a responsive rule panel is needed.
- The trailing MLL being end-of-day-based while breach checks are intraday real-time is a common source of trader confusion; showing "today's floor" and "floor after today's close if you end here" would be a differentiator.

### Gaps
- No screenshot-level or official description of a profit-target progress bar or consistency widget inside TopstepX was found; a search-engine summary claimed a progress bar exists in the "account panel", but the underlying page (traderssecondbrain) did not confirm it, so treat as unverified.
- Exact on-screen wording/notifications at breach (toasts, modals, emails) is not documented in public help pages.

## 3. What do traders praise?

### Takeaway
Praise centers on the built-in risk/discipline tools (PDLL, lockout), the integrated copier, simplicity (browser, no install, commission-free, free L1 data), and responsive support chat. Even critical reviewers call it a "decent" platform when it is stable. Direct Reddit/YouTube sentiment could not be retrieved, so evidence is thinner here than for complaints.

### Cited Findings
- Trustpilot (Topstep overall, not TopstepX only): 3.7/5 from 14,903 reviews as of Oct 2026; ~70% five-star; recent review (2026-10-06) says "TopStepX is actually a decent platform"; 2026-10-01 review praises live chat resolving a chart display issue; platform-specific complaints are a small fraction, most negatives are about payout policies - [Trustpilot](https://www.trustpilot.com/review/topstep.com)
- An affiliate reviewer (4+ years, 50+ firms) calls TopstepX "the best proprietary platform in funded futures", citing TradingView drawing tools, PDLL/PDPT, trade limits, lockouts, copier and API - [proptradingvibes (affiliate)](https://www.proptradingvibes.com/blog/why-traders-leave-topstep)
- Pros listed in a July 2025 review: built for evaluation trading, commission-free, browser-based, integrated TradingView charting, API - [PickMyTrade review, 2025-07-14 (affiliate)](https://blog.pickmytrade.io/topstepx-2025-review/)
- The Lockout button was notable enough that NinjaTrader users requested "Lockout Button Like Topstep Added to their New Platform" on the NinjaTrader forum - [NinjaTrader forum thread 1296510](https://forum.ninjatrader.com/forum/suggestions-and-feedback/suggestions-and-feedback-aa/1296510-lockout-button-like-topstep-added-to-their-new-platform)
- Feature board shows high engagement and that top requests do ship: Performance Stats improvements (1.2k votes, finished), Chart Alerts (1.1k, finished), Cumulative Delta (983, finished), extra timeframes (661, finished) - [ProjectX feature board](https://s2f.featurebase.app/)

### Inferences
- Traders value the platform enforcing their own discipline, which aligns with the firm's interest. Kronant should present risk tools as trader benefits, not only firm controls.
- A public feature-voting board with visible "finished" items builds goodwill; Kronant could offer firms a per-tenant or shared roadmap board.

### Gaps
- Could not retrieve r/Topstep, r/FuturesTrading or r/Daytrading threads (search did not index them; nexusfi returned 403). No App Store reviews exist (no native app). No YouTube review content retrieved. Frequency of praise on Reddit is therefore unknown.

## 4. What do traders complain about? (incidents with dates)

### Takeaway
The dominant complaint in 2025-2026 is reliability: a cluster of outages in Oct-Dec 2025 (11 acknowledged/counted issues in about three months), failing exits and stops, accounts liquidated during outages, and compensation perceived as insufficient. Secondary complaints: no custom/third-party indicators, no market replay, no footprint/order-flow charts, limited bracket/order options, no native mobile app, lag at market open, extra cost for Level 2 and API, and being forced off TradingView/NinjaTrader.

### Cited Findings
**Dated incidents**
- ~2025-07-31 (tweet ID decodes to 2025-07-31 22:50 UTC): Topstep tweeted "We know some traders were affected by platform and dashboard issues earlier today. The issue has been resolved ... Trader Support Team is reviewing support tickets from impacted traders." - [Topstep on X](https://x.com/Topstep/status/1951052848814260568)
- Oct-Dec 2025: "11 confirmed platform issues in the past three months" (count by X user Ajtradesss); traders unable to open or close positions; accounts "blown up"; one trader: "an every week thing"; Topstep on Discord: "Right now, we are not delivering the Ultimate Trading Experience we promised."; CEO Michael Patak: "in January we will be making things right"; Trustpilot fell to 3.6, 16% one-star, company replied to 4% of negative reviews - [Finance Magnates, 2025-12-23](https://www.financemagnates.com/forex/topstep-faces-prop-traders-wrath-due-to-repeated-outages-ceo-sets-january-deadline-for-a-fix/)
- 2025-12-16/17: outage of 3+ hours (from 5:00 PM CST on 12/16) and another ~45 minutes on the morning of 12/17; poster says disruptions recur roughly every 10 days and support "essentially accused [him] of lying" despite video; Topstep's reply: negative trades between 5:00-5:30 PM CST on 12/16 removed and accounts reinstated, but "we are unable to compensate for unrealized profits"; another user: "It's been happening to thousands of users" - [Elite Trader thread, 2025-12-17/18](https://www.elitetrader.com/et/threads/ongoing-topstepx-platform-issues-being-dismissed-by-support-with-video.387981/)
- 2025-12-19: user post "Topstep Official Statement ... What about all the affected traders" (tweet ID decodes to 2025-12-19) - [HitTrades on X](https://x.com/HitTrades/status/2002086299621265627)
- Late 2025 summary: outages, "delayed order executions", failed SL/TP, account closures attributed to malfunctions; statement as of 2025-12-21 acknowledged instability and outlined technical improvements; by March 2026 "some customers acknowledge that Topstep addressed the service disruption by compensating affected traders" - [Wikipedia: Topstep](https://en.wikipedia.org/wiki/Topstep)
- 2026-02-01 (competitor source, unverified): platform repeatedly rejected valid exit orders in Micro Gold with "Outside of trading hours" during active CME hours; claims compensation was "evaluation resets only", no funded reinstatements, and Trustpilot dropped 4.5 -> 3.4 - [Phidias (competitor page)](https://phidiaspropfirm.com/education/topstep-vs-tradeify). Note: the compensation claim conflicts with the reinstatement offer quoted in the Elite Trader thread.
- 2026-09-22 Trustpilot review: software froze mid-trade, causing a profit dispute; 2026-10-04 review complains about forced migration from TradingView to the proprietary platform - [Trustpilot](https://www.trustpilot.com/review/topstep.com)

**Feature gaps (by vote count on the ProjectX feature board, a proxy for complaint frequency)**
- Third-party indicators for TradingView: 1.6k votes, backlog - [ProjectX feature board](https://s2f.featurebase.app/)
- Bracket improvements (e.g. percent mode): 1.1k votes, backlog - [ProjectX feature board](https://s2f.featurebase.app/)
- Market Replay Data: 965 votes, planned ("This is a MUST for new traders") - [ProjectX feature board](https://s2f.featurebase.app/)
- Footprint/order-flow charting: 353 votes, backlog; hotkeys for limit orders and custom OCO also backlog - [ProjectX feature board](https://s2f.featurebase.app/)
- Request that Topstep work with NinjaTrader mobile - [ProjectX feature board post](https://s2f.featurebase.app/p/make-top-step-compatible-with-ninjatrader-mobile)
- Platform bugs and lag at peak hours/market open (July 2025 review) - [PickMyTrade (affiliate)](https://blog.pickmytrade.io/topstepx-2025-review/)
- Only one DOM, not movable to a second screen; no custom indicators; no native mobile app - [Topstep Brokerage Help](https://help.topstepbrokerage.com/en/articles/15368476-topstepx-platform-guide)
- Third-party tools vendors request a ProjectX data feed (e.g. ATAS feature request), showing order-flow traders lack their tools - [ATAS feature board](https://feedback.atas.net/p/please-add-projectx-data-feed-for-topstep-x)

**Costs**
- Level 2 $38/month non-prorated, no refunds - [Topstep Help: market data](https://help.topstep.com/en/articles/8284120-level-1-and-level-2-market-data); API $29/month extra - [Topstep Help: API](https://help.topstep.com/en/articles/11187768-topstepx-api-access)

### Inferences
- Rough frequency: reliability is the most-cited and most-press-covered issue (Q4 2025 peak, tapering in 2026 per Trustpilot where recent platform complaints are a "small fraction"). Feature-gap complaints are persistent but lower-intensity.
- The core lesson for Kronant: in a prop context every outage is a rules dispute (breach during outage, unrealized profit lost). Kronant should ship (a) an outage/incident flag that freezes rule evaluation or auto-voids breaches in a declared incident window, (b) a back-office tool for firms to reinstate accounts and remove trades within a time window with audit trail, and (c) a public status page. Topstep handled this manually via tickets and Discord.
- Since Kronant runs simulated execution, it can avoid the "exchange-side" class of failures but must handle price-feed gaps, which cause the same perceived "rejected exit" problems.

### Gaps
- No official Topstep incident log or status page history was found; individual outage dates beyond those above are not public.
- Reddit complaint volume could not be measured.

## 5. Reception of the move to TopstepX-only and Topstep's business reasons

### Takeaway
New Combines became TopstepX-only from 2025-07-07, resets on legacy platforms ended 2025-07-31/08-01; NinjaTrader and Tradovate were closed to new sign-ups, Quantower survives as a ProjectX-login front end. Topstep's stated reason is outcome data (TopstepX traders get funded more often and keep accounts longer) plus focus; unstated but evident motives are control of data, rule enforcement, cost and vertical integration (ProjectX exclusivity, Plus500, Topstep Brokerage). Reception is mixed: power users lost tools, and the Q4 2025 outages hit just after the forced switch.

### Cited Findings
- July 7, 2025: all new Trading Combines only on TopstepX; August 1, 2025: resets only on TopstepX; legacy-platform accounts that hit MLL cannot reset; Live Funded traders continue as normal - [h2tfunding (affiliate)](https://h2tfunding.com/what-platform-does-topstep-use/)
- NinjaTrader's own FAQ confirms Topstep stopped offering NinjaTrader for new users as of July 7, 2025, and resets through July 31, 2025 - [NinjaTrader: Topstep FAQs](https://ninjatrader.com/ninjatrader-for-prop-traders/topstep-faqs/)
- Stated reason (as quoted by a third party): "traders using TopstepX are getting funded more often, maintaining their accounts longer, and securing more consistent payouts than traders on any other platform" - [h2tfunding (affiliate)](https://h2tfunding.com/what-platform-does-topstep-use/); focusing resources on one platform to "accelerate improvements, offer more focused trader support" - [h2tfunding search summary](https://h2tfunding.com/what-platform-does-topstep-use/)
- Topstep's own marketing: TopstepX built to help traders "develop the habits they need to get funded and stay funded" - [Topstep Brokerage blog, 2026-04-24](https://topstepbrokerage.com/blog/topstepx-futures-trading-platform-funded-traders)
- Help center (checked Aug 2026) no longer mentions NinjaTrader/Tradovate; Quantower documented only for Combine and XFA, not Live - [proptradingvibes (affiliate)](https://proptradingvibes.com/blog/topstep-trading-platforms)
- A claim that "over 82% of recently registered Topstep traders chose TopstepX" before the switch appeared in a search summary; original source not identified - unverified.
- Negative reception: 2026-10-04 Trustpilot review complaining about forced migration from TradingView - [Trustpilot](https://www.trustpilot.com/review/topstep.com); third-party indicator demand (1.6k votes) - [ProjectX feature board](https://s2f.featurebase.app/)
- Vertical integration: Plus500 became Topstep's clearing and technology backbone (2025-10-22), Plus500 as FCM and Topstep Brokerage as IB - [Finance Magnates, 2025-10-22](https://www.financemagnates.com/forex/brokers/plus500-becomes-tech-backbone-for-us-prop-firm); Topstep Brokerage clients trade on TopstepX - [TradeInformer](https://tradeinformer.com/broker-news/topstep-launches-introducing-broker-through-plus500-partnership)
- Exchange rules cited for the single-session restriction - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)

### Inferences
- Business logic visible from the facts: owning the terminal lets Topstep (1) enforce rules in real time inside the same system (no third-party sync lag), (2) add discipline tools that reduce blow-ups and support load, (3) remove per-user platform/data costs paid to NinjaTrader/Tradovate/Rithmic, (4) control the funnel to Topstep Brokerage. These are the same arguments Kronant can make to prop firms for an integrated terminal + back office.
- The risk of a forced migration is that every reliability failure becomes the firm's fault, not a vendor's. Kronant firms will inherit that risk, so SLA, status page and incident tooling matter for sales.

### Gaps
- No primary Topstep announcement text (blog/email) with the business rationale was retrieved; quotes come via affiliates.
- No public data on churn caused by the switch.

## 6. What is ProjectX - white label, which firms, pricing?

### Takeaway
ProjectX was a white-label futures prop platform and API made by Sims2Funded Solutions (rebranded ProjectX in mid-2024), licensed to many US futures prop firms. In November 2025 it announced it would end third-party service on 2026-02-28 and become exclusive to Topstep, citing compliance/audit demands. It is no longer available as a white label; firm-level license pricing was never public.

### Cited Findings
- Sims2Funded rebranded as ProjectX and announced launches of multiple futures prop firms (2024) - [FundedTrading.com](https://fundedtrading.com/sims2funded-rebrands-as-projectx-announces-launches-of-multiple-futures-prop-firms)
- ProjectX "is the trading dashboard, API, and order routing infrastructure that prop firms layer their account rules on top of"; firms set the rules, ProjectX provides the execution technology - [TradersPost](https://blog.traderspost.io/article/what-is-projectx-prop-firms); company ProjectX Trading, LLC, Miami - [TradersPost](https://blog.traderspost.io/article/what-is-projectx-prop-firms)
- Past users included Top One Futures, Lucid Trading, Tradeify, Blue Guardian Futures, TickTick/Tick Tok Trader, Alpha Futures, FuturesElite and others - [Finance Magnates via CoinSpectator, 2025-11-17](https://coinspectator.com/mainstream/2025/11/17/prop-firms-report-futures-prop-tech-provider-projectx-to-end-its-third-party-service-offering/); also listed: E8, Aqua Futures, GoatFundedTrader, FXIFY Futures, Bulenox, Phidias - [PropTradingVibes ProjectX list](https://proptradingvibes.com/best-prop-firms/projectx)
- ProjectX statement: "we've made the decision to wind down our ProjectX third-party service offering ... Shifts in operational demands and upcoming compliance reporting, oversight, and audit requirements have made continuing third-party support no longer sustainable." Announced ~2025-11-15, service end 2026-02-28 - [Finance Magnates via CoinSpectator](https://coinspectator.com/mainstream/2025/11/17/prop-firms-report-futures-prop-tech-provider-projectx-to-end-its-third-party-service-offering/)
- As of Sep 2026, ProjectX Gateway API docs list only TopstepX and The Futures Desk - [TradersPost](https://blog.traderspost.io/article/what-is-projectx-prop-firms); Topstep acquired The Futures Desk on 2026-04-01 and plans to integrate TFD technology into TopstepX - [Press release via AOL, 2026-04-01](https://lite.aol.com/entertainment/story/0022/20260401/9682678.htm)
- Claims that Topstep "acquired" ProjectX outright in late 2025 appear only on affiliate sites - [proptradingvibes/coincodecap search summary](https://proptradingvibes.com/blog/topstep-trading-platforms); another describes it as an "exclusive licensing deal" - [fortraders (low quality, misdates TopstepX branding to Nov 2025)](https://fortraders.com/blog/project-x-trading). API billing still shows "Sim2Funded Solutions" - [Topstep Help: API](https://help.topstep.com/en/articles/11187768-topstepx-api-access)
- Firm license was charged to the prop firm, not the trader; no amounts disclosed - [fortraders](https://fortraders.com/blog/project-x-trading). Trader-level API: $29/month - [Topstep Help: API](https://help.topstep.com/en/articles/11187768-topstepx-api-access)
- Firms losing ProjectX moved to Tradovate, NinjaTrader, Rithmic-based stacks or in-house builds - [fortraders](https://fortraders.com/blog/project-x-trading)

### Inferences
- The ProjectX shutdown left many US futures prop firms without a branded prop terminal at short notice (about 3.5 months) - a concrete market gap and a cautionary tale: vendor dependency risk is a selling point for Kronant (contractual continuity, data export, notice periods).
- Kronant targets forex/CFD/crypto rather than CME futures, so it does not compete head-on, but firms that operate both lines may want one vendor.

### Gaps
- Exact legal form of the Topstep-ProjectX relationship (ownership vs exclusive license) is not confirmed by a primary source.
- No public white-label pricing ever found.

## 7. Recent product changes (2025-2026)

### Takeaway
2025 brought most of the risk and copier features (trailing PDLL, symbol block, contract limits, follower PDLL/PDPT, account-based brackets, Auto-OCO, API launch). 2026 is about consolidation: exclusivity, Topstep Brokerage live trading on TopstepX, TFD acquisition, performance/journal features, price alerts, range bars.

### Cited Findings
- 2025-02-09 Trailing PDLL; 2025-03-12 UP&L/RP&L color customization and TPDLL visibility; 2025-04-07 Symbol Block; 2025-04-17 Quantower login with ProjectX credentials; 2025-05-12 API official launch ($29/month); 2025-06-15 copier sync improvements to reduce fill mismatches; 2025-07-05 Contract Limits; 2025-08-24 PDLL/PDPT on follower accounts; 2025-09-07 Position Brackets per account; 2025-09-21 Auto-OCO brackets with multiple TP/SL levels - [ProjectX changelog](https://s2f.featurebase.app/changelog)
- Performance Stats improvements finished (Jul 1), extra timeframes (Jul 31), Cumulative Delta (Sep 30), Chart Alerts finished; year not shown in retrieved data - [ProjectX feature board](https://s2f.featurebase.app/)
- 2025-07-07 / 2025-08-01 TopstepX-only for new Combines/resets - [NinjaTrader FAQ](https://ninjatrader.com/ninjatrader-for-prop-traders/topstep-faqs/); [h2tfunding](https://h2tfunding.com/what-platform-does-topstep-use/)
- 2025-10-22 Plus500 partnership - [Finance Magnates](https://www.financemagnates.com/forex/brokers/plus500-becomes-tech-backbone-for-us-prop-firm)
- 2025-11 ProjectX exclusivity announcement; 2026-02-28 third-party end - [Finance Magnates via CoinSpectator](https://coinspectator.com/mainstream/2025/11/17/prop-firms-report-futures-prop-tech-provider-projectx-to-end-its-third-party-service-offering/)
- Early 2026 Topstep Brokerage (IB) launched, "Prop-to-Brokerage Funding Model", clients trade on TopstepX - [Wikipedia](https://en.wikipedia.org/wiki/Topstep); [TradeInformer](https://tradeinformer.com/broker-news/topstep-launches-introducing-broker-through-plus500-partnership)
- 2026-04-01 Topstep acquires The Futures Desk; TFD tech to be integrated into TopstepX - [Press release via AOL](https://lite.aol.com/entertainment/story/0022/20260401/9682678.htm)
- Current help center lists features not in older guides: Daily Risk Lock, Trade Clock, Range Bars (beta), Price Alerts, Trader Hub (Financial Juice), Performance Dashboard with journal and calendar, streamer privacy mode, account nicknames, confirm account switching - [Topstep Help: TopstepX](https://help.topstep.com/en/articles/14434175-topstepx)

### Inferences
- The product roadmap is visibly driven by prop-specific risk features rather than charting depth; the fastest-shipping items were self-imposed limits and copier controls.

### Gaps
- No exact ship dates found for Daily Risk Lock, Trade Clock, Range Bars, Price Alerts or Trader Hub, and no 2026 changelog entries were retrievable.
