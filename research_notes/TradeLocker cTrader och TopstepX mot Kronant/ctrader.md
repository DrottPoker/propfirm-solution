# cTrader (Spotware) as a prop firm trading platform, state as of October 2026

Research date: 2026-10-06. Reddit could not be fetched by the research tools (domain blocked), so Reddit sentiment is missing and is listed as a gap. Trader sentiment below comes from Trustpilot, the cTrader community forum, app store ratings and review sites.

## 1. What features does cTrader offer to traders?

### Takeaway
cTrader is a full-featured, multi-device platform (Windows, Mac, Web, iOS, Android) with strong charting (26 timeframes, about 65-70+ built-in indicators, multi/free/detached charts), three kinds of Depth of Market, server-side trade protections (trailing stop, break-even, up to five take-profit levels since May 2026), risk-based position sizing, per-fill trade receipts, built-in copy trading, C# and Python algos with free cloud execution, free Open API and FIX API, and one cTrader ID for all accounts. Its signature is transparency and an "institutional" order-entry feel.

### Cited Findings
**Platforms and accounts**
- Apps for Windows, macOS, Web, iOS and Android, translated into 23 languages; includes cTrader Copy (integrated social/copy trading) and cTrader Algo - [Spotware cTrader overview](https://www.spotware.com/ctrader-overview)
- One cTrader ID gives traders access to all their trading accounts in one place, which Spotware pitches to prop firms as useful for traders with several challenges - [Spotware, cTrader for prop firms](https://www.spotware.com/ctrader/prop-firms/)
- "Shared Access" (desktop 5.0.22) lets a user grant access to a trading account to another person, e.g. a money manager, in branded cTrader apps - [Finance Magnates via TradingView](https://vn.tradingview.com/news/financemagnates%3A866032b80094b%3A0-spotware-introduces-shared-access-in-latest-ctrader-desktop-app-update)

**Charting**
- 26 chart timeframes (vs 9 on MT4 and 21 on MT5) - [ForexBrokers.com, Aug 14 2026](https://www.forexbrokers.com/guides/best-ctrader-brokers)
- "70+ indicators", Level 2 DoM included - [PropFirmApp cTrader review 2026](https://propfirmapp.com/trading-tools/ctrader)
- FTMO's branded cTrader mobile app lists standard timeframes plus Tick, Renko and Range charts, 65 popular technical indicators, push/email alerts, QuickTrade one-click mode, dark theme, 22 languages and an account ROI chart - [App Store, FTMO Platform cTrader](https://apps.apple.com/cz/app/ftmo-platform-ctrader/id1530411160?l=cs)
- Chart layout modes: Multi-chart (default, drag charts around), Single-chart (tabs), Free-chart (resize each chart individually). Charts can be detached into separate windows with their own toolbars and dragged between detached containers; linked charts, chart shots, market sentiment and risk-reward tool are documented - [cTrader Help, chart modes](https://help.ctrader.com/ctrader/charts/chart-modes/)
- cTrader 5.2 Web (May 2025) added timeframe-specific visibility for chart objects and indicators to reduce clutter - [Spotware, cTrader 5.2](https://www.spotware.com/news/spotware-unveils-ctrader-5-2-with-advanced-risk-reward-tools/)
- cTrader 5.4 (Aug 26 2025) added a new time axis, preset date ranges, a "Go to" date function, full-screen configuration with color themes and automatic time zone detection - [Finance Magnates, cTrader 5.4](https://www.financemagnates.com/forex/spotware-adds-risk-management-and-mobile-upgrades-to-ctrader-54-with-python/)
- Mobile 5.6 added a candle countdown on charts and an equity chart in the account dashboard (shows how trading, deposits and withdrawals affect equity over time) - [FX News Group, Mobile 5.6](https://fxnewsgroup.com/forex-news/platforms/spotware-launches-ctrader-mobile-5-6-with-enhanced-charting-and-equity-visualisation/); [Finance Magnates, Mobile 5.6](https://www.financemagnates.com/forex/ctrader-mobile-56-updates-tools-for-retail-traders-as-market-set-to-hit-133b-by-this-decade/amp/)

**Order entry, sizing and protections**
- Depth of Market in three forms: Standard DoM (view only), Price DoM (ladder with price column in the middle, enter stop/limit orders and manage positions directly in the ladder), VWAP DoM (shows volume-weighted average fill price for a chosen volume, trade directly; hover shows pip value, trade value and commission) - [cTrader Help, Depth of Market](https://help.ctrader.com/trading-with-ctrader/depth-of-market/)
- Risk-reward tool with three calculation modes (risk, reward or size) that computes order volume from stop-loss distance and risk exposure (Mobile 5.2, May 2025); Mac got the advanced risk-reward tool, volume calculation from risk tolerance or target reward and automatic SL/TP alignment in 5.4 (Aug 2025) - [Spotware, cTrader 5.2](https://www.spotware.com/news/spotware-unveils-ctrader-5-2-with-advanced-risk-reward-tools/); [Finance Magnates, cTrader 5.4](https://www.financemagnates.com/forex/spotware-adds-risk-management-and-mobile-upgrades-to-ctrader-54-with-python/)
- Server-side protections: trailing stop loss, break-even stop loss, and "Advanced take profit" with up to five take-profit levels (scaling out) on all cTrader apps, settable from order, position, pending-order screens and directly from the chart; announced 28 May 2026; they keep working after the session ends - [Spotware, server-side scaling out, May 28 2026](https://www.spotware.com/news/ctrader-brings-server-side-scaling-out/); [cTrader Help, protections](https://help.ctrader.com/ctrader/trading/protections/)
- Before that change, Advanced Take Profit only worked while cTrader Desktop was running (forum complaint 17 Oct 2024 asking for server-side execution and mobile support) - [cTrader forum suggestion 45216](https://community.ctrader.com/forum/suggestions/45216)
- Supports iceberg orders and VWAP functionality - [PropFirmMap, Apr 8 2026](https://propfirmmap.com/blog/prop-firm-platforms-mt5-ctrader-match-trader-compared-2026)
- Partial fill support with VWAP pricing - [ForexBrokers.com](https://www.forexbrokers.com/guides/best-ctrader-brokers)

**Transparency and analytics**
- "Deal receipts on every fill" with entry/closing prices and market snapshots - [PropFirmApp](https://propfirmapp.com/trading-tools/ctrader)
- Spotware's own prop pitch: "detailed trade receipts make for transparent trading", "Traders First" execution without requotes or rejected trades - [Spotware, How to start a prop firm in 2026](https://www.spotware.com/news/how-to-start-a-proprietary-trading-firm-2026/)
- Trade statistics, price alerts, symbol watchlists, advanced order management settings in mobile app - [App Store, FTMO Platform cTrader](https://apps.apple.com/cz/app/ftmo-platform-ctrader/id1530411160?l=cs)
- Mobile 5.7 added an account ROI chart - [Spotware, Mobile 5.6 news / search summary](https://www.spotware.com/news/cTrader-mobile-5-6/) (version attribution from search snippet, not verified on the page)
- Market Replay (15 Sep 2026, Windows only): pick a historical date, virtual balance and playback speed; minute open-price or tick data; timestamps that can be exported/imported; full Trade Watch with Positions, Orders, History, Equity and Trade Statistics during replay - [Spotware, Market Replay](https://www.spotware.com/news/ctrader-market-replay/)

**Algo, APIs and copy trading**
- cTrader Algo: C# plus native Python (5.4, Aug 26 2025) for cBots, indicators and plugins; official Docker image for Linux console - [Finance Magnates, cTrader 5.4](https://www.financemagnates.com/forex/spotware-adds-risk-management-and-mobile-upgrades-to-ctrader-54-with-python/)
- Free cloud execution for cBots in C# and Python - [PropFirmApp](https://propfirmapp.com/trading-tools/ctrader)
- Open API is free and public for building custom apps that trade on any cTrader account; FIX API is offered to every trader with a cTrader account at no cost (prices with market depth, all order types, positions) - [Spotware dev resources](https://spotware.com/ctrader/dev-resources); [Spotware FIX API](https://www.spotware.com/ctrader-fix-api)
- Official MCP servers (14 May 2026): remote server via cTrader Web (account operations, market data) and local server via cTrader Windows (workspace and chart control); works with Claude Code, ChatGPT Codex, Cursor, Gemini CLI; plus a skills library - [Finance Magnates, May 14 2026](https://www.financemagnates.com/forex/technology/spotware-opens-ctrader-to-ai-agents-as-mcp-wave-catches-up-with-retail-trading/)
- Mobile WebView plugins (5.4) let third-party tools, widgets and dashboards run inside the mobile app - [Finance Magnates, cTrader 5.4](https://www.financemagnates.com/forex/spotware-adds-risk-management-and-mobile-upgrades-to-ctrader-54-with-python/)

### Inferences
- The features prop traders most associate with cTrader and that Kronant could copy: VWAP/Price DoM, per-fill trade receipts with market snapshot, server-side trailing/break-even/multi-TP, risk-based volume calculation from SL distance, detachable charts and free-chart layout, one login for many accounts, equity/ROI charts in the account view, and a replay simulator.
- Server-side multi-level take profit only arrived in May 2026, which shows that even cTrader lagged on something traders want; a simulated-execution engine like Kronant's can implement all protections server-side from day one.

### Gaps
- Exact number of drawing tools and built-in indicators on desktop/web was not found in a primary source (65 is the mobile app listing; 70+ is a third-party review).
- No primary source confirmed details of QuickTrade (the help page URL tried returned 404); only the FTMO app listing mentions "one-click trading through QuickTrade Mode".
- No primary source found on partial close UI details or the symbol "Ticks"/price history tab beyond search snippets.

## 2. Does cTrader show prop challenge rules in the terminal, and what is Spotware's prop offering?

### Takeaway
No evidence was found that cTrader natively shows challenge objectives (daily loss, max drawdown, profit target) inside the trading terminal. Spotware supplies the platform, back office risk tools (including auto-suspension on drawdown breach) and lead generation (a prop challenge directory in the cTrader Store), while challenge logic, trader dashboards, KYC and payouts come from partners. Traders fill the in-terminal gap with paid/free third-party Store add-ons, and firms like FTMO show progress in their own web dashboards.

### Cited Findings
- Spotware's prop pitch: real-time trade mirroring, account grouping for scaling/payout tiers, order aggregation and exposure analysis, auto account suspension on drawdown breaches, real-time stats with dashboard alerts for risky behaviour - [Spotware, How to start a prop firm in 2026](https://www.spotware.com/news/how-to-start-a-proprietary-trading-firm-2026/)
- Same source: evaluation engines, trader dashboards, challenge logic, onboarding, KYC and payouts are handled through "industry platform partnerships"; named evaluation-engine partners Axcera and PropPulse; integration "usually around 5 days" - [Spotware, How to start a prop firm in 2026](https://www.spotware.com/news/how-to-start-a-proprietary-trading-firm-2026/)
- cTrader for prop firms page: back office with "flexible symbol, order and position management, along with advanced risk management features"; launch "in 5 days"; cTrader Leads (challenge listings on ctrader.com, branded company pages, "high-intent traders", dual IB model); the page does not mention showing rules in the terminal - [Spotware, cTrader for prop firms](https://www.spotware.com/ctrader/prop-firms/)
- cTrader Store prop challenge directory (27 Nov 2025): sortable/filterable list of challenges (price, account size, phases, profit target, daily and max loss, profit split) and firm cards with rules, payout policy, instruments, commissions, consistency rules and restricted countries; "only prop firms meeting clear reliability criteria"; Store has 10,000+ daily visitors - [Spotware, Nov 27 2025](https://www.spotware.com/news/prop-challenges-in-ctrader-store/)
- Directory scale: 500+ active challenges across 40+ firms - [PropFirmApp](https://propfirmapp.com/trading-tools/ctrader)
- Critique: the directory is "a lead generation product, not a vetting layer", based on commercial relationships rather than merit - [PropFirmApp](https://propfirmapp.com/trading-tools/ctrader)
- Third-party in-terminal rule trackers sold/listed in the cTrader Store: "Challenge Dashboard Tracker" (on-chart panel with equity, balance, phase profit %, max DD turning red on breach, profit target turning green, mini equity curve), "PROP RISK MONITOR v2" (daily loss, max drawdown, static or trailing limits, two-level audible alerts, daily reset), "PropFirmGuard PRO" - [cTrader Store, Challenge Dashboard Tracker](https://ctrader.com/products/4203); [cTrader Store, PROP RISK MONITOR v2](https://ctrader.com/products/4793); [cTrader Store, PropFirmGuard PRO](https://ctrader.com/products/4329)
- FTMO tracks objectives in its external "Account MetriX" tool (open trades, journal, analysis), not inside cTrader - [FTMO/OANDA blog, Account MetriX](https://ftmo.oanda.com/blog/account-metrix-a-tool-for-analyzing-and-improving-your-prop-trading/)
- B2BROKER sells a "cTrader White Label Prop" turnkey package: cTrader infrastructure plus a client cabinet based on B2PROP, liquidity/data feed, crypto processing, payment integrations; challenges with profit targets, risk limits and performance criteria - [Spotware, B2BROKER WL prop](https://www.spotware.com/news/b2broker-launches-ctrader-white-label-prop-trading-solution/); [B2BROKER](https://b2broker.com/news/b2broker-launches-ctrader-white-label-prop-trading-solution/)
- Voyage Markets launched a prop firm solution backed by cTrader - [Spotware, Voyage Markets](https://www.spotware.com/news/voyage-markets-press-release/)

### Inferences
- The lack of native challenge-rule display is the clearest gap for Kronant to exploit: traders on cTrader install third-party indicators or switch to a firm's web dashboard to see daily loss headroom. A terminal that shows the firm's real rules (daily loss left, max DD left, target progress, consistency rule, trading days) live, computed by the same engine that enforces them, would be a concrete differentiator.
- Spotware's model splits the prop stack across vendors (platform, evaluation engine, CRM/cabinet), so a prop firm on cTrader integrates 2-4 systems. Kronant selling terminal plus back office as one product addresses that integration pain.
- The Store directory turns cTrader into a lead-generation channel for firms; this is a business feature Kronant cannot easily copy but may matter to firms choosing a platform.

### Gaps
- No public documentation found for how cTrader's back office (cTrader Admin/cBroker) configures auto-suspension rules for prop accounts or how breaches are surfaced to the trader in the terminal.
- No information found on whether Axcera or PropPulse inject rule panels into cTrader itself (e.g. via plugins) or only provide web dashboards.

## 3. What do traders praise most?

### Takeaway
Praise centres on the clean, modern UI, ease of use, execution transparency (Level II, deal receipts, no requotes), advanced order handling and C#/Python algos. Ratings are very high on Trustpilot (4.8) and app stores (about 4.7), though Spotware actively campaigns for Trustpilot reviews, so the scores likely overstate typical satisfaction.

### Cited Findings
- Trustpilot: 4.8/5; one snapshot showed 2,883 reviews (87% 5-star, 8.6% 4-star, 1.3% 3-star, 0.7% 2-star, 2.5% 1-star), a later fetch showed 3,413 reviews; Spotware replies to 100% of negative reviews - [Trustpilot via search summary](https://www.trustpilot.com/review/ctrader.com?page=2); [Trustpilot 1-star filter](https://www.trustpilot.com/review/ctrader.com?stars=1)
- Positive review themes: user-friendliness, modern interface, ease of navigation, strong support, advanced trading features compared favourably with MT4/MT5 - [Trustpilot via search summary](https://www.trustpilot.com/review/ctrader.com?page=2)
- Spotware publicises Trustpilot milestones ("surpasses 1,000" and "2,000 Trustpilot reviews") and reported 1,200+ reviews rated "Excellent" in its 2025 review - [cTrader blog](https://blog.ctrader.com/page/3); [BrokerXplorer](https://www.brokerxplorer.com/amp/news/ctrader-surpasses-2000-trustpilot-reviews-7634); [FXStreet, Spotware 2025 review](https://www.fxstreet.com/press-releases/2025-marked-spotwares-shift-beyond-a-single-product-202601261418)
- iOS app (Spotware cTrader) rated 4.7 from about 1.6K ratings in one region; other regions 4.7 - [AppFollow](https://apps.appfollow.io/ios/ctrader-cfd-trading-charts/767428811?country=in)
- Trader forum opinion: preference for cTrader due to ease of use, good graphics with indicators, C# programming and built-in mechanisms against brokers trading against clients - [Elite Trader, "MT5 or cTrader?"](https://elitetrader.com/et/threads/mt5-or-ctrader.357538/post-5359815)
- Industry framing: cTrader is "the professional's choice"/"pro favorite" for execution speed and advanced orders; appeals to discretionary traders wanting clean charts, scalpers wanting ECN-style execution and tight spreads, and traders wanting DoM - [PropFirmMap, Apr 8 2026](https://propfirmmap.com/blog/prop-firm-platforms-mt5-ctrader-match-trader-compared-2026)
- Reviewer verdict: superior charting and order interface vs MetaTrader - [ForexBrokers.com, Aug 2026](https://www.forexbrokers.com/guides/best-ctrader-brokers)
- Awards: cTrader Mobile "Best Mobile Trading App" at UF AWARDS Global 2026; "Best Platform Provider" at Forex Expo Dubai (Sep 2026) - [LeapRate, Mobile 5.9](https://www.leaprate.com/forex/platforms/ctrader-rolls-out-mobile-5-9-upgrade/); [Spotware news list](https://www.spotware.com/news/)
- Support: Spotware says AI resolves 60% of trader inquiries within three minutes (2025) - [FXStreet, Spotware 2025 review](https://www.fxstreet.com/press-releases/2025-marked-spotwares-shift-beyond-a-single-product-202601261418)

### Inferences
- The recurring praise words (clean, modern, transparent, professional) suggest Kronant should treat visual calm and visible fairness (receipts, fill price vs quote, slippage shown) as core product values, not extras.
- Because Spotware solicits reviews, Trustpilot scores are a weak signal of satisfaction; forum threads and 1-star reviews are better sources for pain points.

### Gaps
- Reddit (r/Forex, r/propfirm, r/Daytrading) could not be accessed, so frequency of Reddit praise themes is unknown. YouTube and Forex Factory praise themes were not sampled systematically.
- Google Play rating and review counts were not retrieved.

## 4. What do traders complain about most? (with incidents and dates)

### Takeaway
Complaints cluster around (a) performance and bugs after updates (lag, freezing, high CPU/RAM, especially desktop 5.x and Web), (b) chart behaviour (scaling, scrolling, symbol switching), (c) a smaller ecosystem of indicators/bots than MetaTrader or TradingView, (d) missing features like trailing take profit and seconds charts, and (e) broker-side issues (spreads, fills) blamed on the platform. Negative reviews are a small share (about 2-3% 1-star on Trustpilot), and documented outages are mostly broker- or connector-specific rather than global.

### Cited Findings
**Trustpilot 1-star themes (roughly 2% of reviews)** - [Trustpilot 1-star filter](https://www.trustpilot.com/review/ctrader.com?stars=1)
- Chart scaling/zoom/scroll problems, cannot drag chart left (several reviews, Jun-Jul 2025/2026)
- Symbol changing between charts causing accidental trades (Jun 4 2025)
- Lot size changing without user action causing losses (Sep 15 2026)
- Fills far from chart price (Feb 10 2026); stop losses not triggering (Nov 30 2025, Feb 20 2026)
- Mobile update degraded navigation (Aug 18 2026); freezing, lag and crashes on mobile (Jun 2 2026)
- High CPU/RAM blamed on "lazy loading" optimisation (Jul 28 2026)
- Missing trailing take profit (Jul 22 2026) and some customisation
- Store algos/indicators failing to load or compile (Jul 28 2026); refund friction for bots (Dec 2 2025)
- Broker issues: high commissions/spreads (Aug 19 2026)

**Community forum: performance after updates**
- Desktop 5.2.11 (14 May 2025): "unusable", slow chart panning and delayed ticks despite free CPU/RAM; second user: updates come "with a lot of bugs"; no visible Spotware reply - [cTrader forum 47074](https://community.ctrader.com/forum/ctrader-support/47074/)
- Thread "Two years testing cTrader, and now the troubles of 5.0.40" (2024) - [cTrader forum 45421](https://community.ctrader.com/forum/ctrader-support/45421/)
- Older recurring threads: platform not responding, freezing after 10-15 minutes, RAM hogging - [cTrader forum 40138](https://community.ctrader.com/forum/ctrader-support/40138/); [cTrader forum 36727](https://community.ctrader.com/forum/ctrader-support/36727/)
- Web: Jan 2021 version slow and crashing (older); Web less responsive than Windows when dragging orders on chart; Nov 2024 performance regression after two problem-free years; Copy section "extremely laggy" on Firefox - [cTrader forum 34407](https://community.ctrader.com/forum/ctrader-web/34407); [cTrader Web forum](https://community.ctrader.com/forum/ctrader-web/)
- Mobile bug: platform switched to a different currency after pressing Buy/Sell from chart (date not confirmed) - [Forex Factory / forum search summary](https://www.forexfactory.com/thread/559656-spotware-ctrader-suggestions-improvements-and-issues-thread?page=2)

**Ecosystem and capability gaps (reviewer opinion)**
- Smaller third-party indicator ecosystem and fewer bots (cBots) than MT5 - [PropFirmMap, Apr 2026](https://propfirmmap.com/blog/prop-firm-platforms-mt5-ctrader-match-trader-compared-2026)
- Backtesting lags MT5's cloud resources; reliance on .NET seen as dated; narrower marketplace; fewer brokers - [ForexBrokers.com, Aug 2026](https://www.forexbrokers.com/guides/best-ctrader-brokers)
- No futures (only FX/CFDs/metals/crypto); smaller broker pool - [PropFirmApp](https://propfirmapp.com/trading-tools/ctrader)

**Most-voted feature requests (community forum, 5,382 suggestions total)** - [cTrader forum, Suggestions](https://community.ctrader.com/forum/suggestions/)
- "Let us move charts to the left side further, and magnify a vertical scale solely" - 248 votes (marked Completed), by far the top request
- "Custom Color Range" - 29 votes
- "Advanced protection for pending order" - 26 votes
- "Market Replay is great, BUT..." (May 2023) - 11 votes
- "Seconds bar charts" (Aug 2023) - 7 votes; "cBot Autostart" - 7 replies

**Outages and incidents (dated)**
- Third-party copier/connector Traders Connect logged cTrader incidents: 10 Jun 2024 (2h50m connection issues), 17 Jul 2024 (2h39m, cTrader platform down on API and web app), Jul 2024 (17m API issue) - [Traders Connect status](https://status.tradersconnect.com/clypoo9yj243247hjn1umwxl455); [Traders Connect status](https://status.tradersconnect.com/clx8mwkhq11628bin3bg7j91dl)
- 1 Aug 2025: trades not copied, issue on cTrader's side affecting selected brokers only - [IsDown, Traders Connect](https://isdown.app/status/traders-connect/incidents/425175-ctrader-api-issues)
- 13 Jan 2026: cTrader API issues for 7h25m; 7 Apr 2026: intermittent cTrader issues over 4 days 18 hours (as monitored by Traders Connect) - [StatusGator, Traders Connect cTrader connector](https://statusgator.com/services/traders-connect/ctrader-connector)
- Spotware scheduled maintenance as announced by broker Deriv: 6 Jun 2026 (reported as 05:00-19:00 GMT, which may be a transcription error) and 25 Jul 2026 05:30-07:30 GMT - [StatusGator, Deriv cTrader](https://statusgator.com/services/deriv/ctrader-services); [Deriv status](https://statuspage.incident.io/deriv/incidents/fybykprt)

### Inferences
- Rough frequency: across sources, performance/lag/bugs after updates is the most frequent complaint, then chart behaviour, then ecosystem size, then missing order features. Outages are rarely reported as platform-wide events; most are broker-specific or connector-specific.
- The top-voted request (scroll chart further left, scale vertical axis independently) shows how much traders care about basic chart ergonomics; Kronant should get pan/zoom/axis scaling exactly right before adding advanced tools.
- "Lot size changed by itself" and "symbol switched between charts" complaints point to UX risk in linked charts and remembered volumes; Kronant should make volume and symbol state explicit and hard to change by accident.
- Simulated execution lets Kronant show exactly why a fill or SL happened (quote at trigger, slippage model), directly addressing the "fill far from chart price" and "SL did not trigger" complaints.

### Gaps
- No Reddit data (domain blocked for the research tools).
- Spotware does not appear to publish a public, platform-wide status page with incident history; none was found.
- No independent data on cTrader Web load time or resource use was found.

## 5. Which prop firms use cTrader, why, and what about US clients?

### Takeaway
cTrader is the second most common prop platform after MT5 (about a quarter of surveyed firms) and is used by large firms like FTMO, The5ers, FundedNext, FundingPips and Goat Funded Trader. It gained a lot from MetaQuotes' 2024 crackdown, but in Q1 2026 Spotware itself decided to stop onboarding US traders, and firms moved US clients to Match-Trader, TradeLocker or Volumetrica (futures).

### Cited Findings
- Of 53 prop firms compared: MT5 at 23 firms (43%), cTrader at 14 firms (26%); named cTrader firms include FTMO, The5ers, Goat Funded Trader, Funding Pips, Funded Trading Plus, Fintokei - [PropFirmMap, Apr 8 2026](https://propfirmmap.com/blog/prop-firm-platforms-mt5-ctrader-match-trader-compared-2026)
- FundedNext lists challenges in the cTrader Store directory (18 challenges in one example) - [PropFirmApp](https://propfirmapp.com/trading-tools/ctrader)
- FundingRock adopted cTrader; Goat Funded Trader added cTrader - [Finance Magnates, FundingRock](https://www.financemagnates.com/forex/fundingrock-adopts-spotwares-ctrader-as-prop-firm-platform-lineup-expands/); [Finance Magnates, GFT](https://www.financemagnates.com/forex/prop-firm-goat-funded-trader-adds-ctrader-to-its-platform-lineup/)
- FTMO offers cTrader alongside MT4/MT5, availability depends on region - [PropFirmMap, FTMO](https://propfirmmap.com/firms/ftmo)
- One source claims FTMO charges more for cTrader accounts to pass on a platform licence fee - [search summary of TheTrustedProp](https://thetrustedprop.com/blogs/mt4-vs-mt5-vs-ctrader-best-forex-platforms-compared-2026); contradicted by FTMO pricing summaries that show uniform prices across platforms - [PropFirmMap FTMO review 2026](https://propfirmmap.com/blog/ftmo-review-2026-trustpilot-rating-safety-grade-challenge-prices). Unverified.
- Why firms pick it (opinion): high execution transparency with native Level II and explicit slippage visibility vs MetaTrader's "medium" transparency; appeal to scalpers and DoM users - [PropFirmMap](https://propfirmmap.com/blog/prop-firm-platforms-mt5-ctrader-match-trader-compared-2026)
- 2024 context: MetaQuotes cut off prop firms using its platforms without proper licensing ("grey-labelling" via brokers); within a week US clients of prop firms lost access and the industry rushed to Match-Trader and cTrader - [Finance Magnates, Prop-ageddon](https://www.tradingview.com/news/financemagnates:b5d8a05b6094b:0-prop-ageddon-is-your-prop-firm-still-online-real-time-updates/); [Finance Magnates, US restriction](https://www.financemagnates.com/forex/ctrader-restricts-us-prop-firm-access-following-internal-regulatory-assessment/)
- Funding Pips added Match-Trader, cTrader and DXtrade to replace MT4/MT5; Blueberry Markets terminated all MT4/MT5 prop business (2024) - [FX News Group, Funding Pips](https://fxnewsgroup.com/?p=25090); [FX News Group, Blueberry](https://fxnewsgroup.com/forex-news/retail-forex/blueberry-markets-terminates-all-mt4-mt5-prop-firm-business/)
- About 100 prop firms ceased operations between early 2024 and late 2025 (14% contraction), primarily after MetaQuotes' withdrawal - [Finance Magnates, US restriction](https://www.financemagnates.com/forex/ctrader-restricts-us-prop-firm-access-following-internal-regulatory-assessment/)
- The Funded Trader announced approval to offer cTrader to US clients after a compliance review (20 May 2025) - [Finance Magnates](https://www.financemagnates.com/forex/prop-firm-the-funded-trader-announces-approval-to-offer-ctrader-to-us-clients/)
- Spotware statement: "Following an internal regulatory assessment during the first quarter of 2026, we made the strategic decision to restrict the onboarding of US-based traders on the platform." FundedNext: no new US cTrader accounts from 31 Mar 2026, US clients sent to Match-Trader; Goat Funded Trader amended terms in Apr 2026, US platforms now Match-Trader, TradeLocker, Volumetrica; The5ers updated guidelines in Jun 2026, cTrader non-US only. Article published around 1 Jul 2026; cites CFTC enforcement (2023 My Forex Funds case) as background - [Finance Magnates](https://www.financemagnates.com/forex/ctrader-restricts-us-prop-firm-access-following-internal-regulatory-assessment/); [Coinspectator mirror, 2026-07-01](https://coinspectator.com/mainstream/2026/07/01/ctrader-restricts-us-prop-firm-access-following-internal-regulatory-assessment/)
- The5ers launched futures in Feb 2026, open to US traders who cannot access CFD programs - [search summary of Finance Magnates coverage](https://www.financemagnates.com/forex/ctrader-restricts-us-prop-firm-access-following-internal-regulatory-assessment/)

### Inferences
- Platform-vendor risk is a central concern for prop firms: twice in two years (MetaQuotes 2024, Spotware 2026) a vendor's own compliance decision cut off US clients overnight. A firm-controlled or less US-sensitive platform is a selling point, but Kronant must assess its own US exposure before marketing to US traders.
- The firms that left cTrader for US clients went to Match-Trader, TradeLocker and futures platforms, which are Kronant's direct competitors for that segment.

### Gaps
- No full, current list of all prop firms on cTrader was found (the Store lists 40+ firms but the list itself was not retrieved).
- No firm was found that dropped cTrader entirely (only US restrictions).
- The FTMO cTrader surcharge claim could not be verified on FTMO's own site.

## 6. Business model, pricing, white label and back office

### Takeaway
cTrader is free for traders; Spotware licenses servers and white labels to brokers and prop firms, with negotiated, unpublished pricing. Third-party estimates put a white label at about USD 1,000-5,000 setup plus a USD 2,000 monthly minimum, with volume-based fees. Spotware is broadening into a full broker stack (cBridge liquidity bridge, cFinancial liquidity, CRM partnerships, cTrader Leads).

### Cited Findings
- Spotware does not publish prices and prices by negotiation; it sells server licences, and licence holders can buy extra white labels at a USD 2,000 minimum monthly fee each; without a server licence, white labels come via partners (USD 2,000 minimum monthly plus the provider's fee); setup about USD 5,000 (USD 1,000 without separate branded apps); volume-based fee cited as about USD 5 per million traded - [SGHK, cTrader White Label Cost 2026](https://sghk.org/blog/ctrader-white-label-cost/); [Leverate, cTrader vs MetaTrader WL](https://leverate.com/blog/article/ctrader-vs-metatrader-4-5-white-label/) (third-party estimates, exact attribution between the two pages not verified)
- Generic prop startup budget from Spotware's own guide: platform licensing USD 8,000-25,000 upfront, monthly tech stack USD 3,000-8,000, total USD 50,000-200,000; breakeven 4-8 months (industry estimates, not cTrader prices) - [Spotware, How to start a prop firm in 2026](https://www.spotware.com/news/how-to-start-a-proprietary-trading-firm-2026/)
- White label includes fully branded desktop, web, iOS and Android apps, cTrader Automate, cTrader Copy, built-in trading journal, risk management and backtesting - [search summary of B2BROKER / Spotware WL pages](https://www.spotware.com/news/b2broker-launches-ctrader-white-label-prop-trading-solution/)
- White label deployment "1 week"; 50+ liquidity providers via Spotware integrations - [Spotware, How to start a prop firm in 2026](https://www.spotware.com/news/how-to-start-a-proprietary-trading-firm-2026/)
- Back office is "cTrader Admin" (formerly cBroker), with a public changelog (e.g. cTrader Admin 10.0) - [Spotware cBroker public news](https://cbnu.spotware.com/cbroker-public/news/ver:10.0)
- 2025 new products: cBridge (liquidity bridge with no volume fees), cTrader Store growth (sixfold purchases, 700 daily installs), new website; stats: 11M+ traders, 2M new in 2025, 104 new broker/prop clients, 105% growth in live USD volume - [FXStreet, Spotware 2025 review](https://www.fxstreet.com/press-releases/2025-marked-spotwares-shift-beyond-a-single-product-202601261418)
- 2026: cFinancial for executable liquidity (9 Sep 2026); Intivion all-in-one CRM partnership (20 Aug 2026); cBridge + Finmatek regulatory reporting (4 Sep 2026); creator programme with AI focus (3 Sep 2026) - [Spotware news](https://www.spotware.com/news/)
- Platform serves "300+ brokers and prop firms"; prop page says 12M+ traders - [Spotware, Market Replay](https://www.spotware.com/news/ctrader-market-replay/); [Spotware, cTrader for prop firms](https://www.spotware.com/ctrader/prop-firms/)

### Inferences
- cTrader's monthly minimum plus volume fees and separate evaluation-engine/CRM vendors make the total cost for a small prop firm higher and more fragmented than a single bundled product; Kronant's flat package pricing can be positioned against that.
- Spotware's push into liquidity, CRM and lead generation shows it is moving toward an end-to-end broker/prop stack, so the gap Kronant fills today may narrow.

### Gaps
- No official Spotware price list exists; all numbers are third-party estimates.
- No public description found of a prop-specific cTrader licence tier or price.

## 7. Recent product launches and changes (2025-2026)

### Takeaway
Spotware ships frequently: Python algos (Aug 2025), risk-reward sizing tools (2025), a prop challenge directory in the Store (Nov 2025), official MCP servers for AI agents (May 2026), server-side multi-level take profit and break-even (May 2026), Market Replay (Sep 2026), and a reorganised mobile app 5.10 (Sep 2026). In parallel it restricted US onboarding (Q1 2026).

### Cited Findings
- Mar 2025, cTrader 5.1: algo API upgrades (install/compile algos through the API, file dialogs, DropZone, ColorPicker, ProgressBar) - [Spotware, cTrader 5.1](https://www.spotware.com/news/spotware-unveils-ctrader-51)
- May 2025, cTrader 5.2: risk-reward tools, improved price-alert automation, faster launch ("one second" per Finance Magnates headline), Web timeframe-specific object visibility - [Spotware, cTrader 5.2](https://www.spotware.com/news/spotware-unveils-ctrader-5-2-with-advanced-risk-reward-tools/); [Finance Magnates via TradingView](https://ru.tradingview.com/news/financemagnates:54c99adc8094b:0-spotware-cuts-ctrader-launch-time-to-one-second-in-version-5-2)
- 26 Aug 2025, cTrader 5.4: native Python, Linux Docker, Mac risk-reward tool, mobile WebView plugins, new chart time axis, plugin API expansions - [Finance Magnates](https://www.financemagnates.com/forex/spotware-adds-risk-management-and-mobile-upgrades-to-ctrader-54-with-python/)
- 2025 mobile: launch 5x faster including mainland China; Mobile 5.6 equity chart and candle countdown; Mobile 5.7 account ROI chart - [FXStreet, 2025 review](https://www.fxstreet.com/press-releases/2025-marked-spotwares-shift-beyond-a-single-product-202601261418); [FX News Group, Mobile 5.6](https://fxnewsgroup.com/forex-news/platforms/spotware-launches-ctrader-mobile-5-6-with-enhanced-charting-and-equity-visualisation/)
- 27 Nov 2025: prop challenges in the cTrader Store - [Spotware](https://www.spotware.com/news/prop-challenges-in-ctrader-store/)
- Q1 2026: Spotware restricts onboarding of US traders - [Finance Magnates](https://www.financemagnates.com/forex/ctrader-restricts-us-prop-firm-access-following-internal-regulatory-assessment/)
- 14 May 2026: official MCP servers (remote via Web, local via Windows) and skills library - [Finance Magnates](https://www.financemagnates.com/forex/technology/spotware-opens-ctrader-to-ai-agents-as-mcp-wave-catches-up-with-retail-trading/)
- 28 May 2026: server-side advanced take profit (up to 5 levels) and break-even stop loss on all apps - [Spotware](https://www.spotware.com/news/ctrader-brings-server-side-scaling-out/)
- Mobile 5.9 (2026): Charts get their own tab in bottom navigation; focus mode for positions and orders - [LeapRate](https://www.leaprate.com/forex/platforms/ctrader-rolls-out-mobile-5-9-upgrade/)
- Desktop version 5.7.14 dated 14 Jul 2026 (from a download-aggregator site, low reliability) - [UpdateStar](https://ctrader.updatestar.com/)
- 15 Sep 2026: Market Replay (Windows) - [Spotware](https://www.spotware.com/news/ctrader-market-replay/)
- 29 Sep 2026: Mobile 5.10 with Positions app (positions, orders, history tabs), Markets app replacing Trade section (watchlists, symbol overview, alerts), new login with Google, email and Apple ID - [Spotware, Mobile 5.10](https://www.spotware.com/news/ctrader-mobile-5-10/)
- Aug 24 2026 article argues cloud execution of algos removes the need for a VPS - [Spotware news list](https://www.spotware.com/news/)

### Inferences
- Spotware's 2026 direction is AI integration (MCP, AI creator programme), practice tools (Market Replay) and closing order-management gaps (server-side scaling out). Kronant should expect AI-agent access and replay to become table stakes in 2027.
- The mobile app is being reorganised around positions and markets with fewer steps, signalling that earlier mobile navigation was a pain point (consistent with Aug 2026 Trustpilot complaints about a mobile update).

### Gaps
- No primary release notes were found for desktop/web 5.5, 5.6 and 5.7; their feature content is unknown.
- No evidence found of any cTrader feature that shows prop rules natively, even in the 2026 releases.
