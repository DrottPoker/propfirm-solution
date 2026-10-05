# Produktplan: Kronant Trader och Kronant Prop

Senast uppdaterad: 2026-10-06

## Sammanfattning

Vi bygger två separata produkter som säljs till små och nystartade propfirms:

1. **Kronant Trader**, handelsplattformen: en webbaserad plattform för simulerad handel, i stil med TradeLocker och cTrader men utan riktig orderutförande. Den är vårt eget varumärke och inte white label. Varje firma har en egen server, och traders loggar in med firmans server som i MetaTrader och TradeLocker.
2. **Kronant Prop**, propfirm-plattformen: allt som behövs för att driva en challenge-verksamhet, det vill säga challenges, regelmotor, traderportal, adminpanel och utbetalningsflöde. Den är white label med firmans namn, logga, färger och domän, och använder vår handelsplattform.

Produkterna kan säljas tillsammans som ett billigt allt-i-ett-paket eller var för sig. De byggs med ett tydligt gränssnitt emellan. Bolaget bakom heter Ludware, och produkterna delar namnet Kronant (ADR 0046).

Kunden kommer igång själv via vår portal, utan säljsamtal. Det är, tillsammans med öppen simulering och fast pris utan intäktsdelning, det som skiljer oss från konkurrenterna (se Positionering).

## Bakgrund och beslut

Idén började som en egen propfirm. Den valdes bort på grund av:

- Risken med utbetalningar till traders.
- Regulatorisk gråzon: modellen kan klassas som investeringstjänst eller spel, och konsumentskyddsregler gäller.
- Höga kostnader för färdiga plattformar.
- Oklar skatt på utbetalningar till svenska privatpersoner (risk för arbetsgivaravgifter).

Att i stället sälja tekniken ger:

- Ingen risk för utbetalningar till traders.
- Mindre regleringsrisk, eftersom kunderna är företag och inte konsumenter.
- Återkommande intäkter.
- En affär som bygger på utvecklarkompetens.

## Målgrupp

- **Primär:** små och nystartade propfirms inom forex och CFD som tycker att befintliga lösningar är för dyra.
- **Senare:** brokers. De kräver riktig orderutförande, ställer hårda krav på leverantörer (bland annat DORA inom EU) och har långa säljprocesser. Börja med simulerade demokonton och tradingtävlingar för dem.
- **Möjlig spridning av risk:** tradingutbildare, communities och tävlingar som behöver simulerad handel.

## Marknad och konkurrens

| Lager | Exempel på befintliga aktörer |
|---|---|
| Allt-i-ett: egen plattform och challenge-system | Fintatech, TradeLocker, Match-Trader |
| Handelsplattform | Match-Trader, cTrader, DXtrade, TradeLocker |
| Challenges, CRM och utbetalningar | FPFX (150+ firms), TradeCore, Axcera, Track360, FXPropTech, B2Prop |
| Risk och fuskdetektering | QuantSentry, Centroid PropShield, AltimaCRM, PropForge |

Priser hos konkurrenterna enligt deras egna sidor, lästa 2026-10-04 om inget annat anges. Många tar bara offert, och verkliga priser kan förhandlas.

**Allt-i-ett med egen handelsplattform**

| Lösning | Pris | Kommentar |
|---|---|---|
| Fintatech | Starter: €1 000 i start, €0/mån upp till 50 aktiva konton, sedan €9,90/konto. Professional: €3 000 i start, €1 750/mån för 350 konton, sedan €6,50/konto. Advanced €3 500/mån (750 konton), Enterprise €7 000/mån (1 600). | Närmast vår idé. Starter har bara standardregler och delad server. Egna challenge-regler och egen server kräver Professional. Prisdata ingår. Demosamtal krävs, och nivån går bara att byta en gång per år. Det står inte vad ett aktivt konto är. |
| TradeLocker | $3 000/mån för upp till 500 aktiva konton, ingen startavgift. | Bara plattformen, challenge-systemet kopplas in via API. Ett konto är aktivt om det haft minst en öppen position under månaden. Tillägget för fuskdetektering finns inte längre på sidan. |
| Match-Trader | White label från $2 500/mån. Turnkey med prop-CRM $4 000/mån. | Hur många konton som ingår står inte. |
| Leverate | €1 490/mån för 500 konton, sedan €2/konto. CRM €3 490/mån extra. | Testar en gratis startnivå sedan februari 2026, med okända gränser. |
| cTrader | Offert. "Från €6 000" för propfirms (2024), oklart om per månad. | Tidigare siffror på $8 000-25 000 i start har ingen källa. |
| DXtrade, Volumetrica, Quadcode | Offert. Quadcode white label från $17 500. | |

**Challenge-system ovanpå andras plattformar**

| Lösning | Pris | Kommentar |
|---|---|---|
| FXPropTech | $1 000/mån + $1 500 i start för 500 konton, $2 500/mån + $3 000 för 2 000 konton, $5 000/mån + $6 000 utan gräns. $2,50 per konto utöver. | API, webhooks, riskverktyg och eget varumärke först från $2 500/mån. Oklart om plattformslicensen ingår. En annan sida hos dem har andra siffror. |
| Execurve PropScale | €740/mån för 500 konton, €2 450 i start med någon annans plattform. | |
| B2Prop (B2Broker) | $1 000-3 000/mån (2024). | Produktsidan finns inte längre 2026. |
| FPFX, Axcera, Kenmore Design, YourPropFirm, Trade Tech Solutions, TradeCore | Offert | Axcera och Kenmore har fast pris utan avgift per konto. |

Med en plattform betalar en liten firma i praktiken $3 500-5 500/mån för ett challenge-system. Fuskverktyget QuantSentry kostar €600-1 200/mån till.

**Med intäktsdelning.** Leverantören tar ofta risken för utbetalningar och sköter betalningar och plattform. Riktar sig till influencers utan eget kapital.

| Lösning | Pris |
|---|---|
| Match-Prop (september 2026) | $2 500 i start. Firman får 30-45 % av bruttoförsäljningen, och Match-Prop behåller resten. |
| PropAccount | $3 000 i start. WL1: firman får 30 % av bruttoförsäljningen. WL2: firman får 50 % av nettot. |
| StartPropFirm.Today | $2 000 i start + $2 000/mån + 50 % i intäktsdelning, eller white label utan intäktsdelning för $3 000-4 000/mån och lika mycket i start. |
| PropSuite (YourPropFirm) | $2 749 i start + 50 % av nettovinsten + $5 per försäljning + 5 % av försäljningen. |

Som jämförelse kostar en egen MT5-licens runt $10 000/mån 2026, och MetaQuotes licensierar knappt propfirms längre.

Slutsatser:

- **Hela paketet kostar $2 000-4 000/mån** för en liten firma, med plattform och egna challenge-regler. Bara Fintatechs Starter är billigare, och den saknar egna regler.
- **Priset per konto utöver det som ingår** ligger på ca $2,50-10 (FXPropTech $2,50, Fintatech €4,50-9,90, TradeLocker ca $6 vid 500 konton).
- **Ingen erbjuder självbetjäning.** Alla vi har hittat går via demo, offert eller samtal. De snabbaste lovar lansering inom en vecka.
- **Det svåra är att sälja och få förtroende, inte att bygga.** Fintatech har sålt ett liknande erbjudande i minst ett år utan att synas.

Händelser som visar att firms behöver alternativ:

- MetaQuotes (MT4/MT5) begränsade propfirms tillgång kraftigt 2024.
- ProjectX blev exklusiv för Topstep i november 2025. Andra futures-firms fick byta plattform med kort varsel.
- Nya aktörer kan slå sig in: TradeLocker lanserades 2023 och har enligt en branschkälla 2,5 miljoner aktiva användare 2026.

## Positionering

Vi vinner inte på att vara billigast. Det finns redan erbjudanden för $1 000/mån och en gratisnivå för de minsta firmorna (Fintatech). Vi konkurrerar i stället med tre saker som ingen konkurrent erbjuder tillsammans:

1. **Kom igång själv.** Firman registrerar sig, provar allt i en sandlåda direkt och kan gå live inom ett dygn. Inget säljsamtal, ingen demo och ingen förhandling. Det passar ett litet team, eftersom försäljningen blir en del av produkten (se Kom igång själv).
2. **Öppen och verifierbar simulering.** Traders ser firmans inställningar för spread, avgifter och slippage, och prishistoriken bakom varje fyllning. Det bemöter misstanken om manipulerade priser på en mindre känd plattform.
3. **Fast och publikt pris utan intäktsdelning.** Priset står på webbplatsen. Konkurrenskraftigt men inte lägst.

Dessutom:

- **Ett eget varumärke för traders.** Handelsplattformen heter Kronant Trader hos alla firmor, som TradeLocker. Traders som känner igen den litar lättare på en ny firma, och varumärket växer med varje firma som använder den.
- **Lätt att byta till oss.** Importverktyg för traders och konton från andra plattformar, och möjlighet att exportera all data.
- **Skydd mot plattformsrisk.** Tydliga avtal och full dataexport bemöter oron efter MetaQuotes och ProjectX.
- **Kostnadsfördel mot rena CRM-leverantörer.** Egen handelsmotor betyder inga licensavgifter till tredje part. Det gäller mot leverantörer som bygger ovanpå andras plattformar, men inte mot Fintatech, TradeLocker och Match-Trader som har båda delarna.

## Kom igång själv

Firman ska kunna starta utan att prata med oss. Etablerade konkurrenter är byggda kring säljare och förhandlade avtal och har svårt att kopiera det.

### Flöde

```
registrera -> välj challenge-mall -> portalens logga och färger
-> firmanamn.<vår domän> och firmans server fungerar direkt -> prova i sandlådan
-> koppla betalning (köplänk eller webhook) -> kontroll av bolag och ägare
-> live -> egen domän när DNS är klar
```

Mål: sandlådan på några minuter, live inom ett dygn.

Läge: registreringen, firmans adress och server, challenge-mallen, logga och färger, sandlådan, fler administratörer och kopplingen med API-nyckel och webhooks finns (fas 6, se [specen för registreringen](spec/registrering.md)). Firman kan sälja challenges i portalen med Stripe, sin egen betalsida eller testbetalning i sandlådan (fas 8, se [specen för köp i portalen](spec/kop.md)). Firman betalar oss i förskott med kort för ett paket med platser för aktiva challenges, och går live genom att betala startavgiften och första månaden (fas 7, se [specen för platser och betalning](spec/platser-och-betalning.md)). Innan dess skickar firman uppgifter om bolaget, ägarna och sina villkor och betalar en handpenning, och vi granskar och godkänner den i vår egen adminvy, där vi också kan stänga av en firma (fas 9a, se [specen för granskning och avstängning](spec/granskning.md)). Kvar är egen domän.

### Krav

- **Färdiga mallar** för vanliga challenges, till exempel 100k i två steg, som firman kan justera.
- **Varumärke och domän för portalen.** Underdomän direkt. Egen domän med automatiskt TLS-certifikat. Handelsplattformen behåller vårt varumärke, och firman syns där som server och namn vid kontot.
- **Sandlåda med hela kedjan.** Firman skapar en challenge, handlar och ser regelmotorn godkänna eller stänga ett konto. Bara firmans egna testanvändare, så att kostnaden för prisdata hålls nere.
- **Kontroll innan live.** Sandlådan kräver ingen kontroll. Innan firman får ta emot riktiga traders granskar vi bolag, ägare och villkor själva, inom ett dygn. Firman betalar en handpenning när den skickar sin ansökan, som dras av från startavgiften och inte betalas tillbaka om den nekas. En automatisk kontroll kan läggas till före vår granskning. Det skyddar mot firmor som tar avgifter och aldrig betalar ut, vilket annars skadar vårt rykte.
- **Avtal i portalen.** Användarvillkor och personuppgiftsbiträdesavtal godkänns vid registrering.
- **Betalning i förskott** med kort: startavgift och ett paket med platser för aktiva challenges (se Affärsmodell och prissättning).
- **Hjälp med det som tar längst tid.** Plattformen är sannolikt inte det enda som försenar en ny firma. Erbjud färdiga integrationer mot betalleverantörer som accepterar propfirms, och guider för KYC-leverantör, villkor och bolag.
- **Bra dokumentation**, så att kunderna klarar sig utan support.

## Produkt 1: Kronant Trader (handelsplattform)

### Syfte

Låta traders handla på simulerade konton med riktiga livepriser. Ingenting skickas ut på marknaden.

### Användare

Traders hos firmorna. Plattformen är vårt eget varumärke och inte white label. Varje firma har en egen server. Tradern får sina inloggningsuppgifter från firmans portal och väljer firmans server när den loggar in, som i MetaTrader och TradeLocker.

### Version 1

- Forex och guld.
- Marknads-, limit- och stopordrar.
- Stop loss och take profit.
- Konton, positioner och kontovärde (equity) i realtid.
- Webbgränssnitt med graf, orderformulär, positioner och historik.
- Internt API mot propfirm-plattformen.

### Senare

- Fler tillgångsslag, till exempel krypto. Index först när datalicenserna är lösta.
- Mobilapp.
- API för algohandel.
- Kopieringsmotor som speglar bevisade traders till ett riktigt konto hos en mäklare.

### Tekniska krav

- **Realistisk simulering.** Ordrar fylls mot bid/ask med inställbar spread, avgifter, swap och slippage. Bredare spread vid nyheter och nattetid.
- **Skydd mot gamla priser.** Avvisa ordrar när senaste priset är för gammalt. Det förhindrar att traders utnyttjar fördröjning i prisflödet.
- **Logga allt.** Spara varje prisuppdatering och varje fyllning. Det behövs vid tvister och bygger förtroende.
- **Öppenhet mot traders.** Traders ser firmans inställningar för spread, avgifter, swap och slippage, och kan se prishistoriken bakom varje fyllning.
- **Kontovärde på varje prisuppdatering.** Equity räknas om inklusive öppna positioner.
- **Hög tillgänglighet.** Plattformen måste vara uppe hela tiden när marknaden är öppen (forex handlas dygnet runt fem dagar i veckan).

### Prisdata

- Börja med forex och guld. Index (till exempel NAS100 och US30) bygger på börsdata med dyra licenser.
- Att visa priser för kunders traders räknas som vidaredistribution. Billiga abonnemang förbjuder ofta det, och vissa leverantörer tar betalt per slutanvändare. Kontrollera villkoren och få ett skriftligt godkännande innan prissättningen bestäms.
- Exempel på leverantörer: Massive (från ca $49/mån), Live-Rates (från ca €50/mån), TraderMade och Finage (ca £300-600/mån). Priserna gäller vanliga abonnemang, inte vidaredistribution.
- Alternativ: låt varje firma ha sitt eget dataavtal.

### Grafer

TradingView Lightweight Charts är gratis och öppen källkod. TradingView ska anges som källa.

## Produkt 2: Kronant Prop (propfirm-plattform)

### Syfte

Allt en firma behöver för att sälja challenges, följa upp traders och hantera utbetalningar, utan att bygga något själv. Plattformen är white label: traders ser firmans namn, logga, färger och domän. Handeln sker på vår handelsplattform, där portalen skapar traderns inloggning hos firmans server.

### Delar

| Del | Används av | Innehåll |
|---|---|---|
| Regel- och riskmotor | Körs i bakgrunden | Kontrollerar challenge-reglerna i realtid |
| Traderportal | Traders | Inloggning, dashboard, köp av challenge, begäran om utbetalning |
| Adminpanel | Firman | Uppstart, challenges, regler, priser, traders, godkännande av utbetalningar, fakturering |
| Kassa | Traders | Kopplas till firmans egen betalleverantör, så pengarna går direkt till firman |
| API och webhooks | Firmans egna system | För firms med egen webbplats, kassa eller CRM |

Firmans marknadssajt (startsida, priser, vanliga frågor) ingår inte. Firman gör den själv, till exempel i Webflow eller WordPress, och får en köplänk eller en kassa som går att bädda in.

### Version 1

- En challenge i två steg med fyra grundregler.
- Dashboard för traders.
- Enkel adminpanel.
- Självbetjäning: registrering, challenge-mallar, varumärke, sandlåda och kontroll av kunden innan live (se Kom igång själv).
- Konton skapas via API eller köplänk.
- Utbetalningar som firman sköter själv och markerar som gjorda.

### Senare

- Affiliatesystem och rabattkoder.
- Fuskdetektering: flera konton per person (IP, enhet, betalmetod), speglade eller motsatta affärer mellan konton, latensarbitrage, nyhetshandel.
- KYC-koppling (till exempel Sumsub eller Veriff, via firmans eget avtal).
- Automatiska utbetalningar via firmans eget konto hos en utbetalningsleverantör (låt en jurist bekräfta upplägget först).
- Skalningsplaner och fler challenge-typer.
- Adaptrar mot andra handelsplattformar (Match-Trader, cTrader, DXtrade).

### Regler

Typiska regler som firman ska kunna ställa in:

- Vinstmål, till exempel 10 % i fas 1 och 5 % i fas 2.
- Max daglig förlust, till exempel 5 %.
- Max total förlust, till exempel 10 %, fast eller släpande.
- Minsta antal handelsdagar.
- För funded-konton ofta en regel om jämna resultat (konsistensregel).
- Inaktivitet: en challenge utan nya affärer på till exempel 30 dagar avslutas. Det frigör också firmans plats (se Affärsmodell och prissättning).
- Valfri tidsgräns per fas.

Inaktiviteten och tidsgränsen finns (fas 7), och räknas inte medan en challenge är pausad för att firmans månad är obetald.

Viktigt i designen:

- **Mät på equity**, inklusive öppna förluster, inte bara saldo.
- **Definiera "dag" exakt**: tidszon, klockslag och om daglig förlust räknas från saldo eller equity vid dagens start.
- **Handelsplattformens equity är facit.** Räkna inte på ett separat prisflöde.
- **Spara bevis för varje regelbrott**: tidpunkt, equity, priser och öppna positioner. Visa det för tradern.
- **Hantera trassliga händelser.** De kan komma dubbelt, sent eller i fel ordning. Samma händelse ska kunna tas emot flera gånger utan fel, och tillståndet ska stämmas av regelbundet.

Standardvärden, den exakta definitionen av dag och hur regelmotorn och handelsplattformen delar på ansvaret beskrivs i [specen för regelmotorn](spec/regelmotor.md) och [ADR 0011](adr/0011-regelmotorn-satter-golv.md).

### Kontots livscykel

```
betald -> fas 1 -> fas 2 -> KYC och avtal -> funded -> utbetalning -> funded ...
(regelbrott i valfri fas -> underkänd)
```

Byggs som en tillståndsmaskin med tillåtna övergångar och en logg över varje övergång.

### Utbetalningar

Vi hanterar aldrig själva pengarna. Att förmedla pengar åt andra kräver tillstånd hos Finansinspektionen (betaltjänst), och att föra över krypto åt andra kräver tillstånd enligt MiCA. Det medför också penningtvättskrav.

| Steg | Vem |
|---|---|
| Tradern begär utbetalning i portalen | Vi (portalen) |
| Kontrollera villkoren: minsta antal dagar, jämna resultat, inga regelbrott | Vi |
| Räkna ut beloppet, vinst gånger vinstandel, och ta ut hela vinsten från kontot | Vi |
| KYC-kontroll | Firmans KYC-leverantör |
| Godkänna | Firman, i adminpanelen |
| Skicka pengarna | Firman, via egen bank, utbetalningstjänst eller krypto |
| Markera som betald och spara historiken | Vi |

```
tradern begär -> vi kontrollerar, räknar ut och tar ut vinsten från kontot -> firman godkänner
-> firman betalar -> firman markerar som betald -> vi sparar historiken
```

Vinsten tas ut direkt, så att tradern inte kan förlora pengar som ska betalas ut medan firman kontrollerar. Kontot börjar om från startsaldot. Hur det fungerar beskrivs i [specen för regelmotorn](spec/regelmotor.md) och [ADR 0015](adr/0015-utbetalningar-tar-ut-vinsten-direkt.md). Regeln om jämna resultat finns inte än.

### API och webhooks

Exempel:

```
POST /api/firm/v1/accounts   { "email": "...", "challengeId": "two-step-100k", "reference": "order-17" }
-> startar challengen och öppnar kontot på handelsplattformen

Webhooks till firman:
account.passed       tradern klarade fasen
account.breached     tradern bröt en regel
payout.requested     tradern vill ta ut sin vinst, som är uttagen från kontot
```

API:t och webhooks som finns beskrivs i [specen för propfirm-tjänsten](spec/propfirm-tjanst.md).

## Gränssnitt mellan produkterna

- Handelsplattformen erbjuder ett publikt admin-API som alla firmors system kan använda: skapa traders och konton, sätta golv, stänga konton, skapa inloggningslänkar och läsa firmans händelser i ordning (ADR 0012). Propfirm-plattformen använder samma API som andra kunder.
- Propfirm-plattformen pratar med handelsplattformen genom en adapter. Samma adaptergränssnitt kan senare användas mot Match-Trader, cTrader eller DXtrade.
- Det gör att produkterna kan byggas och testas var för sig och säljas separat.
- Under utvecklingen kan en låtsasversion av handelsplattformen skicka påhittade händelser, så att regelmotorn kan byggas och testas fristående.

Flöde:

```
Firman (kassa, API eller adminpanel) -> propfirm-plattformen skapar konto i handelsplattformen
Trader -> inbjudan -> traderportal -> "Open terminal" -> handelsplattformen med en engångslänk
Trader -> handelsplattform -> affärer och kontovärde -> regelmotor
regelmotor -> beslut (fas klar eller regelbrott) -> propfirm-plattformen stänger kontot eller byter fas
```

## Arkitektur och teknik

Besluten beskrivs i detalj i [docs/adr](adr/README.md).

- **Kodbas:** ett repo med hårda gränser mellan produkterna som kontrolleras automatiskt (ADR 0001).
- **Webb, traderportal och adminpanel:** Next.js med TypeScript.
- **Backend (handelsmotor, regelmotor och API):** C# på .NET 10 med Postgres. Inbyggda decimaltal gör pengaberäkningarna exakta (ADR 0002).
- **Inloggning:** handelsplattformen har egna användare per firma, så samma e-postadress kan finnas hos flera firmor. Tradern väljer firmans server när den loggar in (ADR 0009).
- **Portalen:** en installation för alla firmor, där firman känns igen på adressen. Webbläsaren pratar bara med portalens adress, och inloggningen hålls av propfirm-tjänsten (ADR 0014).
- **Handelsmotor och regelmotor:** egna tjänster som körs hela tiden, inte serverless, eftersom de håller öppna anslutningar och aktuellt tillstånd i minnet. Kärnan i handelsmotorn är deterministisk (ADR 0005) och beskrivs i [specen för handelsmotorn](spec/handelsmotor.md).
- **Aktuellt tillstånd och kö:** motorn håller tillståndet i minnet och sparar ögonblicksbilder i Postgres. Händelser mellan produkterna läses från handelsplattformens händelseström (ADR 0012). Inget Redis i början (ADR 0003). Alla indata och händelser sparas i en journal i Postgres, så att tillståndet kan byggas upp igen efter en omstart (ADR 0008).
- **Kontrakt mellan produkterna:** handelsplattformens publika admin-API under `/api/admin/v1`. OpenAPI-dokumentet i `contracts/` är kontraktet (ADR 0012).
- **Terminalen och handelstjänsten:** REST för kommandon och SignalR för realtid. Gränssnittet räknar aldrig pengar (ADR 0006, [specen för handelstjänsten](spec/handelstjanst.md)).
- **Historik och analys:** Postgres till att börja med, TimescaleDB eller ClickHouse senare.
- **Pengar:** belopp sparas som heltal eller decimaltal, aldrig som flyttal. Avgifter och utbetalningar bokförs i en huvudbok där rader bara läggs till.
- **Flera kunder i samma system:** strikt isolering av varje kunds data.
- **White label i propfirm-plattformen:** egen domän, logga och färger per kund. Handelsplattformen har vårt eget varumärke.
- **Konfiguration per kund:** regler, challenges och priser.
- **Revisionsloggar:** varje beslut ska gå att spåra.
- **Fuskdetektering senare:** spara IP-adress, enhet och betalmetod per konto från början.

## Juridik

- Vi säljer till företag. Avtalet ska säga att firman ensam ansvarar för sitt erbjudande, sina regler och sina utbetalningar.
- Vi hanterar aldrig kundernas pengar (se Utbetalningar).
- GDPR: vi är personuppgiftsbiträde åt firmorna. Personuppgiftsbiträdesavtal krävs, och data bör lagras inom EU. Avtalet godkänns digitalt vid registrering.
- Ansvarsbegränsning i avtalen. En bugg som felaktigt stänger konton eller räknar fel på utbetalningar kan kosta kunden mycket.
- Kontrollera kunderna innan de går live: ägare, bolag och vilka villkor de har mot sina traders. Vi granskar själva i vår adminvy (se Kom igång själv). Avtalet ska ge rätt att stänga av kunder som lurar sina traders, och vår adminvy kan göra det.
- Brokers som kunder senare: DORA-krav på IT-leverantörer.

## Affärsmodell och prissättning

Firman betalar alltid i förskott. Vi fakturerar aldrig i efterhand, så en firma som går dåligt eller läggs ner kan inte lämna obetalda skulder. Traderns pengar går aldrig via oss (se Utbetalningar).

### Platser för aktiva challenges

Firman betalar varje månad för ett paket med ett antal platser, alltså hur många challenges den kan ha aktiva samtidigt, och kan köpa fler platser. Priset växer med firmans storlek. Priserna nedan är vårt förslag från 2026-10-04 och bekräftas efter offerterna för prisdata och intervjuerna.

| Del | Förslag |
|---|---|
| Startavgift, en gång | 700 USD, varav 200 USD är handpenning när firman skickar sin ansökan |
| Paket med 25 platser, per månad | 500 USD |
| Plats 26-100, per månad | 5 USD per plats |
| Plats 101 och fler, per månad | 4 USD per plats |

Per månad blir det 500 USD för 25 platser, 625 USD för 50, 875 USD för 100 och 2 475 USD för 500.

- **Paketet ingår alltid.** Dess platser är de färsta en firma kan ha.
- **En aktiv challenge tar en plats** från att den startar tills den är slut: underkänd, avbruten eller stängd. Ett funded-konto tar en plats så länge det finns. Alla faser i en challenge delar samma plats, även om varje fas får ett eget handelskonto.
- **När platserna är slut** kan inga nya challenges startas. Firmans butik ska då sluta sälja, så att ingen trader betalar för en challenge som inte kan startas. Firman ser lediga platser i adminpanelen och i API:t, och får en varning när till exempel 80 % är använda.
- **Fler platser** kan köpas när som helst och betalas direkt för resten av månaden. Firman kan välja automatisk utökning, till exempel 10 platser i taget, som dras från kortet direkt. Färre platser gäller från nästa månad, bara om de öppna challengerna får plats och aldrig färre än paketets.
- **Månadsbetalningen** dras från kortet 5 dagar innan månaden börjar. Går den inte igenom har firman de dagarna på sig. Är den inte betald när månaden börjar startas inga nya challenges, och befintliga konton pausas. Vi levererar aldrig något som inte är betalt.
- **Inaktivitet:** en challenge utan affärer på till exempel 30 dagar avslutas och frigör sin plats. Firman kan också avbryta konton själv.
- **Sandlådan** tar inga platser.
- **En order som väntar på betalning** i firmans portal håller en plats, så att köparen alltid kan få sin challenge.
- Avtalet säger att firman ansvarar för att betalningen går igenom, och att traders konton pausas annars.

### Priset

- **Varför ett paket.** En helhetslösning för 50 USD i månaden ser för billig ut, när konkurrenternas hela paket kostar 2 000-4 000 USD (se Marknad och konkurrens). Ett fast pris per firma täcker också det som kostar oss lika mycket för varje firma, till exempel prisdata och granskningen. Och det är lättare att sänka ett pris än att höja det.
- **Vi är ändå billigast** med plattform och egna challenge-regler i alla storlekar vi har jämfört: 875 USD vid 100 platser mot ca 2 050 USD hos Fintatech Professional och 4 000 USD hos Match-Trader.
- **Det måste täcka vad en trader kostar oss per månad.** Prisdata som får visas för andras traders kostar 250-500 USD i månaden som fast avgift hos Tiingo och Twelve Data, om ingen avgift tas per slutanvändare. Det måste bekräftas skriftligt innan priset spikas.
- **Det får inte äta upp firmans intäkt.** En challenge för 50 USD som är aktiv i 10 månader kostar firman 50 USD med 5 USD per plats, alltså hela intäkten. De flesta challenges tar slut inom några veckor eftersom de flesta traders misslyckas, men det ska bekräftas i intervjuerna. Skydd mot fallet:
  - Inaktivitetsregeln ovan.
  - Valfri tidsgräns per fas, som firman ställer in.
  - Lågt pris per plats, och lägre pris per plats ju fler platser firman köper.
- **Ett funded-konto som lever länge är en verklig kostnad**, både för oss och för firman. Firman räknar med den när den sätter sina priser.
- **De allra minsta firmorna** som säljer 20-30 challenges i månaden betalar 15-20 % av sin intäkt för paketet (egen uppskattning), och väljer kanske Fintatechs gratisnivå. Det accepterar vi, eftersom de ofta läggs ner snabbt. En firma som säljer 200 challenges i månaden betalar runt 3-4 %.
- **Firmorna är vana vid modellen.** Konkurrenterna säljer i nivåer med ett antal konton som ingår (TradeLocker, FXPropTech, Fintatech), och priset per konto utöver ligger på ca $2,50-10 (se Marknad och konkurrens).

### Övrigt

- Publik prislista på webbplatsen. Det hör till självbetjäningen.
- Startavgiften är 700 USD. Den är lägre än Fintatechs lägsta (ca 1 170 USD) och en femtedel av deras startavgift för egna regler, men inte så låg att den ser oseriös ut, och den betalar för granskningen. Pröva i intervjuerna om den stoppar seriösa firmor.
- Ingen gratisnivå med riktiga konton. Sandlådan är gratis för alla, med högst 10 testkonton, och den är vår viktigaste skillnad.
- Billigare än konkurrenterna, men inte gratis. För lågt pris skadar förtroendet och är svårt att höja senare.
- Korta avtal, eftersom små firms ofta läggs ner.
- Priserna är utan moms. Bolaget är svenskt, så svenska kunder betalar 25 % moms, företag i andra EU-länder med giltigt momsnummer betalar ingen (omvänd skattskyldighet) och företag utanför EU betalar normalt ingen. Firmor i EU anger därför sitt momsnummer i ansökan till vår granskning, eller att de saknar ett och då betalar svensk moms. Moms och egna fakturor byggs inför lansering. En redovisningskonsult ska bekräfta upplägget.
- Undvik stora intäktsdelningar. Det är det kunderna klagar på hos konkurrenterna.

## Drift

- Allt körs i containrar på en VPS per miljö i Stockholm, med Postgres och backup utanför servern, Caddy framför och mejl via Resend (ADR 0040).
- Stabilitet prioriteras före nya funktioner. Ett avbrott under en stor nyhetshändelse kan sänka förtroendet helt.
- Övervakning och larm dygnet runt under handelsveckan.
- Öppen statussida.
- Låg kostnad per kund: kunderna kommer igång själva (se Kom igång själv), och dokumentationen ska vara bra.

## Kundernas affärsmodell (bra att förstå)

- Traders betalar en avgift för en challenge på ett simulerat konto. De som klarar den får ett funded-konto och 80-90 % av vinsten.
- Bara en liten andel klarar challengen, och ännu färre får en utbetalning. Intäkterna kommer främst från avgifterna.
- Att kopiera alla funded-konton till riktiga marknaden blir ofta en förlustaffär. Seriösa firms kopierar bara traders som bevisat sig, ofta i mindre storlek först. Det är en framtida funktion för oss (kopieringsmotor).
- Firms vanligaste problem: utbetalningar som överstiger intäkterna, fusk, betalleverantörer som klassar branschen som högrisk, och ökande granskning från myndigheter (ESMA, FCA, CFTC).

## Risker

- **Kostnad och licensvillkor för prisdata** kan göra det låga priset omöjligt.
- **Traders kan utnyttja ett långsamt prisflöde.**
- **Förtroende.** Traders kan misstänka att priser manipuleras på en mindre känd plattform.
- **Traders föredrar ofta MT5, cTrader eller TradeLocker.** Vår plattform måste vara riktigt smidig.
- **Små firms läggs ofta ner**, vilket ger många uppsägningar och risk för obetalda fakturor.
- **Hela branschen hänger på regleringen.** Om reglerna skärps kan kunderna försvinna.
- **Drift dygnet runt under handelsveckan** är krävande.
- **Kunderna litar inte på en ny leverantör direkt.** Lanseringskund och referenser behövs.
- **Konkurrenter med liknande erbjudande.** Fintatech har redan egen plattform, prop-CRM och låga priser. Vår skillnad måste märkas: självbetjäning och öppen simulering.
- **Självbetjäning lockar oseriösa firmor.** Bedragare kan använda plattformen för att ta avgifter och aldrig betala ut. Kontroll innan live är ett krav.
- **Plattformen är kanske inte flaskhalsen.** Nya firmor kan fastna på betalleverantörer, bolag och KYC. Då väger snabb uppstart lättare.

## Nästa steg

1. Intervjua 10-20 små propfirms och personer som planerar att starta en: vad de betalar, vad som krånglar och vad som skulle få dem att byta. Fråga också:
   - Hur lång tid tog det från beslut till första sålda challenge, och vad tog längst tid?
   - Har ni tittat på Fintatech eller FXPropTech? Varför valde ni bort dem?
   - Hur länge är en challenge aktiv i genomsnitt, och hur stor andel blir funded? Det avgör vad en plats får kosta.
2. Ta in priser och licensvillkor för vidaredistribution från 2-3 dataleverantörer.
3. Gör en landningssida med väntelista, till exempel "Starta din propfirm i dag. Fast pris. Ingen intäktsdelning. Inget säljsamtal." Dela den där blivande grundare finns och mät anmälningarna.
4. Titta på Fintatechs produkt via deras demo. Var öppen med att du undersöker marknaden.
5. Skriv teknisk specifikation för version 1 av båda produkterna: datamodell, internt API, regelmotor, händelseflöden och flödet för självbetjäning.
6. Hitta en lanseringskund, till exempel en trading-influencer eller en nystartad firma, som får lågt pris mot feedback och referens.
7. Köp domänerna (ADR 0018) och registrera Kronant som EU-varumärke i klass 9, 36 och 42 (ADR 0046).

## Öppna frågor

- Vilken dataleverantör och vilka licensvillkor? Under utvecklingen används Tiingos gratisplan (ADR 0010), som inte får visas för andra. Tiingo har även en plan för vidaredistribution.
- Priserna har ett förslag (se Affärsmodell och prissättning). Det bekräftas efter intervjuerna och offerterna för prisdata.
- Bolagsform. Bolaget finns i Sverige.
- Vilka plattformar adaptrarna ska stödja först, utöver vår egen handelsplattform.
- Ska kontrollen av kunden bli automatisk före vår granskning, och med vilken leverantör?

## Källor

- [Match-Trade Broker API (sandbox och endpoints för propfirms)](https://app.theneo.io/match-trade/broker-api/introduction)
- [Spotware - How to Start a Prop Trading Firm 2026](https://www.spotware.com/news/how-to-start-a-proprietary-trading-firm-2026/)
- [Track360 - White Label Prop Firm Cost 2026](https://track360.io/blog/white-label-prop-firm-cost-providers-setup-2026)
- [Track360 - Prop Firm Platform Comparison 2026](https://track360.io/blog/prop-firm-trading-platform-comparison-dxtrade-ctrader-match-trader-2026)
- [Match-Trader - Match-Prop](https://match-trader.com/match-trade-technologies-powers-match-prop-a-new-solution-for-trading-influencers-and-young-entrepreneurs/)
- [FirmForge - How to Start a Prop Firm 2026](https://www.firmforge.cfd/how-to-start-a-prop-firm)
- [FPFX Tech](https://www.fpfxtech.com/)
- [TradeCore](https://tradecore.com/)
- [QuantSentry Review 2026](https://alexfirdaus.com/quantsentry-review/)
- [PropForge - Risk tools](https://propforge.io/features/risk-tools/)
- [FundedTrading - MetaTrader-alternativ 2026](https://fundedtrading.com/best-metatrader-alternative/)
- [For Traders - ProjectX 2026](https://fortraders.com/blog/project-x-trading)
- [For Traders - Tradovate Prop 2026](https://fortraders.com/blog/tradovate-prop)
- [Prop Firm Regulation News Q3 2026 - Track360](https://track360.io/blog/prop-firm-regulation-news-roundup-q3-2026)
- [Massive - Forex WebSocket](https://massive.com/docs/websocket/forex/overview)
- [Live-Rates](https://www.live-rates.com/)
- [TraderMade - FIX API](https://tradermade.com/market-data/fix-api)
- [Finage - Forex](https://finage.co.uk/product/forex)
- [Fintatech - Prop Firm Platform](https://fintatech.com/prop-firm/)
- [Fintatech - Bootstrapping your prop firm (september 2026)](https://fintatech.com/blog/bootstrapping-your-prop-firm-how-to-launch-with-zero-tech-risk-and-0-monthly-saas-minimums/)
- [Leverate](https://leverate.com/)
- [FXPropTech - Pricing](https://fxproptech.com/pricing.html)
- [Execurve - Pricing](https://www.execurve.com/pricing)
- [QuantSentry - Pricing](https://quantsentry.com/pricing)
- [FX News Group - Match-Prop (september 2026)](https://fxnewsgroup.com/?p=48485)
- [PropAccount - Pricing](https://propaccount.com/pricing/)
- [FundedTrading - PropSuite](https://fundedtrading.com/tech-provider/propsuite/)
- [Tiingo - Forex API](https://www.tiingo.com/products/forex-api)
- [Twelve Data - Business pricing](https://twelvedata.com/pricing-business)
- [Fintatech - FintaTrader 3.5](https://fintatech.com/blog/fintatrader-3-5-taking-trading-to-the-next-level/)
- [Tracxn - Fintatech](https://tracxn.com/d/companies/fintatech/__pQqs_s9ThCBfDV-1NP1nLU8GQ0xEa7MxDOk6ro6g2WQ)
- [TradeLocker - Prop Firm Pricing](https://tradelocker.com/prop-firm-pricing/)
- [Match-Trader - White label från $2 500 (nov 2024)](https://match-trader.com/match-trader-white-label-as-low-as-2500-and-server-from-5000/)
- [FXPropTech - White Label Prop Firm Setup](https://fxproptech.com/solutions/white-label-prop-firm-setup.html)
- [B2Broker - B2Prop](https://b2broker.com/news/b2broker-introduces-b2prop-prop-trading-firm-turnkey-solution)
- [PropAccount - How to Choose a White Label Provider](https://propaccount.com/resources/blog/how-to-choose-the-best-white-label-prop-firm-provider/)
- [StartPropFirm.Today](https://startpropfirm.today/)
- [FundedTrading - 6 Best White Label Providers 2026](https://fundedtrading.com/best-white-label-prop-trading-firm-providers/)
- [FundedTrading - cTrader review](https://fundedtrading.com/?p=22336)
