# Cost floor: live FX and gold data with display rights, and hosting, per active trader account (2026)

Research date: 2026-10-04. Vendor pages were fetched live on that date unless another date is given. "Published" = price on the vendor's own page. "Secondary" = aggregator, review site or search snippet, not the vendor. "Contact sales" = no public price found. Older-than-2025 sources are flagged.

## 1. What do FX/gold data vendors charge for real-time data with the right to display it to end users?

### Takeaway
Only two vendors publish a price that explicitly includes external display/redistribution of real-time FX: Tiingo (Redistribution Startup USD 250/mo, Enterprise USD 500/mo) and Twelve Data (Business "Venture" USD 499/mo, or USD 414/mo billed annually, "External display"). Both are flat monthly fees with no published per-end-user fee, so at 1,000+ active accounts the data cost is cents per account. Most other retail-API vendors (TraderMade, Finage, Massive, fastFOREX, Twelve Data individual plans, Tiingo standard plans) forbid display to third parties on their published plans and require a custom licence; institutional vendors (Bloomberg, LSEG, ICE, dxFeed, Barchart, Xignite/QUODD, Massive business currencies) are contact-sales.

### Cited Findings

**Tiingo (published)**
- Standard plans are internal use only: Starter USD 0, Power USD 30/mo (USD 300/yr), Business "Internal Commercial Use" USD 50/mo (USD 499/yr). Internal use is defined as: "you may only use the data for your own personal use and you may not display or share the data with another person or organization." The pricing page says redistribution pricing is "explicit and predictable on our Product pages"; enterprise/bespoke via sales - [Tiingo pricing](https://www.tiingo.com/about/pricing)
- Forex API product page: 140+ pairs plus gold, silver, platinum; intraday history from January 2020; REST and WebSocket; "aggregated bid/ask feed" that "updates to the second" - [Tiingo Forex API](https://www.tiingo.com/products/forex-api)
- Same page lists Redistribution plans for business entities: Startup USD 250/mo (80,000 daily requests, 1 TB monthly bandwidth) and Enterprise USD 500/mo (1,200,000 daily requests, 1 TB bandwidth); the table notes they include "End-of-Day Prices and IEX". The standard Commercial plan (USD 50/mo) is still "Internal Use Only" - [Tiingo Forex API](https://www.tiingo.com/products/forex-api)
- No per-end-user fee is mentioned anywhere on the pricing page - [Tiingo pricing](https://www.tiingo.com/about/pricing)

**Twelve Data (published)**
- Individual plans (Basic free, Grow USD 29, Pro USD 99, Ultra USD 329/mo; 17% off annual) are for "personal, internal, and non-commercial purposes". Real-time forex on all tiers; commodities from Grow; WebSocket credits: Pro 500, Ultra 2,500 - [Twelve Data pricing](https://twelvedata.com/pricing)
- Business plans: Venture USD 499/mo monthly or USD 414/mo billed annually (about USD 4,990/yr), includes "External display data access"; Enterprise USD 1,099/mo or USD 916/mo annual (USD 10,992/yr), adds "External distribution market data" and 99.99% SLA; Enterprise+ custom with "White-labeling data access". One WS credit is consumed per subscribed symbol (e.g. EUR/USD) - [Twelve Data business pricing](https://twelvedata.com/pricing-business)
- Conflict: two fetches of the same page rendered Venture's credits differently (610 API + 500 WS vs. 2,584 API + 2,500 WS monthly / 1,597 + 1,500 annual) and one fetch showed a different Venture price; the USD 499 / 414 figures came from the more specific read. Verify on the page before quoting - [Twelve Data business pricing](https://twelvedata.com/pricing-business)
- No per-user fee, end-user seat cap or redistribution quantity limit is stated on the business page - [Twelve Data business pricing](https://twelvedata.com/pricing-business)

**Live-Rates.com (published, rights unclear)**
- EUR 50/mo monthly, EUR 30/mo billed yearly (EUR 360), EUR 20/mo on 3-year (EUR 720). 150+ instruments, REST and WebSocket, rates update "approximately every second", 1 whitelisted IP (monthly) to 5 (3-year), history only on yearly/3-year. Paid plans "allow commercial use with unlimited requests"; site markets itself to "brokers & fintech" and "apps & games" - [Live-Rates](https://www.live-rates.com/)
- The terms page (/terms) returned 404, so explicit display/redistribution wording could not be verified - [Live-Rates terms (404)](https://www.live-rates.com/terms)

**TraderMade (published price, display needs permission)**
- FX & Crypto plan GBP 599/mo (all FX and crypto pairs, REST, WebSocket, tick history, business use); CFD plan GBP 599/mo (40 CFD symbols); Enterprise custom with WebSocket/FIX/REST, "white-labeling rights", 99.99% SLA - [TraderMade pricing](https://tradermade.com/pricing)
- Terms: "Fair Business Use" means rates "are used for internal purposes or internal applications"; "If you require to display our rates or want to give access to our data you will need a prior permission from TraderMade"; breach lets TraderMade "claim reasonable remuneration". No last-updated date - [TraderMade terms](https://marketdata.tradermade.com/termsandconditions)

**Finage (published price, redistribution prohibited by default)**
- Real-Time Forex Data Feed "starting at" USD 599/mo; Forex & Crypto bundle from USD 949/mo; 3,500+ pairs incl. metals; WebSocket - [Finage Forex](https://finage.co.uk/product/forex)
- Site disclaimer: "Redistribution of the information displayed on or provided by Finage is strictly prohibited." - [Finage pricing](https://finage.co.uk/pricing)
- A search snippet described Basic USD 399 / Professional USD 599 / Enterprise USD 999 tiers with 250/200/100 ms delay; not visible on the fetched page, treat as unverified - [Finage Forex](https://finage.co.uk/product/forex)

**Massive (formerly Polygon.io) (published individual, business contact sales)**
- Currencies Basic USD 0 (end of day, 5 calls/min); Currencies Starter USD 49/mo (real-time, WebSockets, 10+ yrs history). Both "Individual use only" - [Massive pricing](https://massive.com/pricing?product=currencies)
- Business page shows no currencies price; only Stocks Business USD 2,499/mo is visible; currencies/enterprise via "Talk to sales" - [Massive business](https://massive.com/business)
- Massive says it connects "directly to institutional-level banks and liquidity providers" for FX - [Massive knowledge base](https://massive.com/knowledge-base/categories/forex)

**OANDA (published floor, terms not verified)**
- Exchange Rates API "plans start from $4,850 per year" (about USD 404/mo); real-time via REST or FIX, streaming bid/ask/mid, 38,000+ pairs; display/redistribution terms not stated on the page - [OANDA Exchange Rates API](https://www.oanda.com/foreign-exchange-data-services/en/exchange-rates-api/)
- The v20 trading API with streaming prices is free to fxTrade account holders (cost via spreads), but this is a trading API, not a redistribution licence (secondary) - [CurrencyFreaks blog, June 2026](https://currencyfreaks.com/blog/Currency-Conversion-Api-Oanda-Pricing)

**FXCM (contact sales)**
- Offers raw FX prices "sourced directly from major interbank and non-bank market makers"; "Most CFDs are available for redistribution. There are no connectivity fees and significant discounts are available if you subscribe to our data and execute trades." Prices not published; "entry-level data solutions for free" via premiumdata@fxcm.com - [FXCM market data](https://www.fxcm.com/za/algorithmic-trading/market-data/)

**Barchart (secondary / contact sales)**
- OnDemand APIs "start at $500/month", sold as packages or custom enterprise agreements; redistribution fees apply to firms passing data to others (secondary) - [FitGap: Barchart OnDemand](https://us.fitgap.com/products/barchart-ondemand); product page [Barchart OnDemand](https://www.barchart.com/solutions/services/ondemand)

**Bloomberg B-PIPE (secondary, scale reference only)**
- Contract-negotiated by fields, exchanges, redistribution rights and consuming applications; B-PIPE/SAPI licensing quoted at USD 50,000-200,000+/yr (aggregator estimate) - [CostBench](https://costbench.com/software/financial-data-terminals/bloomberg-terminal/hidden-costs)
- QuantConnect's hosted B-PIPE ticker plant is USD 2,000 setup + USD 240/mo, with Bloomberg licensing/entitlement fees separate - [QuantConnect B-PIPE](https://www.quantconnect.com/docs/v2/cloud-platform/datasets/bloomberg-bpipe)

**Others**
- Xignite (now under QUODD): 170+ currencies, 29,000+ pairs, "flexible pricing" per asset class, 7-day trial; no display price found - [Xignite/QUODD forex API](https://website.xignite.com/forex-rate-api-real-time-and-historical-foreign-currency-exchange-rates)
- dxFeed: individual access "starts at $19/month" (secondary, retail); broker/redistribution pricing not public - [PropFirmApp dxFeed review](https://propfirmapp.com/?p=3874)
- fastFOREX (Jan 2026 plans: One/Extra/Premium, 1 s midpoint and real-time bid/ask on Premium, WebSocket add-on; real-time metals announced Aug 2026) states "You must not redistribute information displayed on or provided by fastFOREX" - [fastFOREX 2026 plans](https://www.fastforex.io/hub/2026-new-plans-new-features-new-data)
- Alpaca: provides only US equities, options and crypto data; XAUUSD and spot FX are not available and forex "isn't on Alpaca's immediate roadmap" (forum posts, older) - [Alpaca forum: XAUUSD](https://forum.alpaca.markets/t/cannot-find-data-for-cfds-xauusd/11594); [Alpaca forum: forex](https://forum.alpaca.markets/t/support-forex-trading/6727)
- LSEG/Refinitiv, ICE Data Services: no public FX display/redistribution prices found (search only surfaced exchange fee schedules for other asset classes) - [search context: ICE Fee Overview](https://www.ice.com/publicdocs/Fee_Overview.pdf)
- General market level: a legal guide (updated Dec 2025) puts internal-use data API licences at USD 500-5,000/mo and customer-facing at USD 2,000-25,000/mo ("5-50x markup") - [terms.law](https://terms.law/Trading-Legal/guides/api-license-trading-data.html)

### Inferences
- Cheapest clearly licensed paths found: Tiingo Redistribution (USD 250-500/mo) and Twelve Data Venture (USD 414-499/mo). Both are flat fees, so data cost per account falls linearly with scale.
- Tiingo's Redistribution table mentions "End-of-Day Prices and IEX" on the Forex page; it is not 100% explicit that real-time FX/XAU WebSocket redistribution is covered or that the 80,000 daily-request and 1 TB caps apply to WebSocket streaming. Must be confirmed in writing before relying on it.
- Tiingo's FX feed "updates to the second" (aggregated top-of-book) is probably fine for a simulated platform but coarser than tick-by-tick ECN data; fills against bid/ask will be at that granularity.
- Live-Rates is the cheapest (EUR 20-50/mo) but its source, quality (about 1 s updates) and redistribution rights are unverified; risky as a primary feed, possibly usable as a backup.
- TraderMade and Finage list prices (GBP/USD ~600/mo) are for internal use; a display licence is a separate negotiation, so their real display price is unknown and probably higher.

### Gaps
- Massive (currencies business), Barchart, dxFeed, Xignite/QUODD, LSEG, ICE, Bloomberg: no published display/redistribution price for FX; only contact sales or aggregator estimates.
- Finnhub forex pricing page did not render; no Finnhub FX redistribution terms found.
- Whether Tiingo/Twelve Data licences count "end users" or cap them (none stated) and whether white-label display under another company's brand (our prop firm customers) is allowed under Twelve Data Venture vs. Enterprise+ ("White-labeling data access") is unclear. This matters because our customers' traders see the data under the prop firm's brand.
- No anecdotal (Reddit/HN) quotes for FX display licences were found in this session.

## 2. Are spot FX and gold free of exchange fees, and what are the licensing pitfalls?

### Takeaway
Spot FX and spot XAU/USD are OTC, so there are no exchange per-user fees like those for CME futures or equity/index data; forex prop traders typically pay no separate data fee. The binding constraint is instead the vendor's contract (internal vs. external display, derived data, end-user definitions). Two exceptions: the LBMA Gold Price benchmark (the "fix") needs an IBA licence, and gold futures (COMEX) or index CFDs referencing exchange data bring exchange fees back in.

### Cited Findings
- "Forex traders generally don't pay separate data feed fees because forex is decentralized, and the data is usually built into the spread. Futures and stock traders, however, will almost always see these fees itemized." (Dec 2025) - [DamnPropFirms](https://damnpropfirms.com/prop-firms/platform-fees-vs-data-feed-fees-key-differences/)
- Futures prop example: real-time CME data around USD 40/mo for retail; firms either pass exchange costs to traders or bundle them - [DamnPropFirms](https://damnpropfirms.com/prop-firms/platform-fees-vs-data-feed-fees-key-differences/); a USD 50K futures challenge buyer may pay USD 115 CME pro-data in the first month (2026, secondary) - [Track360](https://www.track360.io/blog/cheapest-prop-firms-2026-real-cost-breakdown)
- LBMA Gold Price (AM/PM auction benchmark) is administered by ICE Benchmark Administration; "Any party using the LBMA Gold Price for valuation and pricing activities and in transactions and financial products requires a usage licence with IBA"; intraday data is available only 4 hours after publication on a delayed basis - [LBMA prices](https://www.lbma.org.uk/prices-and-data/lbma-precious-metal-prices); [ICE LBMA Gold Price](https://developer.ice.com/fixed-income-data-services/catalog/lbma-gold-price); fee schedule exists but was not read: [IBA fee schedule 2027](https://www.ice.com/publicdocs/IBA_MLA_Licensing_Data_Fee_Schedule_2027.pdf)
- Vendor contract restrictions are the real gate: Tiingo standard plans "may not display or share the data" - [Tiingo](https://www.tiingo.com/about/pricing); Twelve Data individual plans are "personal, internal, and non-commercial" - [Twelve Data](https://twelvedata.com/pricing); Massive "Individual use only" - [Massive](https://massive.com/pricing?product=currencies); TraderMade needs "prior permission" to display - [TraderMade terms](https://marketdata.tradermade.com/termsandconditions); Finage "strictly prohibited" - [Finage](https://finage.co.uk/pricing)
- Pitfalls listed by a legal guide (Dec 2025): internal vs customer-facing classification is the biggest cost driver; derived data (analytics built on licensed data) ownership must be clarified; per-user pricing creates disputes over what a "user" is (named, concurrent, API key); professional vs non-professional misclassification can cause "massive retroactive invoices"; revenue-share deals bring audits with "underpayment penalties of 1.5-2x the shortfall" - [terms.law](https://terms.law/Trading-Legal/guides/api-license-trading-data.html)
- Historical enforcement example (flag: 2015, equities/index CFDs, not spot FX): exchanges (CME, LSE, BATS) required each downstream broker to be "disclosed and charged a fee"; unlicensed downstream redistribution led to threats of "large backdated fees" - [Finance Magnates, Aug 2015](https://financemagnates.com/institutional-forex/execution/exchanges-clamp-down-as-unlicensed-firms-distribute-their-data-feeds)
- Indices later option: Cboe offers free retail equities market data in Europe and US, and 26 Degrees markets "No per-user fees" redistribution for CFD brokers (May 2026) - [26 Degrees](https://www.26degreesglobalmarkets.com/cboe-scaling-retail-market-data-distribution/); [Cboe insights](https://www.cboe.com/insights/posts/how-26-degrees-enabled-its-institutional-broker-clients-to-scale-distribution-to-millions-of-retail-end-users-without-breaking-their-data-budget)

### Inferences
- For FX majors/minors and XAU/USD from a dealer/aggregator feed, our cost is only the vendor licence; there is no exchange "per non-pro user" layer to add.
- Candles/charts/history we build from licensed ticks are likely "derived data" under most vendor contracts, so they are covered or restricted by the same licence; storing and showing history must be explicitly allowed.
- Showing data under each prop firm's brand (white label / multi-tenant) is a likely point where a vendor may require a higher tier (e.g. Twelve Data Enterprise+ "white-labeling") or per-firm fees. Ask explicitly.
- Do not label any gold price as "LBMA Gold Price" or use the fix; use dealer spot XAU/USD only.
- Adding indices (e.g. US500, GER40) or gold futures later reintroduces exchange licences (index providers, CME), unless sourced as broker-derived CFD prices with redistribution rights (FXCM says "Most CFDs are available for redistribution").

### Gaps
- No primary source found stating explicitly "spot FX has no exchange fees"; the claim rests on market structure (OTC) and a secondary prop-industry article.
- OTC FX venues (EBS, LSEG Matching, Cboe FX, LMAX) do sell their own venue data; their display fees were not researched.
- IBA licence fees for LBMA Gold Price not read (only needed if we use the benchmark).

## 3. How do existing prop/simulated platforms source price feeds, and is it included in their price?

### Takeaway
The big forex prop platforms bundle the price feed into a per-platform or per-active-account fee: TradeLocker's simulated environment includes "Price feed, Execution, Infrastructure" for USD 3,000/mo up to 500 active accounts (about USD 6/active account at full use); Match-Trader bundles market data and liquidity from its own CySEC-regulated partner into USD 2,500-5,000/mo licences. Futures-oriented platforms (Volumetrica, dxFeed-based) pass exchange data fees to traders. No published per-account data line item was found for any FX prop platform.

### Cited Findings
- TradeLocker Starter: USD 3,000/mo for up to 500 active accounts, "Pay only for accounts that actually trade", minimum monthly fee if under 500, "No setup fees or hidden costs"; Custom tiered pricing above 500 (rates not disclosed); Competition Accounts add-on from USD 290/mo per 500 accounts. Its Simulated Live Environment: "TradeLocker handles: Price feed, Execution, Infrastructure", "no bridge connection costs or maintenance fees" - [TradeLocker prop pricing](https://tradelocker.com/prop-firm-pricing/)
- Match-Trader (flag: Nov 2024 announcement): White Label from USD 2,500/mo, Server licence from USD 5,000/mo, Prop turnkey (platform + CRM) USD 4,000/mo; "hosting on AWS or Azure cloud is included"; "market data, and liquidity sourced from a CySEC-regulated partner"; "All infrastructure-related costs are transparently included in the price" - [Match-Trader pricing announcement](https://match-trader.com/match-trader-white-label-as-low-as-2500-and-server-from-5000/)
- Match-Trade Data Feeds: 250+ instruments; liquidity "minimum monthly fee starting from as low as $1,000"; MT4/MT5 bridge included - [Match-Trader liquidity and data feeds](https://match-trader.com/liquidity-and-data-feeds/); current prop site lists Prop Starter / Turnkey / Server tiers without prices ("Contact us") - [Match-Trader prop](https://prop.match-trader.com/)
- Volumetrica: described in prop directories as a futures platform using dxFeed data (secondary, search snippets) - [PropFirmMap Volumetrica](https://propfirmmap.com/firms-by-platform/volumetrica); futures prop traders often pay CME data on top of challenge fees - [Track360](https://www.track360.io/blog/cheapest-prop-firms-2026-real-cost-breakdown)
- dxFeed is typically encountered via a prop firm's platform rather than subscribed to directly; Tickblaze integrates dxFeed data incl. OTC Forex "at no extra cost" to users (secondary) - [PropFirmApp dxFeed review](https://propfirmapp.com/?p=3874)
- Forex/CFD prop traders generally see no separate data fee - [DamnPropFirms](https://damnpropfirms.com/prop-firms/platform-fees-vs-data-feed-fees-key-differences/)

### Inferences
- TradeLocker's floor is roughly USD 6 per active account per month at 500 accounts (USD 3,000/500), higher below 500 because of the minimum fee. That leaves room for a USD 4-5 offer, but only if our own data + hosting cost stays well under USD 4.
- Platforms negotiate one enterprise data licence (or own the LP relationship) and amortise it over all firms, which is the same model available to us with a flat-fee vendor.

### Gaps
- Fintatech: no pricing or data-sourcing information found.
- Which upstream vendor TradeLocker uses for SLE prices was not found.
- Volumetrica's FX/CFD offering (if any) and its exact data pass-through were not confirmed from Volumetrica itself.

## 4. Could a liquidity provider or broker give quotes free or cheaply in exchange for something?

### Takeaway
Yes in principle: LPs and brokers bundle feeds with flow. FXCM states discounts "if you subscribe to our data and execute trades" and offers entry-level data free; Match-Trade/FX-EDGE give a free bridge and data with liquidity from a USD 1,000/mo minimum or about 2% per funded account. But no public offer of a free feed with explicit display rights for a pure simulation platform (no flow) was found; redistribution rights must be negotiated.

### Cited Findings
- FXCM: "There are no connectivity fees and significant discounts are available if you subscribe to our data and execute trades"; "Most CFDs are available for redistribution"; free entry-level data on request - [FXCM market data](https://www.fxcm.com/za/algorithmic-trading/market-data/)
- FX-EDGE (Match-Trade partner): "Prop firm pays FX-EDGE a fixed fee of as low as 2% per funded account"; dedicated hedge account per funded trader; MT4/MT5 bridge free - [Match-Trader liquidity and data feeds](https://match-trader.com/liquidity-and-data-feeds/)
- Match-Trade liquidity: minimum monthly fee "from as low as $1,000" - [Match-Trader liquidity and data feeds](https://match-trader.com/liquidity-and-data-feeds/)
- OANDA's streaming v20 API is free to account holders (costs via spreads) (secondary) - [CurrencyFreaks blog](https://currencyfreaks.com/blog/Currency-Conversion-Api-Oanda-Pricing); OANDA's paid data product (with licence fees) starts at USD 4,850/yr - [OANDA Exchange Rates API](https://www.oanda.com/foreign-exchange-data-services/en/exchange-rates-api/)
- Gold-i's MatrixNET lets prop firms simulate LP execution (latency, slippage, partial fills, rejections), showing that bridge/risk vendors are also a route to prop-oriented price streams (secondary snippet) - [FX News Group: Gold-i](https://fxnewsgroup.com/forex-news/platforms/gold-i-sees-rise-in-demand-from-prop-trading-firms-for-matrixnet/)
- Community guidance (equities/futures context): it is often cheaper to source from an existing licensee, and price can be lowered "by committing to provide X number of new subscribers annually" (search snippet, NexusFi; not verified) - [NexusFi thread](https://nexusfi.com/showthread.php?p=699559)

### Inferences
- The realistic trade is: an LP/broker provides a redistributable feed cheap or free in exchange for (a) routing funded-stage hedging flow, (b) a per-funded-account fee, or (c) referrals. A pure sim platform without flow has less to offer, but our prop firm customers' funded accounts could be the "something".
- Using a broker's retail trading API (OANDA v20, FXCM retail) to feed many third-party users without a written redistribution licence would breach typical terms; do not rely on it.

### Gaps
- No published LP feed price with explicit display rights for third-party end users.
- B2Broker, Finalto, Leverate, LMAX, Integral data-feed terms were not found in this session.

## 5. Hosting cost per active user for a real-time web trading platform

### Takeaway
Bandwidth for streamed quotes is small (about 1-4 GB per active user per month under the assumptions below), costing about USD 0.09-0.36/user on AWS egress and near zero on Hetzner EU (20 TB included per server). Compute and fixed baseline infrastructure dominate at small scale. Avoid per-message-priced managed WebSocket services (Azure SignalR Service) for tick streaming: message fees can exceed USD 2/user/month. Hetzner more than doubled CPX/CCX prices on 2026-06-15, which raises the EU low-cost baseline.

### Cited Findings
- ASP.NET Core SignalR: connections are persistent, "consume extra memory", servers have a limited number of concurrent TCP connections; scale-out needs Azure SignalR Service or a Redis backplane; Redis backplane is "recommended ... for apps hosted on your own infrastructure" and requires sticky sessions unless WebSockets-only with SkipNegotiation (doc updated July 2026) - [Microsoft Learn: SignalR scale](https://learn.microsoft.com/aspnet/core/signalr/scale)
- Azure SignalR Service Standard: 1,000 concurrent connections per unit, first 1,000,000 messages/unit/day included, 99.9% SLA; Premium adds 99.95%, autoscale, up to 1,000 units - [Azure pricing](https://azure.microsoft.com/en-us/pricing/details/signalr-service/); Standard USD 1.61/unit/day (about USD 48/mo), Premium USD 2/unit/day (about USD 60/mo), extra messages about USD 1 (Ably's wording "$1/day" is ambiguous; Azure prices extra messages per million) (Sept 2025) - [Ably comparison](https://ably.com/topic/azure-signalr-pricing)
- AWS EC2 data transfer out to internet: first 100 GB/mo free, then USD 0.09/GB for the first 10 TB, 0.085 next 40 TB, 0.07 next 100 TB, 0.05 above 150 TB (2026, secondary) - [CloudZero](https://cloudzero.com/blog/aws-egress-costs); [DigitalOcean article](https://www.digitalocean.com/resources/articles/aws-egress-costs)
- Hetzner EU cloud servers include 20 TB outbound; overage EUR 1/TB; US plans include only 1 TB (secondary) - [Comparedge Hetzner](https://comparedge.com/tools/hetzner/cost-guide); [Hetzner docs: traffic](https://docs.hetzner.com/robot/general/traffic/)
- Hetzner price increase effective 2026-06-15 (DE/FI, EUR/mo): CCX13 15.99 -> 42.99, CCX23 31.49 -> 85.99, CCX33 62.49 -> 138.49; CPX22 7.99 -> 19.49, CPX32 13.99 -> 35.49, CPX42 25.49 -> 69.49; US CPX up to 3.1x - [PrivateDevOps](https://privatedevops.com/news/hetzner-june-2026-cloud-price-increase-what-to-do); [WZ-IT](https://wz-it.com/en/blog/hetzner-price-increase-june-2026-cpx-ccx-alternatives/)
- DigitalOcean managed PostgreSQL from about USD 15.15/mo (1 GiB single node), backups included - [DigitalOcean docs](https://docs.digitalocean.com/products/databases/postgresql/details/pricing/)
- Bundled reference: Match-Trader includes AWS/Azure hosting in USD 2,500-5,000/mo licences (Nov 2024) - [Match-Trader](https://match-trader.com/match-trader-white-label-as-low-as-2500-and-server-from-5000/)

### Inferences
- Bandwidth model (assumption): server throttles to about 4 updates/s per symbol, a trader watches about 10 symbols, about 50 bytes per update => about 2 KB/s, about 7 MB per online hour. At 6 h/day x 22 trading days that is about 1 GB/month; an always-open tab (24x5) is about 3.7 GB. Cost: AWS USD 0.09-0.36/user; Hetzner EU about EUR 0.001-0.004/user (10,000 users x 4 GB = 40 TB, roughly two servers' allowance).
- Azure SignalR Service pitfall (assumption: batching to 4 messages/s per user, 6 h/day): about 86,000 messages/user/day. At 1,000 concurrent users that is about 86 M/day against 1 M included per unit, i.e. about 85 M extra/day; at about USD 1 per million that is about USD 2,500/mo, i.e. about USD 2.5/user, plus about USD 48/unit. Self-hosted SignalR (Kestrel + Redis backplane) has no per-message fee.
- Rough self-hosted infra (Hetzner EU, post-June-2026 prices; includes app/price service nodes, Postgres, Redis, monitoring, backups; my estimate, not a benchmark): 100 users about EUR 150-250/mo (USD 1.6-2.7/user); 1,000 users about EUR 300-500/mo (USD 0.3-0.55/user); 10,000 users about EUR 1,000-2,000/mo (USD 0.11-0.22/user). On AWS/Azure multiply compute by about 2-3x and add egress USD 0.09-0.36/user.
- Fixed costs dominate below about 500 users; above that, hosting is well under USD 0.5/user.

### Gaps
- No authoritative published benchmark found for ASP.NET Core SignalR connections per server or memory per hub connection (only Blazor circuit figures, which are heavier and not comparable).
- Azure SignalR exact extra-message price and message-size counting not read from Microsoft's own page (it renders prices client-side).
- No published "hosting cost per active trader" figure from any trading platform was found.

## 6. Estimated data + hosting cost per active account per month (100 / 1,000 / 10,000 active users)

### Takeaway
With a flat-fee licence (Tiingo Redistribution or Twelve Data Venture), data is about USD 2.5-5.0 per account at 100 users, USD 0.25-0.50 at 1,000 and USD 0.03-0.05 at 10,000. Adding self-hosted infrastructure, the total floor is about USD 4-8 at 100 accounts (above the USD 4-5 target), about USD 0.6-1.1 at 1,000 and about USD 0.15-0.30 at 10,000. The USD 4-5 price fits from a few hundred active accounts upwards, provided the vendor confirms flat-fee display rights with no per-user fee.

### Cited Findings
- Monthly list prices used (sources in section 1): Tiingo Redistribution Startup USD 250 / Enterprise USD 500 - [Tiingo Forex API](https://www.tiingo.com/products/forex-api); Twelve Data Venture USD 499 (USD 414 annual), Enterprise USD 1,099 (USD 916 annual) - [Twelve Data business](https://twelvedata.com/pricing-business); Live-Rates EUR 50 (EUR 30 annual) - [Live-Rates](https://www.live-rates.com/); TraderMade GBP 599 (display needs permission) - [TraderMade](https://tradermade.com/pricing); Finage from USD 599 (redistribution prohibited by default) - [Finage](https://finage.co.uk/product/forex); OANDA from USD 4,850/yr - [OANDA](https://www.oanda.com/foreign-exchange-data-services/en/exchange-rates-api/); Barchart from USD 500 (secondary) - [FitGap](https://us.fitgap.com/products/barchart-ondemand); Match-Trade liquidity/data min USD 1,000 - [Match-Trader](https://match-trader.com/liquidity-and-data-feeds/); Massive Stocks Business USD 2,499 as a reference only - [Massive business](https://massive.com/business); B-PIPE USD 50k-200k+/yr (secondary) - [CostBench](https://costbench.com/software/financial-data-terminals/bloomberg-terminal/hidden-costs)

### Inferences
Data cost per active account per month = list price / active accounts (assumes one server-side feed fanned out to all users, flat fee, no per-user fee; currencies not converted):

| Option (monthly) | Display rights status | 100 | 1,000 | 10,000 |
|---|---|---|---|---|
| Tiingo Redistribution Startup USD 250 | Published redistribution tier; FX scope and caps to confirm | 2.50 | 0.25 | 0.025 |
| Tiingo Redistribution Enterprise USD 500 | Same | 5.00 | 0.50 | 0.05 |
| Twelve Data Venture USD 499 (414 annual) | "External display" published; white label unclear | 4.99 (4.14) | 0.50 (0.41) | 0.05 (0.04) |
| Twelve Data Enterprise USD 1,099 (916 annual) | "External distribution" published | 10.99 (9.16) | 1.10 (0.92) | 0.11 (0.09) |
| Live-Rates EUR 50 (30 annual) | "Commercial use" only; redistribution unverified | EUR 0.50 (0.30) | EUR 0.05 | EUR 0.005 |
| TraderMade GBP 599 | Needs written display permission; real price unknown | GBP 5.99+ | GBP 0.60+ | GBP 0.06+ |
| Finage USD 599 | Prohibited by default; custom licence | 5.99+ | 0.60+ | 0.06+ |
| OANDA ER API ~USD 404 (4,850/yr) | Not stated | 4.04+ | 0.40+ | 0.04+ |
| Barchart OnDemand from USD 500 | Custom; secondary | 5.00+ | 0.50+ | 0.05+ |
| Match-Trade liquidity/data from USD 1,000 | Bundled with liquidity | 10.00 | 1.00 | 0.10 |
| Massive business (ref. USD 2,499) | Contact sales | ~25 | ~2.50 | ~0.25 |
| Bloomberg B-PIPE USD 4,200-16,700 | Negotiated | 42-167 | 4.2-16.7 | 0.42-1.67 |

- Hosting add-on (section 5 estimate, self-hosted EU): about USD 1.6-2.7 at 100, 0.3-0.55 at 1,000, 0.11-0.22 at 10,000 per account. On AWS/Azure: roughly 2-3x compute plus USD 0.09-0.36 egress per user.
- Total floor with Tiingo Startup or Twelve Data Venture plus self-hosted infra: about USD 4.1-7.7 at 100 accounts, 0.55-1.05 at 1,000, 0.14-0.27 at 10,000.
- Break-even on data alone at USD 4.5/account: Tiingo Startup at about 56 accounts, Twelve Data Venture at about 92-111, Twelve Data Enterprise at about 204-244; adding infra, about 100-200 active accounts are needed before USD 4-5 covers both.
- If a vendor applies a per-user display fee (common in exchange data, e.g. a few USD per non-pro user), the flat-fee math collapses; this is the single most important term to confirm.

### Gaps
- All per-account figures assume no per-end-user fees and no white-label surcharge; none of the vendors confirm this in writing on their pages.
- Usage caps (Tiingo 80,000 daily requests / 1 TB; Twelve Data WS credits) were not tested against our architecture (one upstream WebSocket for ~30 symbols should fit, but this is unverified).
- Hosting figures are estimates from list prices and assumed traffic, not measured benchmarks.
