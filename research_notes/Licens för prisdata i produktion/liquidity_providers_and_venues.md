# Licensing real-time spot FX and metals feeds from LPs, brokers, FX venues and institutional distributors (October 2026)

Research date: 2026-10-06. Pages and PDFs were fetched live on that date unless stated. "Primary" = provider's own page or PDF. "Secondary" = press, directory, forum or a search-engine summary of a page that could not be opened. Anything dated before 2025 is flagged "(older)". FXCM, Match-Trade, FX-EDGE, OANDA and Bloomberg B-PIPE are covered in the 2026-10-04 note (`research_notes/Konkurrenters priser och vårt pris/data_and_hosting_costs.md`, sections 1, 2 and 4) and are not repeated here except for comparison.

## 1. Which LPs, prime-of-prime brokers and tech vendors sell a "data feed only" (no liquidity) to prop firms or prop platforms, and at what price? (Includes the per-provider matrix.)

### Takeaway
The only FX venue in scope that publishes a data-only price usable by a non-trading client is LMAX Exchange: USD 1,500/month for combined FX and metals top of book (USD 1,200 FX only, USD 500 metals only), effective 1 June 2026. Non-trading clients get that feed throttled to 10 updates per second, and the PDF says nothing about redistribution or display rights. TrueFX (Integral) publishes USD 4,950/month (Professional) and USD 7,450/month (Institutional) but does not state its display terms. No LP or prime-of-prime (B2Broker/B2Prime, Finalto, Swissquote) publishes a data-only price. B2Broker sells "liquidity or data feed" bundled into its cTrader prop package (Nov 2025). Bridge and aggregation vendors (PrimeXM, oneZero, Centroid, TFB, Gold-i, Leverate) are pipes that need an upstream data licence: they add a platform fee (PrimeXM XCore from USD 2,000/month) and do not themselves grant display rights. Retail brokers (Saxo, Interactive Brokers) explicitly forbid passing data on to third parties.

### Cited Findings

**LMAX Exchange (FCA-authorised MTF) - primary, published fee schedule**
- Fee PDF heading: "Monthly market data fees" / "DEPTH/FULL ORDER BOOK ACCESS FEES - EFFECTIVE FROM: 01 JUNE 2026" - [LMAX Exchange market data fees PDF](https://www.lmax.com/documents/LMAXExchange-Market-Data-Fees.pdf)
- FIX connection fees: "LD4 / NY4 / TY3 $500 (per venue)"; "SG1 Free of charge" - [LMAX fees PDF](https://www.lmax.com/documents/LMAXExchange-Market-Data-Fees.pdf)
- Columns are "Standard fees | Trading client discounts | Volume discounts", all monthly:
  - FX only: "TOB $1,200 Free"; "3 levels $2,600 $1,300 50% (if total notional >=4b)"; "5 levels $6,000 $3,000 50% (if total notional >=7b)"; "10 levels $13,500 $6,750 50% (if total notional >=10b)"; "Delayed trade feed $1,500 Free"
  - Metals only: "TOB $500 Free"; "3 levels $1,200 $600 50% (if total notional >=4b)"; "5 levels $2,400 $1,200 50% (if total notional >=7b)"; "10 levels $5,500 $2,750 50% (if total notional >=10b)"; "Delayed trade feed $1,100 Free"
  - Combined schedule (FX & Metals): "TOB $1,500 Free"; "3 levels $3,000 $1,500 50% (if total notional >=4b)"; "5 levels $6,500 $3,250 50% (if total notional >=7b)"; "10 levels $15,000 $7,500 50% (if total notional >=10b)"; "Delayed trade feed $1,500 Free" - [LMAX fees PDF](https://www.lmax.com/documents/LMAXExchange-Market-Data-Fees.pdf)
  - Note: in the PDF's visual layout the "50%" cells sit one row higher (next to TOB) than in the PDF's text order (next to 3 levels). The text-order reading is shown above. Confirm with LMAX.
- Conditions: "For non-trading and trading clients who do not meet monthly eligibility criteria FIX market data throttled to 10 updates per second." "Trading clients monthly eligibility criteria $250 million minimum traded volume per month, per tier, per venue." "$5 billion minimum traded volume per month for access to 5 and 10 levels." "Trading clients can receive unthrottled FIX market data (up to 1ms updates)." - [LMAX fees PDF](https://www.lmax.com/documents/LMAXExchange-Market-Data-Fees.pdf)
- ITCH: "LD4 $80,000", "Reduced to $25,000 if total global volumes exceed $25bn and a minimum aggressive/passive ratio of 30% is reached." - [LMAX fees PDF](https://www.lmax.com/documents/LMAXExchange-Market-Data-Fees.pdf)
- Entity and contact: "LMAX Limited, which operates a multilateral trading facility, authorised and regulated by the Financial Conduct Authority (firm reference number 509778)"; sales "+44 20 3192 2682 | institutionalsales@lmax.com" - [LMAX fees PDF](https://www.lmax.com/documents/LMAXExchange-Market-Data-Fees.pdf)
- Market data page: Level 1 to Level 3 and ITCH; "250+ unique instruments" across "FX, metals, commodities, equity indices, digital assets and Asian NDFs"; "Available to all clients, regardless of size or activity levels" via FIX 4.2/4.4 or ITCH; 10+ years of history. The page says nothing about licensing, redistribution or display by brokers to retail clients - [LMAX market data](https://www.lmax.com/exchange/market-data)
- LMAX also publishes FX prices to the Pyth Network (from 2021). A Pyth success story says retail brokers and LPs "redistribute" LMAX data to improve retail execution (secondary, Pyth marketing) - [Pyth: LMAX](https://www.pyth.network/success-stories/exploring-the-future-of-market-data-distribution-with-lmax); [Pyth: new data provider LMAX (2021, older)](https://www.pyth.network/blog/new-pyth-data-provider-lmax)
- A retail-trader forum thread reportedly cites LMAX Global asking USD 300/month for .NET API data plus a USD 10,000 account and trading activity. This comes from a search summary only, the thread was not opened, and it is undated - [Elite Trader thread](https://elitetrader.com/et/threads/lmax-datafeed-access-only.361251/)

**Integral / TrueFX - primary price, terms not stated**
- TrueFX now shows two paid tiers: Professional "$4,950 per month" (full set of currencies, optional crypto) and Institutional "$7,450 per month" ("3 level depth of book pricing"); "Bid, Offer"; "Event driven stream via FIX"; prices from "Integral OCX"; footer 2009-2026. No terms on commercial use, redistribution or display - [TrueFX](https://www.truefx.com/)
- Conflict: older sources describe TrueFX as free real-time interbank rates, including a free individual tier with 25 pairs (2009, older) - [Finance Magnates 2009](https://www.financemagnates.com/forex/brokers/integral-introduces-truefx-free-real-time-interbank-rates-for-all/). The current page shows no free tier.

**dxFeed (Devexperts) - contact sales**
- FX offering: Cboe FX data and a "Composite Forex Feed": "Consolidated price from multiple major Forex contributors, Prime Banks and financial institutions", in real time, delayed, historical and market replay. No pricing or licensing terms on the page; "Contact Sales" - [dxFeed FX](https://dxfeed.com/market-data/fx/)
- Oct 2020 (older): dxFeed licensed Cboe FX data; "Clients of dxFeed can already receive Cboe FX through dxFeed's handlers and do not have to have additional contracts with Cboe"; covers 70 pairs "including CLS-Settled; emerging market; and Precious metals" - [dxFeed + Cboe FX](https://dxfeed.com/forex-feed-from-cboe-fx-is-available-via-dxfeed-data-provider/)
- dxFeed data runs underneath DXtrade, which FTMO and BrightFunded use (secondary) - [PropFirmApp dxFeed review](https://propfirmapp.com/?p=3874). dxFeed data is also distributed through the oneZero ecosystem (undated, search snippet) - [dxFeed + oneZero](https://dxfeed.com/dxfeed-market-data-joins-onezeros-ecosystem/)

**B2Broker / B2Prime - bundled, no published data-only price**
- cTrader White Label Prop (17 Nov 2025): "a turnkey solution that includes the cTrader infrastructure and a client cabinet based on B2PROP, liquidity or data feed, crypto processing, and integration with payment systems"; cTrader charges "per account rather than trading volumes". No price stated - [Spotware news](https://www.spotware.com/zh/news/b2broker-launches-ctrader-white-label-prop-trading-solution/)
- Liquidity terms (15 Dec 2022, older): "The previous monthly minimum of $1,250 has been reduced to $1,000"; minimum deposit cut "from $20,000 down to $10,000"; volume fees for spot FX and metals reduced (rates not given) - [Finance Magnates](https://www.financemagnates.com/thought-leadership/b2broker-announces-fresh-institutional-liquidity-offer/)
- B2Prime: a regulated prime-of-prime (CySEC, SFSA, FSCA, FSC Mauritius) "for institutional and professional clients", including proprietary trading groups; distributed through oneZero, PrimeXM, Centroid and FXCubic (Aug 2025, search snippet) - [Finance Magnates: B2Prime Q2 2025](https://www.financemagnates.com/thought-leadership/b2prime-unveils-q2-2025-growth-as-founder-shares-key-milestones/)
- B2Connect builds top-of-book and depth prices from aggregated Level 2 quotes (search snippet) - [B2Broker B2Connect guide](https://b2broker.com/product-guides/what-is-b2connect-and-how-does-it-work/)

**Finalto - no data-only offer found**
- Institutional brokerage and prime services LP for CFDs and rolling spot (forex, precious metals, indices and more). FCA and CySEC regulated. "More than 600 institutional clients across 82 countries", including proprietary trading firms (secondary, search summary; the directory page did not render Finalto when fetched) - [Finance Magnates directory: Finalto](https://directory.financemagnates.com/forex-liquidity-provider/finalto)
- Liquidity is available on DXtrade (search snippet, undated) - [FinanceFeeds](https://financefeeds.com/dxtrade-integrates-finaltos-bespoke-liquidity-pools-for-fx-and-cfd-brokers/). 2025 revenue was USD 75.2M (search snippet) - [FX News Group](https://fxnewsgroup.com/forex-news/institutional/finalto-revenues-up-4-in-2025-to-75m-profit-18m/)
- The finalto.com home and liquidity pages returned no readable content to the fetcher.

**Swissquote - LP, no data-only offer found**
- Brokers page: "Prices sourced from an aggregated stream of Tier 1 banks and non-bank liquidity providers", "130+ financial instruments", proprietary FIX API, MT4/MT5 gateways. No data-only, prop or white-label data offer; no minimums - [Swissquote brokers](https://www.swissquote.com/en/institutional/clients/brokers)
- Acts as LP to Itexsys' MT4/MT5 white-label clients (undated, search snippet) - [Finance Magnates](https://www.financemagnates.com/forex/brokers/itexsys-confirms-liquidity-provider-partnership-with-swissquote/)

**Saxo - redistribution forbidden on OpenAPI**
- "Any use of financial or market data provided by Saxo Group is solely for your own non-commercial use and benefit, and not for resale or other transfer or disposition to, or use by or for the benefit of, any other person or entity." Copying, publication, transmitting and distributing are forbidden without written permission - [Saxo developer: enabling market data](https://www.developer.saxo/excel/user-guide/enabling-market-data)
- Third-party tools are "for direct clients of Saxo Bank" only (search snippet) - [Saxo third-party tools](https://home.saxo/en-gb/platforms/third-party-tools)

**Interactive Brokers - redistribution forbidden**
- GFIS subscriber agreement (IB's data entity; copy hosted by introducing broker Lynx, "Last updated: 09.04.2020", older): "For Non-Professional Subscribers, the Data is licensed only for personal use"; "Subscriber is not allowed to transfer or disclose Data to third parties except as permitted herein or as required to comply with Applicable Laws"; "Subscriber may access a Subscription through only one internet-connected computer or mobile device at a time." - [GFIS Subscriber Agreement (Lynx)](https://www.lynxbroker.de/media/doc/3089_GLOBAL_FINANCIAL_INFORMATION_SERVICES_SUBSCRIBER_AGREEMENT_ENG.pdf)

**IG - not verified**
- IG's help page points only to labs.ig.com, which returned HTTP 500. No IG redistribution terms were retrieved - [IG help](https://www.ig.com/za/help-and-support/platforms/general-queries/how-can-i-acces-the-ig-api-and-what-can-i-use-it-for)

**PrimeXM (bridge/aggregator) - primary pricing, no data fees listed**
- "XCore Fee = Max (Instance Fees, Connectivity Fees, Volume Fees, Transaction Fees)". Instance "$2000" monthly. Maker FIX "$600" monthly ("one pricing and one trading connection"). Taker MT4/MT5 "$1200" monthly; FIX "$100+" minimum ("$50 per stream"); Binary "$100". Giveup FIX "$400" per connection. Volume "Fiat Margin 0.50 [USD/M]", "Fiat Execution 1.00 [USD/M] per side". Transaction "0.001 [USD/TX]". No setup, data or vendor fees and no price-feed-only terms on the page - [PrimeXM XCore pricing](https://primexm.com/xcore/pricing/)
- XCore integrates market data vendors "such as ICE, IB, CBOE" and Refinitiv Elektron, "either as an independent reference price feed or as a part of price aggregation" (search snippet; Elektron integration May 2020, older) - [PrimeXM XCore](https://primexm.com/xcore/); [PrimeXM Elektron (2020)](https://primexm.com/2020/05/14/xcore-integration-with-refinitiv-elektron)

**oneZero (bridge/hub)**
- Ecosystem of 200+ banks, brokers and funds. dxFeed data (Forex, crypto, indices, commodities) is offered to oneZero clients as "custom data packages" (undated search snippets) - [dxFeed + oneZero](https://dxfeed.com/dxfeed-market-data-joins-onezeros-ecosystem/); there is a Cboe-oneZero market data partnership (undated, likely 2018, older) - [The TRADE](https://www.thetradenews.com/cboe-and-onezero-financial-team-up-on-market-data/)
- Market Data Sentinel (6 Nov 2018, older) automates exchange-data reporting on MT5. It is not an FX feed - [Finance Magnates](https://www.financemagnates.com/forex/technology/onezero-reveals-new-market-data-solution-for-mt5-brokers/)

**Centroid Solutions (bridge/connectivity)**
- 15 Apr 2025: integrated the ICE Consolidated Feed, including "price streaming for spot prices in over 2,700 FX pairs". No licensing terms stated - [TradeInformer](https://tradeinformer.com/tech-news/centroid-enhances-stock-market-access-with-ice-data-services-consolidated-feed-integration)
- Sept 2025: integrated Cboe US/EU market data (equities, options, indices, not FX); serves "350+ firms" including prop firms (search snippet) - [FX News Group](https://fxnewsgroup.com/forex-news/platforms/centroid-solutions-integrates-with-cboe-market-data/)

**Tools for Brokers (TFB)**
- The prop package includes "a bridge connection to liquidity providers, an aggregated price feed, an execution engine for real market simulations on demo accounts" and risk management; Trade Processor connects 100+ LPs. No price (search snippets; t4b.com returned HTTP 403 to the fetcher) - [TFB for prop trading](https://t4b.com/about-us/blog/tfb-for-prop-trading-whats-in-the-new-offer/); [TFB + Red Acre](https://t4b.com/about-us/news/tfb-launches-a-prop-trading-package-in-partnership-with-red-acre/)

**Gold-i**
- 22 Jul 2025: MatrixNET lets prop firms "simulate real market trading conditions", is "integrated with over 80 Liquidity Providers and 35 crypto exchanges" and connects to TradeLocker and Devexperts. Pricing model: per-user fees during evaluation, transaction-based fees once traders are funded - [FX News Group](https://fxnewsgroup.com/forex-news/platforms/gold-i-sees-rise-in-demand-from-prop-trading-firms-for-matrixnet/)

**Leverate**
- LXFeed: "real-time data for more than 1,000 instruments" (19 Apr 2017, older) - [Finance Magnates](https://www.financemagnates.com/forex/technology/leverate-unveils-new-improvements-lxtools-lxfeed-suite/)
- A search summary reported a one-time setup fee of USD 25,000-50,000 for Leverate solutions, with an unclear source. Unverified - [Startupim Leverate overview](https://slimpages.startupim.com/company_page/leverate)

**Per-provider matrix (all "not stated" values mean no source found; see the findings above for each source)**

| Provider | Type | Redistributable feed to third-party platforms? | Licensing model | Published or reported price | White-label retail display | Simulated / non-trading use | Minimums | Onboarding / regulatory | Contact |
|---|---|---|---|---|---|---|---|---|---|
| LMAX Exchange | FCA MTF (venue) | Not stated in PDF or page; Pyth says brokers redistribute (secondary) | Flat monthly per depth tier and asset class; free or 50% off with volume | Combined FX+metals TOB USD 1,500/mo, FX TOB 1,200, metals TOB 500, +USD 500/venue FIX (1 Jun 2026) | Not stated | Non-trading clients accepted for data but throttled to 10 updates/s | USD 250M/month per tier per venue for free TOB | Institutional onboarding as MTF client; criteria not published | institutionalsales@lmax.com, +44 20 3192 2682 |
| Cboe FX | ECN (venue) | Via vendors (dxFeed) or direct to participants | Per entitlement (session) per month | Prints/Snap add-on USD 1,000-7,500/mo by entitlement count (1 Aug 2026); base ECN feed price not found | Not stated | Not stated | Not stated | Participant/User Agreement | fxtradedesk@cboe.com |
| EBS (CME) | Venue | Not verified | Subscription (EBS Live, Data Mine) | Not found; USD 50k/mo figure unverified | Not stated | Not stated | Not stated | CME data licence | CME market data team |
| LSEG (FX Matching, Real-Time) | Venue + distributor | Yes, redistribution programme | "pay-as-you-grow", entitlements | Not published | Programme exists; terms not public | Not stated | Not stated | Enterprise contract | +1 800 427 7570 (Americas) |
| ICE (Consolidated Feed, OTC FX) | Distributor | Not stated | Not stated | Not published | Not stated | Not stated | Not stated | Enterprise contract | ICE "Contact us" |
| Integral / TrueFX | ECN tech + data portal | Not stated | Flat monthly | USD 4,950/mo Professional, 7,450 Institutional | Not stated | Not stated | Not stated | Not stated | truefx.com contact form |
| dxFeed | Data vendor | Yes for brokers (DXtrade, oneZero), terms private | Custom | Not published (retail from USD 19/mo, secondary, earlier note) | Likely (powers DXtrade prop firms), terms private | Used by sim prop platforms (DXtrade) | Not stated | Vendor contract | Sales form |
| B2Broker / B2Prime | PoP LP + tech | "liquidity or data feed" in cTrader prop bundle | Bundled with platform/liquidity | Liquidity min USD 1,000/mo + USD 10,000 deposit (2022, older) | Bundle is white label | Prop bundle implies yes | USD 1,000/mo (2022) | Institutional/professional clients, KYC | b2broker.com sales |
| Finalto | PoP LP | No data-only offer found | Liquidity | Not published | Not stated | Not stated | Not stated | FCA/CySEC LP, institutional KYC | finalto.com |
| Swissquote | Bank LP | No data-only offer found | Liquidity | Not published | Not stated | Not stated | Not stated | Bank KYC | Local office |
| Saxo | Bank/broker | No | Client API | n/a | Forbidden without written permission | n/a | n/a | Client account | n/a |
| Interactive Brokers | Broker | No | Per-subscriber | n/a | Forbidden ("not allowed to transfer or disclose") | n/a | n/a | Client account | n/a |
| IG | Broker | Not verified | n/a | n/a | Not verified | n/a | n/a | n/a | labs.ig.com |
| PrimeXM | Bridge | Pipe only; data from LPs/vendors | Max(instance, connectivity, volume, tx) | XCore from USD 2,000/mo | Depends on upstream licence | Supports reference feeds | USD 2,000/mo | Tech contract | primexm.com |
| oneZero | Bridge/hub | Pipe; dxFeed packages via ecosystem | Not published | Not published | Upstream licence | Not stated | Not stated | Tech contract | onezero.com |
| Centroid | Bridge | Pipe; ICE feed integrated | Not published | Not published | Upstream licence | Not stated | Not stated | Tech contract | centroidsol.com |
| TFB | Bridge | "aggregated price feed" in prop package | Not published | Not published | Upstream licence | Yes ("real market simulations on demo accounts") | Not stated | Tech contract | t4b.com |
| Gold-i | Bridge/risk | Pipe; 80+ LPs | Per user (evaluation), per transaction (funded) | Not published | Upstream licence | Yes (simulated LP execution) | Not stated | Tech contract | gold-i.com |
| Leverate | Platform vendor | LXFeed (2017) | Setup fee + licence | USD 25-50k setup (unverified) | White-label platforms | Not stated | Not stated | Tech contract | leverate.com |

### Inferences
- **Realistic for a small Swedish company without a brokerage licence (shortlist):** (1) LMAX Exchange, combined FX+metals TOB at USD 1,500/month plus USD 500 FIX per venue. It is the only published venue price with metals, but we must get written redistribution and white-label display rights and accept the 10 updates/s throttle as a non-trading client. That throttle is probably acceptable for a simulated web terminal, but it needs confirming. (2) dxFeed composite FX, already proven in simulated prop through DXtrade, price unknown. (3) B2Broker's prop bundle data feed. (4) FXCM and Match-Trade from the earlier note. TrueFX at USD 4,950/month is more expensive than Tiingo or Twelve Data (earlier note), and its terms are unknown.
- **Unrealistic as a direct source:** Cboe FX direct (participant agreements, priced per session), EBS/CME direct, LSEG and ICE (enterprise contracts, no public price, likely well above our budget) and all retail broker APIs (Saxo and IB forbid it; IG unverified).
- Bridge vendors (PrimeXM, oneZero, Centroid, TFB, Gold-i) only make sense once there is real flow to hedge. For pure display they add USD 2,000+/month without solving the licence question.
- LMAX's throttle (10 updates/s) is applied per FIX session. One server-side session fanned out to all tenants would see at most 10 price updates per second across the session, so it is unclear whether that is 10/s per instrument or in total. This is critical for 12 instruments and must be asked.

### Gaps
- No published Cboe FX base market data fee (only the Prints/Snap add-on), no EBS/CME fee lines (CME pages timed out or blocked the fetcher), and no LSEG, ICE or dxFeed FX prices.
- No provider's page states whether display to retail end users under another company's brand is allowed. This must be asked in writing.
- Finalto, IG and TFB primary pages could not be read.

## 2. What do FX venues charge for market data and redistribution (LMAX, Cboe FX, EBS/CME, LSEG Matching/Refinitiv, Integral)?

### Takeaway
Only LMAX and Cboe FX publish current FX data fee schedules (LMAX from 1 June 2026, Cboe FX Prints/Snap from 1 August 2026). Both price per connection or entitlement per month, not per end user, and neither schedule mentions display, non-display or redistribution categories. EBS/CME, LSEG and ICE did not expose FX fee lines. Integral's TrueFX is USD 4,950-7,450/month. None of the venues offers a published "redistribution to retail display" licence for spot FX. Equity-style categories (display, non-display, per-user) do not appear in any FX venue schedule found.

### Cited Findings
- **LMAX Exchange (effective 01 June 2026, monthly):** FX TOB USD 1,200 standard / free for trading clients; combined FX & metals TOB USD 1,500 / free; depth up to 10 levels USD 15,000 combined (USD 7,500 for trading clients); FIX connection USD 500 per venue (LD4/NY4/TY3); ITCH LD4 USD 80,000 (USD 25,000 with volume above USD 25bn and 30% aggressive/passive ratio); non-qualifying clients throttled to 10 updates/s. Exact lines are in section 1 - [LMAX fees PDF](https://www.lmax.com/documents/LMAXExchange-Market-Data-Fees.pdf)
- **Cboe FX Prints & Snap Fee Schedule "Effective 1 August 2026":** "All users subscribed to market data feeds with Cboe FX Prints and/or Cboe FX Snap Entitlements are subject to the fees listed below." Monthly fee by number of Prints/Snap entitlements: "1-4 $1,000"; "5 - 10 $2,500"; "11 - 15 $5,000"; "16+ $7,500". "Prints and/or Snap fees apply to all enabled ITCH and/or FIX Bookfeed sessions with at least one login during the billing month. A single user ID with both Prints and Snap entitlements is counted as two." "Entitlement counts are determined at the enterprise level across the NY5 and LD4 data centers." The fees amend "the User Agreement or Participating Financial Institution Agreement". Contact: "fxtradedesk@cboe.com", London +44 (0)20 7131 3450 - [Cboe FX Prints/Snap fee schedule](https://cdn.cboe.com/resources/fx/Cboe_FX_Prints_Cboe_FX_Snap_Feeds_Effective_20260801.pdf)
- Cboe FX market data page: ITCH real-time event stream or "FIX Bookfeed API (50ms)" snapshots, full depth; "available directly to clients as well as via leading market data vendors". No fees on the page - [Cboe FX market data](https://cboe.com/market_data_services/global/fx/)
- Cboe FX Market Maker pricing (Jan 2026): USD 3.50 per million for passive ADV under USD 750M (search snippet, PDF not opened). These are trading fees, shown for context only - [Cboe FX MM pricing Jan 2026](https://cdn.cboe.com/resources/fx/CboeFX_MarketMakerPricing_jan_2026.pdf)
- General Cboe licensing process: sign the Cboe Global Data Agreement, then request data through the Onboarding Portal (search summary of the Cboe document library; not FX-specific) - [Cboe market data document library](https://ww2.cboe.com/market_data_services/document_library)
- **EBS (CME):** EBS Live is described as live streaming prices "direct from EBS to the customer's market data distribution platform"; EBS Data Mine holds historical data from 1997 (search summary; fetch timed out) - [CME EBS data and analytics](https://www.cmegroup.com/markets/ebs/ebs-data-and-analytics.html). The CME 2027 Market Data Fee Notice (10 Aug 2026) says DataMine subscription fees rise 3.5% at 2027 renewal; EBS lines were not readable (search summary) - [CME 2027 fee notice](https://www.cmegroup.com/market-data/files/2027-market-data-fee-notice-august-10-2026.pdf)
- Conflict / unverified: a search-engine summary claimed "EBS Live currently has a $50,000 per month subscription fee". The only matching article found is from 19 Feb 2001 (older) and contains no figures - [FX Markets 2001](https://www.fx-markets.com/node/1543822). Treat the USD 50k figure as unverified.
- Bloomberg BFIX will include EBS Market spot transactions, implemented in early 2026 depending on consultation - [Bloomberg press](https://www.bloomberg.com/company/press/bloomberg-bfix-expands-spot-fx-data-sources-with-inclusion-of-ebs-market-transactions); [The Full FX](https://thefullfx.com/bloomberg-to-add-ebs-market-data-to-bfix/)
- **LSEG:** the redistribution programme offers "Clear entitlements, Transparent licensing, A pay-as-you-grow framework" for "financial institutions, FinTechs, RegTechs, and data-driven technology providers"; covers "Real-Time, Pricing, Reference and Tick History data". No fees published - [LSEG data redistribution](https://www.lseg.com/en/data-analytics/market-data/data-redistribution). FX Matching is a primary venue for Asian, Scandinavian, Eastern European, EM and major pairs; no data fee found - [LSEG FX Matching](https://www.lseg.com/en/fx/venues/spot-matching-forwards-matching)
- **ICE:** OTC FX data has "over 3,200 spot rates" from "leading market makers, trading platforms, execution venues, banks and brokers", streaming 24/7, via the ICE Consolidated Feed. No fee or licence terms published - [ICE OTC FX](https://developer.ice.com/fixed-income-data-services/catalog/ice-otc-data-services-foreign-exchange-fx). Scale reference only (futures, not spot): ICE Futures Europe annual redistribution licence USD 30,000 for non-members (search snippet of an older fee overview) - [ICE Fee Overview](https://www.ice.com/publicdocs/Fee_Overview.pdf)
- **Integral:** TrueFX USD 4,950/month or USD 7,450/month (section 1) - [TrueFX](https://www.truefx.com/). In June 2025 Integral connected clients to CME's EBS Market and FX Spot+ "with zero technology investment or additional fees" (execution, not data; search snippet) - [Integral](https://www.integral.com/?p=5143)

### Inferences
- FX venue data is priced as flat monthly fees per connection or entitlement. That suits a single server-side session fanned out to many tenants, provided the venue grants external display rights. No venue publishes such a right, so it is a negotiation item in every case.
- LMAX at USD 1,500/month (FX+metals TOB) is about the same cost as Twelve Data Enterprise (earlier note) but with venue-grade, no-last-look prices and metals included.
- Cboe FX's Prints/Snap fees are an add-on on top of the base ECN feed. Cboe FX's real cost for a non-participant redistributor is unknown, and the agreements imply participant status.

### Gaps
- Cboe FX base data fee and data policy (display/redistribution) not found. EBS Live/Ultra fee lines not retrieved. LSEG Real-Time FX and FX Matching data fees not public. ICE FX feed fees not public.
- LMAX redistribution and white-label display terms are not stated anywhere public.

## 3. Can a feed be had cheaply or free in exchange for flow, referrals or a per-funded-account fee, and what would that require?

### Takeaway
Yes, but only against real traded volume, which we do not have today. LMAX makes TOB free for trading clients with at least USD 250M traded per month per tier per venue (5 and 10 levels need USD 5bn). Gold-i charges per user during evaluation and per transaction once funded. B2Broker's liquidity has had a USD 1,000/month minimum and a USD 10,000 deposit (2022). FX-EDGE takes about 2% per funded account and FXCM discounts data for clients who execute (earlier note). Every flow-for-data deal requires an account with a regulated LP: KYC of Ludware (or of each prop firm), a margin deposit, and in practice real hedging of funded accounts.

### Cited Findings
- LMAX: trading client discounts make TOB "Free" and halve depth fees; eligibility "$250 million minimum traded volume per month, per tier, per venue"; "$5 billion minimum traded volume per month for access to 5 and 10 levels"; non-qualifying clients get FIX data "throttled to 10 updates per second" - [LMAX fees PDF](https://www.lmax.com/documents/LMAXExchange-Market-Data-Fees.pdf)
- Gold-i: per-user fees during the evaluation phase, then transaction-based fees once traders move to funded trading (22 Jul 2025) - [FX News Group](https://fxnewsgroup.com/forex-news/platforms/gold-i-sees-rise-in-demand-from-prop-trading-firms-for-matrixnet/)
- B2Broker: monthly minimum USD 1,000 (from USD 1,250) and minimum deposit USD 10,000 (from USD 20,000) (Dec 2022, older) - [Finance Magnates](https://www.financemagnates.com/thought-leadership/b2broker-announces-fresh-institutional-liquidity-offer/); B2Broker's cTrader prop bundle includes "liquidity or data feed" (Nov 2025) - [Spotware](https://www.spotware.com/zh/news/b2broker-launches-ctrader-white-label-prop-trading-solution/)
- PrimeXM: the fee is the maximum of instance (USD 2,000), connectivity, volume (fiat margin USD 0.50 per million) and transaction fees, so volume replaces rather than adds to the floor once it exceeds USD 2,000 - [PrimeXM XCore pricing](https://primexm.com/xcore/pricing/)
- Integral offered EBS/FX Spot+ access "with zero technology investment or additional fees" for execution clients (Jun 2025, search snippet) - [Integral](https://www.integral.com/?p=5143)
- B2Prime serves "institutional and professional clients" (2025, search snippet) - [Finance Magnates](https://www.financemagnates.com/thought-leadership/b2prime-unveils-q2-2025-growth-as-founder-shares-key-milestones/); Finalto serves "professional and institutional clients" (secondary) - [Finance Magnates directory](https://directory.financemagnates.com/forex-liquidity-provider/finalto)

### Inferences
- USD 250M/month notional is about 2,300 standard EURUSD lots a month (at about USD 108k notional per lot), roughly 100 lots per trading day. Reaching that needs aggregated, actually hedged funded flow from several tenants. Many prop firms do not hedge funded accounts, so it is not a near-term route.
- A per-funded-account fee model (FX-EDGE 2%, Gold-i per user) fits our multi-tenant model better than a volume threshold, but the LP will want to see the funded accounts and probably hedge them. That makes Ludware, or each prop firm, the LP's client, with KYC and contracts.
- Referral deals (LP pays or discounts for prop firms we introduce) were not found in any public source. They may be negotiable, but nothing citable exists.
- Realistic sequence: pay a flat data fee now (LMAX USD 1,500/mo or a vendor from the earlier note). Once tenants have funded flow worth hedging, renegotiate with the same LP, or with B2Broker or FXCM, for free or discounted data against that flow.

### Gaps
- No published "data for referrals" programme from any provider.
- The LMAX eligibility wording "per tier, per venue" was not clarified (does USD 250M on LD4 qualify TOB only on LD4?).

## 4. Are there regulatory constraints in the EU (Sweden) for a non-regulated technology company receiving and redistributing such feeds?

### Takeaway
No specific EU or Swedish licence was found for receiving or redistributing spot FX or spot metals quotes. Spot FX is outside MiFID II, and ESMA is "not currently engaged in any substantive discussions regarding retail prop trading" (CySEC chairman, July 2026). The binding constraints are commercial and contractual instead. Regulated LPs and venues serve "institutional and professional clients" and will run KYC on Ludware. Broker licences forbid passing data to third parties. Any later routing or hedging of funded flow in CFDs moves into MiFID territory.

### Cited Findings
- "Spot foreign exchange trading is not covered by" MiFID II; all FX products except spot are in scope; ESMA had considered bringing spot FX into market abuse rules (2017, older, search summary) - [ECB OMG: MiFID II impact for the FX market (2017)](https://ECB.eu/paym/groups/pdf/omg/2017/201706/2017-06-22_Item2_MiFID_II_impact_for_the_FX_market.pdf); [Euromoney on MAR and spot FX (older)](https://www.euromoney.com/article/b12kpl59bb140d/more-clarity-needed-on-impact-of-mar-on-spot-fx)
- 2 Jul 2026: CySEC chairman George Theocharides said "ESMA is not currently engaged in any substantive discussions regarding retail prop trading"; the article calls the model "legally ambiguous" because many firms present themselves as educational platforms - [Cyprus Mail](https://cyprus-mail.com/2026/07/02/retail-prop-trading-not-an-esma-priority-says-cysec-chairman)
- National warnings: Consob (Jul 2024), FSMA and CNMV issued prop-firm risk warnings (secondary blog) - [NYC Servers blog](https://newyorkcityservers.com/blog/prop-firm-rule-changes-2026); the FCA pursues firms whose model "crosses the line into regulated territory" (secondary) - [iDenfy](https://idenfy.com/?p=42738)
- Counterparty restrictions come from the providers' own regulation: LMAX Exchange is an FCA-authorised MTF - [LMAX fees PDF](https://www.lmax.com/documents/LMAXExchange-Market-Data-Fees.pdf); B2Prime is CySEC/SFSA/FSCA/FSC regulated and serves institutional and professional clients - [Finance Magnates](https://www.financemagnates.com/thought-leadership/b2prime-unveils-q2-2025-growth-as-founder-shares-key-milestones/)
- Contractual bans on passing data on: Saxo "not for resale or other transfer" - [Saxo developer](https://www.developer.saxo/excel/user-guide/enabling-market-data); IB/GFIS "not allowed to transfer or disclose Data to third parties" - [GFIS agreement](https://www.lynxbroker.de/media/doc/3089_GLOBAL_FINANCIAL_INFORMATION_SERVICES_SUBSCRIBER_AGREEMENT_ENG.pdf)

### Inferences
- Receiving an OTC dealer or venue feed and showing it in a simulation is not a regulated activity in itself. The data contract is what governs it. The earlier note's warnings still apply: no LBMA benchmark, and indices or futures reintroduce exchange licences.
- Our displayed prices are spot FX/metals quotes, but prop firms often describe instruments as CFDs. If Ludware later transmits funded traders' orders to an LP in CFDs, that is likely "reception and transmission of orders" in financial instruments, a MiFID investment service needing a licence or a regulated partner. This is an inference, not verified against Swedish law in this session. Hedging done by the prop firm in its own name is the prop firm's own trading, not Ludware's.
- An MTF such as LMAX normally onboards only professional clients or eligible counterparties. Whether a non-regulated Swedish tech company qualifies as a data-only client is unknown. LMAX's page says data is "available to all clients, regardless of size or activity levels", but that refers to existing clients.
- The EU Benchmarks Regulation is unlikely to apply: our prices determine simulated P/L, not amounts payable under financial instruments. This was not researched in this session; flag it for legal review if payouts are contractually tied to our prices.

### Gaps
- No primary EU or Swedish (Finansinspektionen) source specifically on market data redistribution by unregulated firms. The exact spot-FX definition (Delegated Regulation (EU) 2017/565, Art. 10) was not fetched.
- Whether an LP's KYC accepts an unregulated Swedish AB as a data-only client is not stated by any provider.

## 5. Which providers cover XAUUSD and XAGUSD spot as well as the 10 FX pairs?

### Takeaway
Spot gold and silver alongside FX are confirmed for LMAX (separate "Metals only" fee schedule and a combined FX & Metals schedule), Cboe FX via dxFeed ("Precious metals", 2020), ICE (gold, silver, platinum, palladium), EBS (precious metals, per CME catalog snippet) and B2Broker (spot FX and metals). Finalto and Swissquote list precious metals but as CFD or rolling-spot products. TrueFX and dxFeed's composite pages do not mention metals. No source gives a per-symbol list confirming all 12 instruments (including EURGBP, EURJPY, GBPJPY crosses), so this must be checked against each provider's symbol list.

### Cited Findings
- LMAX: separate "Metals only" tiers (TOB USD 500) and "Combined schedule (FX & Metals)" (TOB USD 1,500) - [LMAX fees PDF](https://www.lmax.com/documents/LMAXExchange-Market-Data-Fees.pdf); spot market data across "FX, metals, commodities, equity indices, digital assets and Asian NDFs" - [LMAX market data](https://www.lmax.com/exchange/market-data)
- Cboe FX via dxFeed: "70 of the most widely traded currency pairs in the world, including CLS-Settled; emerging market; and Precious metals" (Oct 2020, older) - [dxFeed + Cboe FX](https://dxfeed.com/forex-feed-from-cboe-fx-is-available-via-dxfeed-data-provider/)
- ICE OTC FX: "Gold, Silver, Platinum & Palladium, at various fixings & weights", plus 3,200+ spot FX rates - [ICE OTC FX](https://developer.ice.com/fixed-income-data-services/catalog/ice-otc-data-services-foreign-exchange-fx)
- EBS Spot FX data covers "major currency pairs and precious metals" (search summary of the CME catalog; fetch timed out) - [CME EBS Spot FX](https://www.cmegroup.com/market-data/browse-data/catalog/ebs-spot-fx.html); Integral offers access to "FX spot, NDF and precious metals liquidity" on EBS (search snippet) - [Integral](https://www.integral.com/?p=5143)
- B2Broker: volume fees for "Spot FX, Metals, Indices, Energies, and Commodities" (2022, older) - [Finance Magnates](https://www.financemagnates.com/thought-leadership/b2broker-announces-fresh-institutional-liquidity-offer/)
- Finalto: "forex and precious metals" in "CFD and rolling spot products" (secondary) - [Finance Magnates directory](https://directory.financemagnates.com/forex-liquidity-provider/finalto)
- Swissquote: precious metals listed as a solution category; XAU/XAG spot not detailed - [Swissquote brokers](https://www.swissquote.com/en/institutional/clients/brokers)
- TrueFX: "16+ major currency pairs" in history; no metals mentioned - [TrueFX](https://www.truefx.com/). dxFeed composite FX page: no metals mentioned - [dxFeed FX](https://dxfeed.com/market-data/fx/)
- LBMA benchmark (not spot quotes) needs an IBA licence (earlier note, section 2).

### Inferences
- LMAX is the cleanest single source for all 12 instruments with one published price, assuming its 250+ instruments include the three JPY/GBP crosses. That is typical for an FX ECN but not verified.
- Finalto, B2Broker and Swissquote price metals as CFDs or rolling spot. The quote is usable as a spot price for display, but the licence must cover "CFD prices", which FXCM says it generally allows (earlier note).

### Gaps
- No provider symbol list was fetched to confirm EURGBP, EURJPY, GBPJPY, XAGUSD for each provider.
- Cboe FX's current metals coverage (2026) not confirmed from Cboe itself.
