# What retail prop traders want and hate in trading platforms: the cross-platform view (MT5, TradeLocker, cTrader, Match-Trader, DXtrade, TopstepX/ProjectX, NinjaTrader, Tradovate, Volumetrica), 2024-2026

Research date: 2026-10-06. Scope: problems and wishes about the trading platform itself, not firm rules or payouts unless the platform is involved. Single-platform deep dives on TradeLocker, cTrader and TopstepX are in sibling notes and are only touched here where they show a cross-platform pattern.

Source conventions: "Primary" = the platform, the firm or a trader's own post. "Press" = trade press (mostly Finance Magnates). "Secondary" = review site, aggregator, vendor or competitor blog. "Snippet" = wording seen only in a search-engine summary, the page itself was not opened, so treat as unverified. "Competitor" = written by a rival firm, so likely biased. Items older than 2025 are flagged "(2024)" or "(older)". Reddit could not be searched or fetched (the search tool refuses reddit.com), and Trustpilot pages were mostly blocked (HTTP 403) or showed no platform-specific reviews, so trader voices come from Finance Magnates reporting, the Tradovate community forum, review sites and search snippets. Frequency estimates ("how common") are my own judgement from the volume and repetition of sources, not measured data.

Our product, for reference: Kronant Trader is a white-label web terminal (FX/CFD/indices/commodities/crypto) with simulated execution, sold with the Kronant Prop back office.

## 1. Which platform problems cause the most anger in prop trading (slippage, spikes, spreads, disputed breaches, disconnects, outages, no compensation)?

### Takeaway
The angriest and best-documented episodes of 2024-2026 are not about slippage, but about platforms being unavailable when a position is open: TopstepX outages (late 2025 to September 2026) where traders could not close trades, and forced migrations when a platform vendor cut firms off (MetaQuotes in February 2024, ProjectX in February 2026, Tradovate for one firm in November 2025). Slippage on stops during news, spread widening and "the wick was not on other charts" disputes are the constant background complaint. They are rarely documented as specific incidents, but they are the root of the general distrust: in simulated execution the firm controls the price, so traders cannot check it.

### Cited Findings

**Outages and being unable to exit (most anger, best documented)**
- Finance Magnates (23 Dec 2025, press): Topstep traders said repeated TopstepX problems stopped them opening and closing positions, some said accounts were "blown up" by it, one trader wrote "This is an every week thing with them". Social media counted "11 confirmed platform issues in the past three months". Topstep wrote "Right now, we are not delivering the Ultimate Trading Experience we promised", and CEO Michael Patak said "in January we will be making things right". Trustpilot 3.6/5, 16% one-star, Topstep answered only 4% of negative reviews, typical reply time two weeks - [Finance Magnates](https://www.financemagnates.com/forex/topstep-faces-prop-traders-wrath-due-to-repeated-outages-ceo-sets-january-deadline-for-a-fix/)
- Competitor claim (Phidias, no sources cited): 11 outages "acknowledged by the company itself" between October and December 2025. Traders were "locked out of funded accounts during active CME trading hours", "Stop-loss and take-profit orders failed to execute", and on 1 Feb 2026 valid exit orders in Micro Gold were rejected with "Outside of trading hours" during active CME hours. Compensation: "evaluation resets only. No funded account reinstatements. No refunds of the activation fee." "No retroactive profit recovery." - [Phidias (competitor)](https://phidiaspropfirm.com/education/topstep-vs-tradeify)
- IBTimes Australia: a new TopstepX outage on Tuesday 23 Sep 2026 from 9:54 a.m. ET with frozen or delayed prices, static charts, locked dashboards and traders unable to close open positions. No official statement at publication time; traders were sent to the status page and Discord, and called the response "unsupportive" - [IBTimes AU](https://www.ibtimes.com.au/topstepx-trading-platform-outage-reports-1875760)
- investingLive (24 Dec 2025) lists TopstepX's "repeated platform outages and trading anomalies" as a red flag when choosing a firm - [investingLive](https://investinglive.com/news/how-to-spot-unfair-prop-firm-practices-before-you-sign-up-20251224/)
- (2024) FTMO's MT4/MT5 mobile servers were offline over the weekend of 18-19 Feb 2024. Traders complained on X. FTMO: "We understand the importance of having access to your trading platform at your fingers" - [Finance Magnates via TradingView](https://vn.tradingview.com/news/financemagnates:019af7769094b:0-prop-trading-firm-ftmo-restores-metatrader-mobile-functionality-but-not-for-all)
- (2024) The Funded Trader said it was dealing with traders "on DXTrade who were 'wrongfully breached'" due to server errors. No count or remedy was published - [Finance Magnates, 21 Aug 2024](https://www.financemagnates.com/forex/prop-trading-the-funded-trader-resurfaces-five-months-after-pausing-operations/)

**Forced platform migrations (platform vendor cuts the firm off)**
- (2024) In February 2024 MetaQuotes pulled MetaTrader from prop firms serving US clients. Firms rushed to cTrader, DXtrade, TradeLocker and Match-Trader. FTMO moved US clients to DXtrade - [Finance Magnates via TradingView](https://www.tradingview.com/news/financemagnates:f8c595249094b:0-prop-trading-firms-renaissance-cutting-us-clients-integrating-new-trading-platforms/); [Finance Magnates](https://www.financemagnates.com/forex/exclusive-ftmo-halts-us-client-acquisition-amid-metaquotes-crackdown/)
- (2024) As of 19 Feb 2024 FTMO's MT4/MT5 servers were closed to all US nationals and residents on desktop, web and mobile - [Finance Magnates via TradingView](https://vn.tradingview.com/news/financemagnates:019af7769094b:0-prop-trading-firm-ftmo-restores-metatrader-mobile-functionality-but-not-for-all)
- (2024) Funding Pips stopped trading on 14 Feb after MetaQuotes made its broker cut ties, then moved everything to Match-Trader within about a week ("Match-Trader is 100% done, everything is migrated"), with history transferred and spreads and commissions kept - [Finance Magnates via TradingView](https://vn.tradingview.com/news/financemagnates:f6deb5f85094b:0-prop-trading-firm-funding-pips-back-online-with-match-trader-migration)
- ProjectX "went fully exclusive to Topstep at the end of February 2026". Bulenox, Tradeify, Lucid, Alpha Futures, Phidias, TradeDay and TickTickTrader lost it and "had to rebuild on Tradovate or Rithmic-based stacks within weeks". Topstep also bought The Futures Desk on 1 Apr 2026 and folded TFD-X into TopstepX - [DamnPropFirms (secondary)](https://damnpropfirms.com/best-prop-firm-trading-platforms/)
- In November 2025 Tradovate suspended access for TickTick Trader's traders. Tradovate said the firm had breached its Evaluation Services Agreement, the firm blamed Tradovate's "operational constraints", and traders were moved to ProjectX - [Affordable Indicators (secondary)](https://affordableindicators.com/ninjatrader-help/ticktick-trader-tradovate-ninjatrader/)

**Slippage, stops during news, spreads and "non-market prices"**
- Firm policies differ on whether slippage that pushes a loss past a limit counts as a breach. FTMO: "stop losses do not guarantee execution at the predefined price", and a breach caused by slippage stands. Axon Funded: "if an allowed risk percentage is exceeded strictly because of market slippage, it is not considered a rule violation" - [Tradeog (secondary)](https://tradeog.com/stop-loss-slippage-prop-firm-violation/)
- (Older, weak data) A blog post on TradingView (19 Aug 2024) cites a "2023" study of 10 US prop firms with 3,000 traders: 92% reported "prices that do not reflect real market conditions", 67% slippage, 73% orders "not being executed as expected, particularly when they were set to generate profits", 52% technical glitches. Methodology, sponsor and sampling are not given and 10 US firms is an odd sample, so use only as a sign of perceived distrust, not as a measure - [TradingView post by Lingrid](https://www.tradingview.com/chart/EURUSD/hKzgFuc1-The-Dark-Side-of-Prop-Trading-Factors-Leading-to-Financial-Loss/)
- Forex Factory (snippet, undated): a trader tested NFP with two brokers, got about 50 pips of slippage on GBP/USD, and a stop meant for -26 pips filled at -100 - [Forex Factory post](https://www.forexfactory.com/thread/post/14057769)
- Anecdotal (Trustpilot reviewer page, snippet): claims that some firms "intentionally widen spreads" and ban people from Discord who ask about spreads or payout delays - [Trustpilot user page](https://no.trustpilot.com/users/6633d183a81c8900129720dd)
- Blacklist site (secondary): traders accused Funded Academy of "unreasonable spread widening, slippage manipulation" - [Vetted Prop Firms](https://vettedpropfirms.com/prop-firm-scams-and-blacklist/)
- Opinion/allegation, unverified: a Medium post claims firms inject a "Ghost Tick" (a millisecond spike) near a trader's limit to trigger a breach. No evidence is given. It shows the kind of suspicion traders hold - [Medium, fxmbrand](https://medium.com/@fxmbrand/the-prop-firm-cabal-exposing-the-hidden-rules-and-funding-ticks-that-keep-98-of-traders-broke-420cdda5a091)
- Context for distrust: the CFTC charged My Forex Funds with taking more than USD 300 million from customers - [Finance Magnates](https://www.financemagnates.com/forex/my-forex-funds-fiasco-pushes-prop-trading-firms-toward-transparency/)

**Rule enforcement through the platform (platform-adjacent)**
- FundingTicks changed rules retroactively in late 2025 (for example a minimum one-minute hold time), so accounts that had passed were breached or had profits cut. Its Trustpilot score fell from 4.1 in October to 3.2, and it wound down in January 2026 - [Finance Magnates](https://www.financemagnates.com/forex/prop-firm-fundingticks-faces-massive-backlash-after-retroactive-rule-change/); [Finance Magnates](https://www.financemagnates.com/forex/following-profit-cuts-and-trading-limits-prop-firm-fundingticks-begins-winding-down/)

### Inferences
- Ranking by anger and how often it shows up (my estimate):
  1. Platform outage or freeze with an open position, then a breach with no reinstatement. Rare per firm, but causes the biggest outbursts and press coverage. TopstepX is the prime 2025-2026 case, and it recurred about nine months after the CEO's "January" promise.
  2. Slippage on stops during news or at the open, which turns a planned loss into a breach. Very common as a complaint (it shows up in nearly every firm's reviews and FAQs), rarely provable.
  3. "Price that never existed" (spike or wick not seen at other brokers or on TradingView). Common as a suspicion. I found no 2024-2026 press case where a firm admitted a bad tick and restored accounts, so it lives mostly in reviews and forums.
  4. Spread widening at rollover and news. Common, often bundled with point 2.
  5. Forced platform migration (MetaQuotes 2024, ProjectX 2026). Rare but affects whole firms at once, and traders lose tools, history and sometimes open positions.
- The common thread is no compensation and no explanation. Traders accept that markets slip; what turns it into anger is a breach with no evidence and no remedy.
- For Kronant: in simulated execution the firm (through us) produces the price and the fill, so we will be blamed for every spike and every outage. The answer is not "better fills" alone but proof: a reference-price check, logged fills and a published outage policy.
- Platform vendors cutting firms off (MetaQuotes, ProjectX, Tradovate) is a firm-side trust issue. A white-label vendor that promises not to compete with or cut off its firms, and that exports accounts and history, has a selling point.

### Gaps
- I found no specific, sourced 2024-2026 case of a forex/CFD prop firm admitting a bad price spike and reinstating accounts. Such cases may exist on Discord or X but were not findable with the tools available.
- Reddit (r/propfirm, r/Forex, r/FuturesTrading, r/Topstep) was blocked for this research, so frequency estimates rest on press and review sites. A manual Reddit pass is worth doing.
- A search summary said 80-100 prop firms closed by end-2024 "according to Finance Magnates Intelligence", but I could not tie it to an opened page.
- Details of the CFTC complaint against My Forex Funds (what it alleged about price or fill manipulation) and the later course of the case were not checked in this session.
- Whether Topstep actually compensated anyone in January 2026 as promised is not documented in the sources found.

## 2. How do traders verify that a breach was fair, which platforms or firms offer transparency tools, and what do traders ask for?

### Takeaway
Mostly they cannot. Traders compare their chart with TradingView or another broker, take screenshots and export trade history, then argue with support. Only a few firms publish anything: Hola Prime's tick-by-tick comparison against TradingView (2024) and FundingTraders' "Trade Clarity Desk" showing which rule fired (August 2026). No platform I found gives the trader a per-breach evidence pack (triggering tick, reference price, order timeline). That is the clearest open space.

### Cited Findings
- Finance Magnates on the MFF fallout (snippet): a firm said "We cannot really show trade-by-trade ticketing information of how we go into the market as traders might expect to see". Industry figures said conflict-of-interest policies, transparency policies and audits would be welcome - [Finance Magnates](https://www.financemagnates.com/forex/my-forex-funds-fiasco-pushes-prop-trading-firms-toward-transparency/)
- (2024) Hola Prime publishes a "Price Transparency Report", "a comprehensive tick-by-tick data report, comparing the price ticks on their trading platforms with the market prices advertised on TradingView". Its CFO called it "the first and only prop firm to provide this level of price transparency". The release gives no method, frequency or sample - [FXStreet press release, Nov 2024](https://www.fxstreet.com/amp/press-releases/hola-prime-sets-new-industry-standard-as-the-worlds-leading-transparent-prop-trading-firm-202411201405)
- FundingTraders launched the "Trade Clarity Desk" on 25 Aug 2026: a dashboard showing "Your rule violations in real time" and "The exact rule that was triggered", labelled as warning, deduction or reduced profit split, before a payout request. It is not clear whether the numbers are binding. Eightcap had named profit-distribution rules as a big source of disputes - [Finance Magnates](https://www.financemagnates.com/forex/fundingtraders-shows-rule-breaches-before-prop-payout-requests/)
- investingLive's advice (Dec 2025) on documenting problems: timestamped screenshots, record rejections and error messages, export fills, order history and statements, save firm announcements (Discord, status page, email), and get written confirmation from support. It does not mention tick data or independent price checks - [investingLive](https://investinglive.com/news/how-to-spot-unfair-prop-firm-practices-before-you-sign-up-20251224/)
- Topstep answered only 4% of negative Trustpilot reviews, typically after two weeks - [Finance Magnates](https://www.financemagnates.com/forex/topstep-faces-prop-traders-wrath-due-to-repeated-outages-ceo-sets-january-deadline-for-a-fix/)
- The TopstepX outage of 23 Sep 2026 had no public statement at the time of the article, and traders were sent to a status page and Discord - [IBTimes AU](https://www.ibtimes.com.au/topstepx-trading-platform-outage-reports-1875760)
- A secondary guide (snippet) says that when a firm does not disclose how it sources prices, "spreads, fill prices, and slippage are generated within the firm's own infrastructure, with no external reference against which the trader can independently verify what they received" - search summary of the liquidity-provider results, page not opened, so unverified - [Turnkey Inside (likely source)](https://www.turnkeyinside.com/liquidity-providers-for-prop-firms/)
- FTMO marks news blackouts as "Restricted event" in its economic calendar, which lets a trader check after the fact whether a closed trade fell in the 2-minute window - [FTMO FAQ](https://ftmo.com/faq/can-i-trade-news/)

### Inferences
- What traders ask for, pieced together from the complaints (inference, not a survey): (a) proof that the triggering price existed, against an outside reference; (b) the exact rule and number that triggered the breach, at the moment it happens; (c) the order timeline (sent, received, filled, latency) to settle "my stop was hit late" disputes; (d) an admitted outage log and a known compensation rule; (e) answers from a human within hours, not weeks.
- Kronant Trader can do (a)-(c) natively because we own the price stream, the fill engine and the rule engine. A "breach report" (the tick that triggered, bid/ask, a second reference source, spread at that moment, equity at each step, order event times) downloadable by both trader and firm would be unusual in the market. Hola Prime's periodic report and FundingTraders' rule dashboard each cover only part of it.
- A public status page with incident history per tenant, plus automatic tagging of accounts that had open positions during an incident, would give firms a fair, fast basis for reinstatement and would directly address the TopstepX pattern.

### Gaps
- I found no platform vendor (MT5, cTrader, Match-Trader, DXtrade, TradeLocker, Volumetrica) marketing a trader-facing breach or execution report. Their own documentation was not checked in depth here (sibling notes cover three of them).
- No data on how many breach disputes firms get or how often they reverse them.
- No source checked whether Hola Prime's report is still published in 2026 or what it looks like.

## 3. Which prop-specific features do traders value inside the terminal?

### Takeaway
Traders want the rules visible and enforced in the terminal: live daily loss and max drawdown meters, distance-to-breach warnings, position sizing based on remaining room, and self-set lockouts that cannot be undone while tilted. Match-Trader (prop widget on login) and TopstepX (personal daily loss limit, profit-target lockout, trade limits, cooldowns) are the references. A large ecosystem of third-party calculators, trackers, journals and Chrome extensions shows that most terminals still do not meet this need.

### Cited Findings
- Match-Trader shows a prop widget on login with profit target, max daily loss and max loss for the current phase. It also has account statistics, multi-account management, in-platform challenge purchase, competitions and leaderboards, and add-ons (profit split increase, drawdown limit increase, fewer minimum days) - [PropFirmApp review (secondary)](https://propfirmapp.com/trading-tools/match-trader)
- A firm blog (snippet) says Match-Trader tracks daily drawdown against the 5% ceiling "with alerts at 50% and 80%" - [For Real Funding (secondary, snippet)](https://forrealfunding.com/blogs/matchtrader-prop-firm-platform-guide)
- TopstepX has a Personal Daily Loss Limit (a self-set cap below the account rule that locks you out), a Personal Daily Profit Target lockout, daily and weekly trade limits, and enforced cooldown lockouts (snippets) - [Prop Trading Vibes (secondary)](https://proptradingvibes.com/blog/topstep-trading-platforms); [Topstep blog](https://topstepbrokerage.com/blog/topstepx-futures-trading-platform-funded-traders)
- TopstepX enforces daily loss, trailing drawdown and consistency at platform level. Tradovate has a "Manual Lockout button - One-click session kill switch" - [DamnPropFirms (secondary)](https://damnpropfirms.com/best-prop-firm-trading-platforms/)
- Tradovate community, 21 Feb 2026 (primary): a trader asks for "max # of trades per day", automatic lockout at loss or profit limits, and for the "Lock risk settings if trading locked" option to work on prop accounts, not only live ones. They argue a manual lockout button helps little because the trader "could be 'tilted'", and say Topstep's risk settings "actually feels like the platform understands traders and dealing with tilt, revenge trading" - [Tradovate community](https://community.tradovate.com/t/better-risk-settings-feature-parity-please/12529.md)
- Tradovate community, 4 Nov 2025 (primary): request for a time-based lockout independent of P&L, to stop revenge trading and avoid bad sessions. Reply: "A lock out with no ability to change mid session is a fix. Anything else is a guideline not a lock." Similar requests were posted between March 2025 and September 2026 - [Tradovate community](https://community.tradovate.com/t/personal-lockout-request-for-tradovate-ninja-trader/12387)
- FTMO offers Account MetriX (objectives and statistics), a Trading Journal, and an economic calendar marking "Restricted event" news (2 minutes before and after, funded Standard accounts only) - [FTMO tools](https://ftmo.com/en/trading-apps/); [FTMO FAQ](https://ftmo.com/faq/can-i-trade-news/)
- Third-party tools built to fill the gap: PropTracker ("daily and overall drawdown guardrails with warnings before breaching") - [PropTracker](https://proptrackerpro.lovable.app/); a Google Play "Forex Prop Firm Planner" with daily loss tracker, max drawdown monitor and journal - [Google Play](https://play.google.com/store/apps/details?id=com.appnovasi.prop_firm_planner&hl=en_US); an MT5 lot-size EA with a "prop firm daily limit barrier" that locks execution - [ForexBroker500](https://forexbroker500.com/mt5-lot-size-calculator-indicator-free-download/); drawdown calculators for trailing, static and EOD drawdown - [PropScope](https://propscope.net/en/drawdown-calculator/), [OneTradeJournal](https://onetradejournal.com/tools/prop-firm-drawdown-calculator); a "Propfirm Risk Manager" Chrome extension - [ChromeBoard](https://www.chromeboard.com/extension/pflock-propfirm-risk-mana-lkgjadglmehpllkkbojneckgcccknjpo)
- Journals that connect to many prop platforms: Consistry connects to MetaTrader, cTrader, TopstepX, DXtrade, TradeLocker, Tradovate and NinjaTrader (snippet) - [PropFirmMap](https://propfirmmap.com/blog/match-trader-prop-firm-tools-that-connect-2026)
- A tool sold to disable trading around news ("RunwiseFX", snippet) - [search result](https://ddg.wcroc.umn.edu/runwisefx-disable-trading-during-news)

### Inferences
- Must-haves for a 2026 prop terminal (my ranking by how often they appear and how much traders value them):
  1. Always-visible meters for daily loss and max drawdown, in money and percent, with the exact breach level and distance to it. Table stakes, Match-Trader set the bar.
  2. Pre-trade risk check: "this stop risks X of your Y remaining daily room", and position sizing from risk % or money. Currently solved with external calculators and EAs.
  3. Warnings at set thresholds (for example 50% and 80% of the limit), on screen and as push.
  4. Self-set lockouts that cannot be lifted until the next session: personal daily loss, profit-target lock, max trades per day, time-out. TopstepX wins praise for this, and Tradovate users ask for parity.
  5. News blackout windows drawn on the chart and blocked in the order ticket, when the firm has a news rule. Today this is a separate calendar page (FTMO).
  6. Consistency rule tracker (best day share of total profit) shown live. Sources mention TopstepX enforcing it but I found no detail on trader-facing tracking.
  7. Built-in journal and statistics, so traders do not need Tradezella-type tools. Nice to have, as many traders use external journals anyway.
- Kronant Prop holds the rules and Kronant Trader holds the prices, so we can compute all of this server-side and identically in terminal, back office and breach report. That consistency (the meter shows exactly what the rule engine will enforce) is itself a trust feature.

### Gaps
- No quantitative survey of which in-terminal features prop traders rank highest. The ranking above is inferred.
- How Match-Trader, DXtrade, cTrader and TradeLocker handle consistency rules and news windows in the terminal was not verified here.

## 4. Which features do traders miss when moving from MT4/MT5 to newer platforms?

### Takeaway
The big loss is automation and the ecosystem: Expert Advisors, custom indicators and scripts, terminal-based trade copiers and risk tools did not survive the move, and the new platforms each have their own API. MT5 still has the most prop firms (43% of 53 firms in April 2026) mainly for that reason, even though its interface is seen as dated.

### Cited Findings
- After the February 2024 exodus, "the old tooling was built inside the MetaTrader terminal - Expert Advisors, terminal-resident copiers, plugin bridges", and none of it moved. New tools had to be written against four different APIs while many firms also restricted automation on the new platforms, which "thinned the commercial incentive" for developers. The new ecosystem is "younger, narrower, and often single-feature". APIs: TradeLocker REST via cloud bridges, cTrader Open API (OAuth), DXtrade REST with session tokens, Match-Trader Platform API with credential auth - [QuantCrawler (secondary)](https://quantcrawler.com/learn/post-metatrader-prop-stack)
- PropFirmMap (8 Apr 2026, 53 firms): MT5 at 23 firms (43%), Match-Trader 16 (30%), cTrader 14 (26%), MT4 11 (21%), DXtrade 10 (19%), TradeLocker 8 (15%). MT5's "Interface feels dated" but it has the strongest EA ecosystem and indicator libraries. Match-Trader has "limited EA/bot support" and fewer advanced order types. EA traders prefer MT5 or cTrader - [PropFirmMap](https://propfirmmap.com/blog/prop-firm-platforms-mt5-ctrader-match-trader-compared-2026)
- Search summary (snippet): firms switching to DXtrade forced traders to move custom bots and indicators, which is "time-consuming and costly" - search result, source page (PropFirmMatch blog) returned 403 - [PropFirmMatch](https://propfirmmatch.com/blog/best-forex-trading-platform-for-prop-firms)
- Trade copiers: firms generally ban copying someone else's trades but most allow copying between your own accounts (snippet) - [FundedTrading](https://fundedtrading.com/best-trade-copier-for-forex-prop-traders); of 9 verified tools only 3 support Match-Trader (Consistry, PickMyTrade, Traders Connect) (snippet) - [PropFirmMap](https://propfirmmap.com/blog/match-trader-prop-firm-tools-that-connect-2026); Local Trade Copier added MT4-to-DXtrade copying - [Barchart](https://www.barchart.com/story/news/28936448/local-trade-copier-adds-dxtrade-integration-enabling-traders-to-copy-trades-from-mt4-to-dxtrade-effortlessly)
- Webhook bridges from TradingView alerts to TradeLocker and other platforms are sold as products (Copygram, PickMyTrade), showing demand for automation that the platforms do not provide natively - [Copygram](https://copygram.app/tradingview-to-tradelocker); [PickMyTrade](https://pickmytrade.io/broker/tradingview-to-tradelocker/)
- cTrader is placed by reviewers as the platform for algo traders (cBots, FIX, under 100 ms claimed) (snippet) - [FundedTrading](https://fundedtrading.com/best-metatrader-alternative/)

### Inferences
- What MT4/MT5 users miss most (my estimate, by frequency): (1) EAs and automation; (2) their own custom indicators; (3) trade copiers across accounts; (4) multiple charts and saved templates or workspaces; (5) hotkeys and one-click trading habits; (6) a familiar desktop terminal. Points 4 and 5 are common in general platform reviews but I found no prop-specific source naming them, see Gaps.
- Most prop firms restrict EAs anyway (HFT, latency arbitrage, copy trading between people), so for a sim-only web terminal the realistic answer is a clean, documented API and webhook entry (TradingView alerts in, trade events out) with firm-level controls, not an EA runtime.
- Account and history export matters: traders and firms who lived through the MetaQuotes and ProjectX cut-offs value being able to move.

### Gaps
- No prop-specific source found on hotkeys, multi-chart layouts or templates as a migration pain. Reddit would be the place to check.
- A claim that TradeLocker has no strategy tester or replay and that "TradeLocker Studio" was not available in early 2026 appeared only in a search summary without a clear source; left out (sibling TradeLocker note should confirm).

## 5. What do traders say about TradingView integration (trading from TradingView vs built-in TradingView charts)?

### Takeaway
TradingView is the reference that traders trust and the charting they already know. On forex/CFD prop, the norm is TradingView charts embedded inside the platform (TradeLocker, Match-Trader). On futures prop, the norm is trading from TradingView itself via a Tradovate bridge. Traders use TradingView prices to judge whether their firm's prices were fair, and they want their own TradingView layouts and alert automation, which embedded charts only partly give.

### Cited Findings
- TradeLocker and Match-Trader both run TradingView charts inside the platform, so traders keep the same charts without paying for a TradingView plan (vendor, snippet) - [TradeLocker](https://tradelocker.com/forex-trading/tradelocker-vs-match-trader/)
- Match-Trader includes TradingView charts "at no additional cost" - [PropFirmApp](https://propfirmapp.com/trading-tools/match-trader)
- TradingView is offered at 12 of 53 prop firms (23%) as a trading venue, and chart-focused traders choose TradingView or cTrader - [PropFirmMap](https://propfirmmap.com/blog/prop-firm-platforms-mt5-ctrader-match-trader-compared-2026)
- For futures prop, most firms that support TradingView route orders through Tradovate: the trader enters Tradovate credentials in TradingView's Trading Panel. Not all allow direct order routing, and some allow charting only (snippet) - [GOAT Funded Trader](https://www.goatfundedtrader.com/blog/futures-prop-firms-that-use-tradingview); [MyFundedFutures](https://myfundedfutures.com/blog/best-prop-firms-tradingview-integration)
- Tradovate is valued for TradingView integration and for managing several firms' accounts - [DamnPropFirms](https://damnpropfirms.com/best-prop-firm-trading-platforms/)
- A blog cites "PropFirmMatch" data for "a 40% surge in firms integrating TradingView" (snippet, no year or method) - [PickMyTrade blog](https://blog.pickmytrade.trade/top-prop-firms-using-tradingview-2025/)
- Hola Prime chose TradingView prices as the benchmark for its tick-by-tick transparency report - [FXStreet](https://www.fxstreet.com/amp/press-releases/hola-prime-sets-new-industry-standard-as-the-worlds-leading-transparent-prop-trading-firm-202411201405)

### Inferences
- Two different needs: (a) "I want the TradingView look and tools in my terminal" (met by embedding TradingView's charting library, as TradeLocker and Match-Trader do); (b) "I want to trade from my own TradingView account with my layouts, Pine indicators and alerts" (only met by a TradingView broker integration or webhooks). Serious chart traders want (b); casual traders are fine with (a).
- A TradingView broker integration is a heavy, gated project. A practical middle path for Kronant is TradingView-style charts in the terminal plus a webhook endpoint for TradingView alerts, so traders keep their Pine alerts.
- Because traders use TradingView as the "truth", showing a TradingView-comparable reference price next to our own price on a breach report would directly answer the "fake wick" suspicion.

### Gaps
- No direct trader quotes found on the embedded-vs-external TradingView trade-off (Reddit blocked).
- Not checked: whether embedded TradingView charts in TradeLocker or Match-Trader allow custom Pine scripts. Sources conflict in summaries and none was opened.

## 6. What do traders expect from mobile trading?

### Takeaway
Mobile is mainly a safety channel: check meters and close or adjust positions when away from the desk or when the main platform fails. Expectations: full position and order management, the same rule meters as desktop, and reliability. Match-Trader and Tradovate are seen as strong on mobile, and complaints focus on missing or separate apps and outages.

### Cited Findings
- Mobile traders favour Match-Trader or Tradovate - [PropFirmMap](https://propfirmmap.com/blog/prop-firm-platforms-mt5-ctrader-match-trader-compared-2026)
- Match-Trader has no single app: each firm or broker has its own branded app, and the store apps only reach Match-Trader's demo server, which confuses traders. The platform runs as a PWA on web, desktop shortcut and mobile - [PropFirmApp](https://propfirmapp.com/trading-tools/match-trader)
- An operator-facing comparison says TradeLocker "wins on chart experience and mobile" - [Track360 (vendor, secondary)](https://track360.io/blog/prop-firm-trading-platform-comparison-dxtrade-ctrader-match-trader-2026)
- (2024) FTMO's MT4/MT5 mobile servers went offline for a weekend, and traders complained on X - [Finance Magnates via TradingView](https://vn.tradingview.com/news/financemagnates:019af7769094b:0-prop-trading-firm-ftmo-restores-metatrader-mobile-functionality-but-not-for-all)
- Tradovate's prop blog frames mobile as needed to "monitor positions and act quickly" to stay within daily loss caps (vendor) - [Tradovate Prop](https://prop.tradovate.com/blogs/mobile-trading-platform-prop-traders)
- NinjaTrader Prop markets one consistent experience across mobile, web and desktop - [NinjaTrader Prop](https://prop.ninjatrader.com/platform/mobile-trading/)
- App-store summary (snippet): Rithmic Trader Pro's app is described as hard to use but kept as an emergency backup to close trades - [App Store](https://apps.apple.com/us/app/rithmic-trader-pro/id1600330588?uo=4)
- TopstepX outages included inability to close positions and frozen charts, the case where a second channel matters - [IBTimes AU](https://www.ibtimes.com.au/topstepx-trading-platform-outage-reports-1875760)

### Inferences
- For a web terminal, a good responsive PWA under the firm's brand covers most needs without app-store work per firm (which Match-Trader is criticised for). The must-haves on mobile: rule meters, open positions with one-tap close, modify stop and target, close all, push alerts at drawdown thresholds and on breach.
- "Close all" and protective actions should work even when the full chart view is degraded, since mobile is the fallback when things go wrong.

### Gaps
- A widely repeated "72% positive feedback for mobile usability on r/Daytrading" for TradeLocker appeared only in a search summary with no traceable source; treat as unverified.
- No survey found on how much of prop trading happens on phones.

## 7. Comparisons and rankings of prop platforms: what wins and why?

### Takeaway
No platform wins on everything. MT5 still leads in firm count (43%) because of EAs and familiarity. Match-Trader is the fastest-growing and leads on built-in prop tooling and turnkey launch. cTrader wins on execution, order types and algo traders. TradeLocker wins on TradingView charts, modern UX and mobile. DXtrade wins with firms on rule flexibility. On futures, TopstepX wins on built-in risk discipline but loses on reliability, Tradovate on TradingView and multi-firm access, Rithmic on latency.

### Cited Findings
- PropFirmMap (8 Apr 2026, 53 firms): MT5 43%, Match-Trader 30%, cTrader 26%, NinjaTrader 23%, Tradovate 23%, TradingView 23%, MT4 21%, DXtrade 19%, Rithmic 19%, TradeLocker 15%. Hola Prime offers six platforms (DXtrade, cTrader, Match-Trader, MT5, MT4, TradeLocker). Trader fit: EA traders MT5 or cTrader, scalpers Rithmic or cTrader, mobile Match-Trader or Tradovate, chart traders TradingView or cTrader - [PropFirmMap](https://propfirmmap.com/blog/prop-firm-platforms-mt5-ctrader-match-trader-compared-2026)
- Track360 (2026, operator view): "Match-Trader and DXtrade lead because they ship with prop-specific tooling and clean APIs for enforcing challenge rules". cTrader "wins on trader experience and execution model but asks for more integration work". TradeLocker "wins on chart experience and mobile" - [Track360](https://track360.io/blog/prop-firm-trading-platform-comparison-dxtrade-ctrader-match-trader-2026)
- Match-Trader was used by over 60 prop firms as of early 2026 - [PropFirmApp](https://propfirmapp.com/trading-tools/match-trader)
- cTrader was named Best Trading Platform for Brokers 2025 by Finance Magnates and is supported by more than 250 brokers and prop firms (snippet) - [NYC Servers](https://newyorkcityservers.com/blog/best-ctrader-prop-firms-2026)
- (2024) Top Tier Trader's CEO evaluated DXtrade, cTrader and others and picked TradeLocker as the best MT4/MT5 alternative (snippet) - [Finance Magnates via TradingView](https://vn.tradingview.com/news/financemagnates:53082226b094b:0-prop-trading-firm-top-tier-trader-ditches-metatrader-for-lesser-known-tradelocker)
- A review summary (snippet) puts TradeLocker execution at 200-300 ms and cTrader under 100 ms via FIX; method unknown - [FundedTrading](https://fundedtrading.com/best-metatrader-alternative/)
- Futures: TopstepX has "Platform-enforced risk rules"; Tradovate offers multi-firm management, the manual lockout and TradingView integration; Rithmic offers low latency and many front ends (R|Trader Pro, Quantower, NinjaTrader, Sierra Chart, TradeSea) - [DamnPropFirms](https://damnpropfirms.com/best-prop-firm-trading-platforms/)
- Volumetrica: VolSys focuses on order flow (heat maps, footprint, volume profile) on DXFeed data; the vendor says more than 20 futures prop firms use its dashboards or platforms (snippets) - [AMP Futures](https://www.ampfutures.com/reviews/volumetrica-volsys); [Trading Funder](https://tradingfunder.com/platforms/volumetrica/)
- Kraken bought NinjaTrader (and Tradovate) for USD 1.5 billion, announced March 2025, closed around May 2025, so one vendor change or outage hits every firm on that stack (snippet) - [Wikipedia: NinjaTrader](https://en.wikipedia.org/wiki/NinjaTrader)

### Inferences
- What wins, across reviews: (1) the trader can see and trust the rules (Match-Trader widget, TopstepX lockouts); (2) charts they know (TradingView); (3) execution that feels instant and is stable under load; (4) automation for the minority who need it (MT5, cTrader); (5) mobile. Reliability is not a differentiator when it works, but it is the top reason platforms lose trust when it fails (TopstepX).
- Most comparison sites are affiliate or vendor-run, so "rankings" mostly reflect firm count and marketing. Firm count (MT5 43%) is not the same as trader preference.
- Kronant's open space: combine Match-Trader's prop-native dashboard, TopstepX-style self-lockouts, TradingView-style charts and mobile, and add what no one offers: verifiable prices and fills (breach evidence pack, reference price, outage log). Do not try to match MT5's EA ecosystem; offer an API and webhooks instead.

### Gaps
- No independent trader survey ranking these platforms by satisfaction was found. Rankings above come from firm counts and affiliate or vendor reviews.
- No reliable trader-side complaints were found for Volumetrica or DXtrade specifically (beyond the 2024 TFT DXtrade breaches); Reddit would likely add detail.
