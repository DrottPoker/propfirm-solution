# How prop platforms and prop firms source licensed FX and metals prices for simulated accounts (October 2026)

Research date: 2026-10-06. Pages were fetched on that date unless noted. "Snippet" = text seen only in a search-engine result, not on a fetched page. "Secondary" = review site, aggregator or vendor marketing blog, not the platform itself. "Anecdotal" = forum or social media. Items older than 2025 are flagged. This note builds on section 3 of `Konkurrenters priser och vårt pris/data_and_hosting_costs.md` (2026-10-04) and does not repeat its vendor price list.

## 1. Platform by platform: who provides the price feed, is it bundled, and is there a published data charge?

### Takeaway
Every FX/CFD prop platform that markets to new prop firms in 2025-2026 bundles a price feed into its platform fee or its own liquidity arm. TradeLocker's SLE, Match-Trader (Match-Prime/FX-EDGE), cTrader (Spotware, now also its own LP cFinancial, launched Sept 2026), DXtrade (dxFeed), Leverate (LXFEED), ThinkTrader (ThinkMarkets) and B2Broker's cTrader White Label all work this way. None of them publishes the data part as its own line item. The exceptions are MT5 under a direct MetaQuotes licence and "bring your own bridge" setups (TradeLocker Option B, Brokeree, FPFX Tech). There the firm needs its own LP or data feed, which vendor cost guides put at about USD 300-1,500/mo for data plus USD 500-2,000/mo for a bridge. None of the platforms names its upstream source for FX and gold prices.

### Cited Findings

**TradeLocker**
- Prop pricing page (fetched 2026-10-06): Starter USD 3,000/mo for up to 500 active accounts, "Pay only for accounts that actually trade", a minimum monthly fee applies, Custom tiers above 500. Option A, "Simulated Live Environment" (SLE): "Demo trading environment with real market data. Launch faster with zero technical overhead, no bridge connection costs or maintenance fees". TradeLocker handles "Price feed, Execution, Infrastructure". Option B, custom bridge: the firm uses its own liquidity and pricing and TradeLocker handles bridge integration, instrument setup and price feed/routes. The page does not name a price source - [TradeLocker prop pricing](https://tradelocker.com/prop-firm-pricing/)
- Broker pricing (fetched 2026-10-06): Starter USD 5,000/mo with "1,000 Live Accounts" and "2,000 Demo Accounts". Brokers "Use your own liquidity, bridge, and pricing". "All hosting is done on our side, included in the base pricing" - [TradeLocker broker pricing](https://tradelocker.com/broker-pricing/)
- Unverified snippet: two search results said the SLE "is priced at $6.50 per active account". This figure is not on the fetched page and may come from an older version of it - [TradeLocker prop pricing](https://tradelocker.com/prop-firm-pricing/)
- TradeLocker has a partner documentation page called "How to Get Quotes and Order Execution", but it could not be fetched (redirect loop), so the upstream vendor is still unknown - [TradeLocker partners portal](https://partners.tradelocker.com/portal/how-to-get-quotes-and-order-execution)

**Match-Trader (Match-Trade Technologies)**
- Liquidity and data page (fetched 2026-10-06): the liquidity partners are Match-Prime and FX-EDGE, and the services are "not provided by Match-Trade Technologies". Data feeds cover "over 250 instruments in total" (FX, CFDs, crypto). There is a data-only product: "For brokers who need reliable price streams without a full liquidity setup, Match-Trade Data Feeds deliver institutional-grade, low-latency feeds". Liquidity has a "minimum monthly fee starting from as low as $1,000". For prop firms, FX-EDGE charges a "fixed fee of as low as 2% per funded account". "You get the MT4/MT5 Bridge included - no extra fees". No price for the data-only product - [Match-Trader liquidity and data feeds](https://match-trader.com/liquidity-and-data-feeds/)
- Prop site (fetched 2026-10-06): no prices, "Contact us". It mentions the FX-EDGE funded-phase option ("paying a fixed per-account fee while FX-EDGE takes on the entirety of funded-phase risk") and "TradingView charts ... available to every client at no additional cost" - [Match-Trader prop](https://prop.match-trader.com/)
- Older than 2025 (Nov 2024): White Label from USD 2,500/mo, Server from USD 5,000/mo, prop turnkey USD 4,000/mo, "market data, and liquidity sourced from a CySEC-regulated partner" (already in the 2026-10-04 note) - [Match-Trader pricing announcement](https://match-trader.com/match-trader-white-label-as-low-as-2500-and-server-from-5000/)

**cTrader (Spotware)**
- Snippet (the page now returns 404, date unknown): "Accurate pricing is already included in the platform offer". The turnkey solution for prop startups "starts at 6,000 EUR" (another snippet: "starting at 6,000 EUR per month, ready within just 5 days"). Spotware arranges "granting access to the price feed". "cTrader's account-based pricing model ensures that firms only pay for the active accounts they use" - [Spotware startup/prop (404)](https://spotware.com/startup/prop)
- Current prop page (fetched 2026-10-06): no prices, "Contact Sales". It offers a gated download, "Learn more about cTrader price feed for prop firms" - [Spotware prop firms](https://www.spotware.com/prop-firms)
- Snippet (Spotware news on FunderPro, date not checked): the cTrader price feed "provides a wide range of instruments along with accurate L2 pricing data" - [Spotware: FunderPro adds cTrader](https://www.spotware.com/news/funderpro-enhances-trading-with-addition-of-the-ctrader-platform/)
- Sept 10, 2026: Spotware launched cFinancial at Forex Expo Dubai (22-23 Sept 2026). cFinancial is "an institutional liquidity provider" for brokers and prop firms covering "FX, metals, indices, energy and crypto", with pricing aggregated from multiple sources. It is sold as a bundle offer with cTrader and cBridge. Regulatory entity and prices are not disclosed - [Spotware Forex Expo Dubai 2026](https://www.spotware.com/news/spotware-forex-expo-dubai-2026/)
- Oct 22, 2025: cTrader free prop trial accounts (branded, time-limited demo accounts for competitions). They are free to the firm and used by FTMO, Instant Funding and Vision Trade. The price feed is not specified - [TradeInformer](https://www.tradeinformer.com/tech-news/ctrader-launches-free-prop-trial-accounts-for-competitions)

**DXtrade (Devexperts, owner of dxFeed)**
- Futures prop page (fetched 2026-10-06): "Turnkey bundle with a trading platform, hosting, and CRM integration", "Launch in 7 days", market data from dxFeed with "futures data feeds from the main US, EU, and other exchanges". No prices on the page - [DXtrade futures prop](https://dx.trade/?p=7639)
- Snippet: the futures variant "Starting at $5,000/month" (not on the fetched page) - [DXtrade futures prop](https://dx.trade/?p=7639)
- Secondary: "prop firms that license DXtrade ... often get dxFeed as the bundled data source", and DXtrade CFD's market data is "supplied by dxFeed" - [PropFirmApp dxFeed review](https://propfirmapp.com/?p=3874)
- DXtrade CFD connects to external liquidity via Centroid (100+ market makers), Brokeree Liquidity Bridge, PrimeXM XCore (120+ LPs) and Trade Processor - [FinanceFeeds: DXtrade + Finalto](https://financefeeds.com/dxtrade-integrates-finaltos-bespoke-liquidity-pools-for-fx-and-cfd-brokers/); [DXtrade partners](https://dx.trade/partners)
- 20 verified prop firms use DXtrade (secondary directory) - [PropFirmMap DXtrade](https://propfirmmap.com/firms-by-platform/dxtrade)

**MetaTrader 5 (MetaQuotes)**
- Older than 2025 (2024 crackdown): MetaQuotes terminated True Forex Funds' MT4/MT5 licences in Feb 2024 and forced grey-label brokers to stop serving prop firms (BlackBull and Funding Pips; Purple Trading and Funded Engineer, AquaFunded, Goat Funded Trader, The Funded Trader and others). The stated trigger at Funding Pips was "active US accounts" - [FundedTrading: True Forex Funds](https://fundedtrading.com/true-forex-funds-shutdown/); [Finance Magnates: Funding Pips case](https://financemagnates.com/forex/prop-trading-and-metaquotes-funding-pips-case-may-mark-the-end-of-mt-access-to-us-clients/); [FX News Group: Blueberry terminates MT prop business](https://fxnewsgroup.com/forex-news/retail-forex/blueberry-markets-terminates-all-mt4-mt5-prop-firm-business/)
- May 14, 2025: prop firms returned to MT5 under their own direct MetaQuotes licences, for example Funding Traders after incorporating in Saint Lucia and Funding Pips with "a direct license from MetaQuotes". The article does not say where their price data comes from - [Finance Magnates](https://www.financemagnates.com/forex/mt5-returns-to-prop-firm-funding-traders-after-saint-lucia-registration/)
- MetaQuotes sells the platform, not prices. An MT5 prop setup needs a datafeed or bridge. The Tools for Brokers (T4B) "MT5 Main Label" prop package (snippet; the page returned 403) lists "Trade Processor liquidity bridge, bridge connection to liquidity providers, orders for demo accounts, data feed for symbols ..., hosting services" - [T4B for prop trading](https://t4b.com/for-prop-trading/)
- Snippet (Finance Magnates, not fetched): brokers running both MT4 and MT5 pay MetaQuotes about USD 20,000-25,000/mo from Jan 2025, plus USD 3,000-5,000 for white labels - [Finance Magnates: MetaQuotes fee increase](https://www.financemagnates.com/forex/metaquotes-to-increase-metatrader-licensing-fees-from-january-1/)

**Volumetrica**
- Futures: "Volumetrica identifies dxFeed as its real-time data partner for its futures offering", and at a dxFeed prop firm "the data cost is embedded in your challenge fee" (secondary, snippet) - [PropFirmMap Volumetrica](https://propfirmmap.com/firms-by-platform/volumetrica); [PropFirmApp dxFeed review](https://propfirmapp.com/?p=3874)
- FX/CFD: Volumetrica FX was integrated into the Trade Tech Solutions prop ecosystem (Aug 22, 2025, promotional article). It covers FX, indices, commodities and 500-600+ crypto CFDs. No data vendor and no prices are named - [TradeInformer](https://www.tradeinformer.com/tech-news/5-reasons-prop-firms-are-switching-to-volumetrica-fx-with-trade-tech-solutions)

**ThinkTrader (ThinkMarkets)**
- Older than 2025 (Jul 3, 2024): ThinkMarkets "offered services to other prop trading brands for a while now", namely "price data of asset classes, an admin dashboard, and CRM", and licenses ThinkTrader to prop firms. It previously grey-labelled its MetaTrader licence to prop firms. No prices are given - [Finance Magnates](https://www.financemagnates.com/forex/cfds-broker-thinkmarkets-launches-its-own-prop-trading-brand/)
- The prop firm Bespoke moved to ThinkTrader to resume onboarding US clients - [Finance Magnates](https://financemagnates.com/forex/prop-trading-firm-bespoke-to-resume-onboarding-us-clients-with-thinktrader-integration/amp)

**B2Broker**
- Nov 17, 2025: cTrader White Label Prop is "a turnkey solution that includes the cTrader infrastructure and a client cabinet based on B2PROP, liquidity or data feed, crypto processing, and integration with payment systems". Pricing comes from "B2BROKER's liquidity clusters, which aggregate pricing from multiple sources". "cTrader charges per account rather than trading volumes". No amounts are given - [Spotware news: B2Broker cTrader WL prop](https://www.spotware.com/es/news/b2broker-launches-ctrader-white-label-prop-trading-solution/)
- B2Prop (the CRM/challenge layer; press release date not verified): USD 1,000/mo when added to B2Copy, B2Core or White Label cTrader Prop, USD 2,000/mo standalone, USD 3,000/mo with B2Copy (snippet) - [FinTech Futures: B2Prop](https://www.fintechfutures.com/press-releases/b2broker-announces-b2prop-an-all-inclusive-solution-for-launching-a-prop-trading-firm)
- June 2026 B2Broker article: "Synthetic models fill orders internally against a reference price feed, so there is no LP counterparty". It does not name the feed or its cost - [B2Broker: white label prop solution](https://b2broker.com/news/white-label-forex-prop-firm-solution/)

**Leverate**
- Jul 9, 2026: the start-up broker/prop plan is free for one month, now with 500 trader accounts (up from 50), no setup fee and no long-term commitment. It includes "data feed and Leverate Liquidity, hosting, a payment processor". Paid-tier prices are not given - [FX News Group](https://fxnewsgroup.com/forex-news/platforms/leverate-increases-offer-of-start-up-broker-prop-firm-plan/)
- LXFEED "consolidate[s] data from multiple sources". Setup fee "USD 25,000-50,000 (one-time)" (secondary snippet; this conflicts with the no-setup-fee start-up plan above) - [NexusFi: Leverate](https://nexusfi.com/d/b2b-services/leverate/)

**Brokeree**
- Prop Pulse is a challenge and account management layer for MT4/MT5 and cTrader. No prices and no feed are included - [Finance Magnates](https://www.financemagnates.com/forex/brokeree-solutions-integrates-prop-trading-tools-to-ctrader/); [Brokeree news](https://brokeree.com/news/prop-pulse-solution-for-prop-trading/)
- Brokeree's MT4/MT5 Synthetic Feed builds instruments from "quotes of original symbols from liquidity or market data providers", so the firm brings its own source (snippet) - [Brokeree solutions](https://brokeree.com/solutions/)

**FPFX Tech**
- FPFX Tech is a back office (CRM, challenge logic, dashboards, payouts) that integrates MT4, MT5, DXtrade, cTrader, Match-Trader, Rithmic and others. Pricing is quote-based. One secondary listing says "$3000 - one-time" (unverified). FPFX does not supply prices - [FundedTrading: FPFX](https://fundedtrading.com/tech-provider/fpfx/); [NexusFi: FPFX Tech](https://nexusfi.com/d/b2b-services/fpfx-tech/)

**Fintatech**
- Nothing found. Searches return only "Finatechs" (a 2024 retail platform launch, unrelated) - [Crowdfund Insider, Aug 2024](https://www.crowdfundinsider.com/2024/08/228703-finatechs-introduces-trading-platform-with-advanced-analytical-tools/)

### Inferences
Summary of what each platform does with prices (EUR/USD as published, per month unless stated):

| Platform | Price source | Feed bundled? | Published data fee |
|---|---|---|---|
| TradeLocker SLE | Not disclosed | Yes ("Price feed, Execution, Infrastructure") | None; USD 3,000/mo up to 500 active accounts all-in (USD 6.50/active account unverified) |
| TradeLocker Option B / broker | Firm's own LP | No | n/a |
| Match-Trader | Match-Prime / FX-EDGE (partner, CySEC-regulated per Nov 2024) | Yes in licence; data-only product exists | Liquidity min USD 1,000; FX-EDGE about 2% per funded account; data-only not priced |
| cTrader (Spotware) | Spotware price feed, now cFinancial (Sept 2026) | Yes ("already included") | None; turnkey from EUR 6,000 (snippet) |
| B2Broker cTrader WL Prop | B2Broker liquidity clusters | Yes ("liquidity or data feed") | Not published |
| DXtrade | dxFeed (in-house) | Yes (secondary) | Not published; futures from USD 5,000 (snippet) |
| MT5 direct licence | Firm's LP/bridge (T4B, Brokeree, PrimeXM, Centroid etc.) | No | Data USD 300-1,500 + bridge USD 500-2,000 (vendor guide, section 2) |
| Leverate | LXFEED + Leverate Liquidity | Yes | Not published; first month free (500 accounts) |
| ThinkTrader | ThinkMarkets (broker) | Yes ("price data of asset classes") | Not published (2024) |
| Volumetrica | dxFeed (futures); FX unknown | Futures: passed into challenge fee | Not published |
| Brokeree, FPFX Tech | n/a (add-on layers) | No | n/a |
| Fintatech | Unknown | Unknown | Unknown |

- The market splits into two models. (a) A broker, LP or data company owns the price relationship and bundles it: Match-Trader through Match-Prime, Spotware through cFinancial, Devexperts through dxFeed, Leverate, ThinkMarkets, B2Broker. (b) A pure software vendor either hides the upstream source (TradeLocker SLE) or makes the firm bring its own (MT5, Brokeree, FPFX). Kronant Prop is a type (b) vendor that wants to offer type (a) convenience. So we must hold one licence that lets us show data to all tenants, as TradeLocker SLE apparently does.
- The trend in 2025-2026 is platform vendors adding their own liquidity or price businesses (Spotware cFinancial in Sept 2026, Match-Trade's Match-Prime, Leverate Liquidity). This makes the bundled feed a revenue and lock-in tool rather than a cost line. It also suggests their marginal data cost per account is low.
- For our pricing: TradeLocker's bundled price (about USD 6/active account at 500, inclusive of feed, platform and hosting) is the most transparent competitor price. Spotware's "from EUR 6,000/mo" (snippet) is higher. No competitor exposes a separate data charge, so traders and firms expect the feed to be "included".

### Gaps
- None of the platforms names its upstream FX/XAU source, except Devexperts (dxFeed, own company) and Match-Trader (Match-Prime/FX-EDGE). TradeLocker's SLE source is still unknown because its partner documentation page could not be fetched.
- The USD 6.50/active account TradeLocker SLE figure is unverified.
- The Spotware EUR 6,000 figure comes from a snippet of a now-removed page, and it is unclear whether it is a one-off or monthly fee.
- Fintatech: no information found at all.
- DXtrade CFD prop pricing and dxFeed FX redistribution prices are not public.
- cFinancial's regulator, entity and pricing are not public.

## 2. Do MT5 or cTrader white-label prop firms pay a separate feed fee, and how much?

### Takeaway
On cTrader the feed comes from Spotware or the white-label provider (B2Broker) and is inside the platform or white-label fee. On MT5 a firm with its own licence pays a separate LP or data line. Vendor guides from 2026 put this at USD 300-1,500/mo for data (for "anything past the core FX pairs") plus USD 500-2,000/mo for a bridge. Match-Trade's liquidity starts at USD 1,000/mo, and FX-EDGE charges about 2% per funded account. All these figures come from vendors' own marketing guides or pages. No independent invoice-level figure was found.

### Cited Findings
- B2Broker MT5 white-label cost guide (May 30, 2026, updated Jun 25, 2026; vendor content): setup USD 5,000-30,000 one-time; platform USD 1,000-10,000+/mo; bridge connectivity USD 500-2,000/mo; data feeds USD 300-1,500/mo; back office/CRM USD 500-1,500/mo; per-active-client charge USD 5-20. Quote: "Real-time pricing for anything past the core FX pairs carries its own licensing cost, usually $300 to $1,500 per month for coverage of indices, commodities, and cryptocurrency CFDs". First-year total "usually run $30,000 to $150,000" - [B2Broker: MT5 white label costs](https://b2broker.com/news/mt5-white-label-costs/)
- Track360 (May 31, 2026, written by a Track360 co-founder; vendor blog), indicative 2026 ranges: "Trading platform & data" USD 1k-10k/mo ("Server, licences, market data, liquidity feed"); platform/SaaS USD 2k-15k/mo; per-account or challenge USD 1-10; setup USD 5k-50k - [Track360: white label prop firm cost 2026](https://track360.io/blog/white-label-prop-firm-cost-providers-setup-2026)
- Unverified snippet: "Liquidity/data feed monthly costs typically range from $500-$3,000+" and "a realistic all-in monthly cost often moves closer to $2,700-$11,000+" (search summary attributed to MT5 white-label cost guides; not fetched) - [Quadcode: MT5 white label cost](https://quadcode.com/blog/mt5-white-label-cost)
- Match-Trade: liquidity minimum "from as low as $1,000" per month; FX-EDGE "as low as 2% per funded account"; bridge included free; a data-only feed exists but has no published price - [Match-Trader liquidity and data feeds](https://match-trader.com/liquidity-and-data-feeds/)
- cTrader: per the snippet, Spotware's offer includes "access to the price feed" and "Accurate pricing is already included in the platform offer" - [Spotware startup/prop (404)](https://spotware.com/startup/prop); B2Broker's cTrader WL Prop includes "liquidity or data feed" (Nov 2025) - [Spotware news](https://www.spotware.com/es/news/b2broker-launches-ctrader-white-label-prop-trading-solution/)
- MT5 bundle via T4B: the package includes "data feed for symbols" and "orders for demo accounts" (snippet; no price) - [T4B for prop trading](https://t4b.com/for-prop-trading/)

### Inferences
- The B2Broker wording suggests that core FX pairs usually come with the LP or bridge relationship at no explicit charge, and that extra licence fees mainly apply to indices, commodities and crypto CFDs. This supports our earlier finding that spot FX and XAU carry no exchange fees.
- For a prop firm, the realistic separate data line on MT5 is about USD 300-1,500/mo plus bridge. This is the same band as the flat-fee display licences found on 2026-10-04 (Tiingo USD 250-500, Twelve Data USD 414-1,099). One flat licence of about USD 250-1,500/mo shared across all our tenants is therefore market-consistent. At 1,000 active accounts it costs USD 0.25-1.50/account.
- The per-funded-account LP model (FX-EDGE about 2%) is an alternative that ties data cost to funded accounts rather than to all active accounts, but it requires routing funded-stage risk to that LP.

### Gaps
- No prop firm has published what it actually pays for an MT5 datafeed. All figures are vendor marketing ranges.
- Spotware's per-account rate and the share of it that covers the price feed are not public.

## 3. What founders, CTOs and consultants report paying for an FX/metals feed with display rights

### Takeaway
No first-hand founder or CTO figure for an FX/metals display licence was found. Reddit was not reachable with the research tools, and ForexFactory, NexusFi and LinkedIn searches gave no usable numbers. The only "reported" figures are consultant and vendor guides (B2Broker, Track360, Quadcode), all of which sell prop infrastructure. They put data at USD 300-1,500/mo, or bundle it into a USD 1,000-10,000/mo "platform & data" line. Prop-industry blogs agree that FX traders do not pay a separate data fee, unlike futures traders.

### Cited Findings
- Vendor and consultant figures: data feeds USD 300-1,500/mo - [B2Broker (May/Jun 2026)](https://b2broker.com/news/mt5-white-label-costs/); "Trading platform & data" USD 1k-10k/mo - [Track360 (May 2026)](https://track360.io/blog/white-label-prop-firm-cost-providers-setup-2026)
- Consultant guides without figures: Programming Insider (Aug 22, 2026) says white-label pricing is "modular rather than flat" and advises asking for "a full breakdown by component" - [Programming Insider](https://programminginsider.com/breaking-down-the-real-cost-of-prop-firm-white-label-software/). Quadcode (updated Jul 10, 2026) advises "Ask what is included and what becomes billable later" - [Quadcode](https://quadcode.com/blog/how-to-start-a-white-label-prop-trading-firm)
- TradeLocker publishes a "2026 Prop Firm Launch Blueprint" covering "Realistic costs & budgeting". The figures are only in a gated PDF that was not read - [TradeLocker: open a prop firm](https://tradelocker.com/open-a-prop-firm)
- Trader side (secondary): at a dxFeed-based prop firm "the data cost is embedded in your challenge fee" - [PropFirmApp dxFeed review](https://propfirmapp.com/?p=3874). "Forex traders generally don't pay separate data feed fees" (Dec 2025; already in the earlier note) - [DamnPropFirms](https://damnpropfirms.com/prop-firms/platform-fees-vs-data-feed-fees-key-differences/)
- Futures contrast (secondary): CME data USD 12-124+/mo per trader; for example, a USD 50K futures challenge buyer pays USD 115 for CME pro data in the first month - [Track360: cheapest prop firms 2026](https://track360.io/blog/cheapest-prop-firms-2026-real-cost-breakdown)

### Inferences
- Since even vendors trying to sell bundles quote data at only USD 300-1,500/mo, the FX/XAU feed is a small cost for a prop firm compared with platform fees (USD 2k-15k/mo) and payouts. The economics of the business do not depend on it, but its licence terms (white label, multi-tenant display) do.

### Gaps
- No first-hand founder, CTO or consultant quote with a named FX/metals display-licence price was found. Reddit could not be searched (blocked for the tool), and LinkedIn and YouTube gave nothing indexable.
- The TradeLocker blueprint PDF figures were not read.

## 4. Problems over price sources (feeds differing from the market, disputes, vendor or regulator objections) and lessons

### Takeaway
The documented problems are about bad ticks and spikes on a firm's own server leading to disputed stop-outs and payouts, about vendors pulling the platform (MetaQuotes 2024), and about regulators alleging manipulated execution against simulated prices (CFTC v. MyForexFunds, 2023). No case was found of a data vendor objecting to a prop firm's FX feed use. The lessons for choosing a feed are: use an aggregated, filtered feed with spike protection; keep an audit trail of the ticks used for fills; have a second source; and avoid depending on a single vendor's goodwill.

### Cited Findings
- Anecdotal (forum, Feb 2026; snippet, the page returned 403): on Blueberry Funded's MT5 feed at the 00:00 rollover on Feb 26, 2026, USDCHF spiked from about 0.7728 to 0.6830 (about 450 pips per the post) and recovered to 0.7723 within 4 minutes. A trader's stop filled 33.5 pips beyond its level (loss USD 176.28). The firm credited USD 175 + 2 on Feb 28 - [Forex Peace Army: Blueberry Funded tag](https://www.forexpeacearmy.com/community/tags/blueberryfunded/)
- Older than 2025 (May 2024): MyFlashFunding blamed a "data feed issue" that created unusually favourable conditions and denied or adjusted payouts. PropFirmMatch delisted the firm - [FX News Group](https://fxnewsgroup.com/forex-news/retail-forex/prop-firm-myflashfunding-confirms-two-week-delayed-payouts-to-traders/). The CEO later said affected traders got "full refunds and compensation" (May 20, 2024) - [Finance Magnates](https://www.financemagnates.com/forex/prop-trading-firm-myflashfundings-ceo-responds-to-payout-concerns/)
- Older than 2025 (Aug 2023): the CFTC alleged that MyForexFunds (Traders Global) used software to delay execution and impose slippage on simulated accounts, and "manipulation of market data". Afterwards several firms changed their wording to "simulated accounts" - [Finance Magnates: Dissecting MFF](https://www.financemagnates.com/forex/dissecting-my-forex-funds-model-how-did-the-prop-trading-firm-generate-310m/); [FundedTrading: what happened to MFF](https://fundedtrading.com/what-happened-to-my-forex-funds/). The case was later dismissed (headline only; details not verified) - [Today's General Counsel](https://todaysgeneralcounsel.com/?p=96462)
- Vendor objection (platform, not feed; 2024): MetaQuotes cut off grey-label MT5 for prop firms, which forced firms to migrate within days. Funding Pips moved to Match-Trader - [Finance Magnates via TradingView](https://vn.tradingview.com/news/financemagnates:f6deb5f85094b:0-prop-trading-firm-funding-pips-back-online-with-match-trader-migration); [Finance Magnates: FTMO halts US clients](https://www.financemagnates.com//forex/exclusive-ftmo-halts-us-client-acquisition-amid-metaquotes-crackdown/)
- Vendor objection (back office, date not verified): FPFX Tech terminated its licence with Funded Engineer, alleging fake accounts, wash trading and fictitious payouts - [FinanceFeeds](https://financefeeds.com/breaking-fpfx-tech-accuses-prop-firm-funded-engineer-of-fraud-wash-trading/)
- Price differences are "normal": a trade-copier/analytics vendor tells users its unified premium feed differs from prop firms' MT5/cTrader prices because "each prop firm uses different Liquidity Providers (LPs) and aggregates prices differently" - [PropFirmOne help](https://propfirmone.featurebase.app/help/articles/6791725-why-do-i-see-different-prices-between-pf1-and-my)
- Feed-quality marketing points to the same risk: Leverate's LXFEED promises brokers can "sidestep costly errors like price spikes, requotes, and off quotes" (snippet) - [Finance Magnates: Leverate prop](https://www.financemagnates.com/forex/leverate-jumps-into-prop-trading-with-the-launch-of-white-label-solution/); cFinancial promises "Pricing designed to hold steady during volatility spikes" - [Spotware Forex Expo Dubai 2026](https://www.spotware.com/news/spotware-forex-expo-dubai-2026/)

### Inferences
- In a simulated prop environment, the feed is the "market". A bad tick becomes a dispute over a stop-out, a breach or a payout, and a feed that is easy to arbitrage (stale or slow) becomes a payout-loss risk. Both reached forums and the trade press (MyFlashFunding, Blueberry Funded).
- Practical lessons for Kronant: (1) filter spikes and outliers, with extra care around the daily rollover and low-liquidity hours; (2) store the exact ticks used for every fill and stop so disputes can be settled (our 30-day history helps, but fill-level audit data may need longer retention); (3) keep a secondary feed for cross-checking and failover; (4) give firms a documented "price correction" policy; (5) avoid a single upstream whose withdrawal would halt every tenant (the MetaQuotes lesson); (6) never add artificial delay or slippage (the MFF allegations).
- Differences between our prices and other venues are normal. Firms should be able to explain them (aggregated source, spreads) to their traders.

### Gaps
- No documented case was found of an FX data vendor or regulator objecting to how a prop firm displayed or used licensed FX prices.
- The Blueberry Funded case is a single forum report, seen only via a snippet.
- The later status of the CFTC v. MFF case (dismissal details) was not verified.

## 5. Do prop-tech vendors sell a standalone "price feed for simulated accounts", and at what price?

### Takeaway
Several firms sell feeds that can serve prop or demo environments without full liquidity: Match-Trade Data Feeds, the FX-EDGE bridge with feeds, Spotware's "cTrader price feed for prop firms" (gated), the GCEX and FXCM redistributable CFD/FX feeds, and now Spotware's cFinancial. None publishes a price for a data-only, simulation-only licence. The only published anchors are LP-linked: a USD 1,000/mo liquidity minimum and about 2% per funded account. FXCM and GCEX offset data cost against trading commissions, which assumes the buyer sends flow.

### Cited Findings
- Match-Trade Data Feeds: "For brokers who need reliable price streams without a full liquidity setup", 250+ instruments; no published price. Liquidity starts at USD 1,000/mo minimum - [Match-Trader liquidity and data feeds](https://match-trader.com/liquidity-and-data-feeds/)
- FX-EDGE: a free MT4/MT5 bridge with "ultra-fast data feeds for B-Book execution", priced at a fixed fee "as low as 2% per funded account" (partly a snippet) - [Match-Trader liquidity and data feeds](https://match-trader.com/liquidity-and-data-feeds/)
- Spotware: the gated "cTrader price feed for prop firms" document; prices via sales - [Spotware prop firms](https://www.spotware.com/prop-firms); cFinancial (Sept 2026) is offered to prop firms with no public price - [Spotware Forex Expo Dubai 2026](https://www.spotware.com/news/spotware-forex-expo-dubai-2026/)
- FXCM Pro CFD price feed: "Most CFDs are available for redistribution". "We can charge per instrument, or via packages". "There are no connectivity fees" and "significant discounts" if clients also trade with FXCM liquidity. "intended for institutional and professional clients only". No amounts - [FXCM CFD price feed](https://www.fxcm.com/pro/market-data/cfd-price-feed/)
- Older than 2025 (Oct 10, 2024): GCEX market data feed covers "Equity Index CFDs, Energy CFDs, Commodity CFDs, Crypto CFDs, Spot FX, and Bullion". Brokers, funds and professional traders can "redistribute this data with greater flexibility". "the cost of the data can be offset against trading commissions". No price - [Finance Magnates](https://www.financemagnates.com/institutional-forex/gcex-introduces-market-data-feed-covering-cfds-fx-and-cryptos/)
- TradeLocker SLE is in effect a "price feed for simulated accounts" sold only inside the platform licence (USD 3,000/mo up to 500 active accounts) - [TradeLocker prop pricing](https://tradelocker.com/prop-firm-pricing/)
- Leverate bundles its "data feed and Leverate Liquidity" into the start-up plan (first month free, 500 accounts, Jul 2026) - [FX News Group](https://fxnewsgroup.com/forex-news/platforms/leverate-increases-offer-of-start-up-broker-prop-firm-plan/)

### Inferences
- LP-linked feeds (Match-Prime/FX-EDGE, cFinancial, FXCM, GCEX, B2Broker) are the natural "free or cheap" route for us. The price is commissions or funded-account flow, which our tenant firms could provide. Pure data vendors (Tiingo, Twelve Data, from the 2026-10-04 note) are the clean, flow-free route at USD 250-1,099/mo.
- Before signing, both routes must confirm in writing: multi-tenant white-label display (each firm's brand), derived data (candles and 30-day history), no per-end-user fee, and the right to use the prices as the fill reference for simulated execution.

### Gaps
- No standalone simulation-feed product with a public price was found.
- FXCM, GCEX, Match-Trade Data Feeds and cFinancial all require a sales contact for pricing and redistribution scope.
