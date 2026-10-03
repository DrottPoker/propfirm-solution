# Produktplan: handelsplattform och propfirm-plattform

Senast uppdaterad: 2026-10-03

## Sammanfattning

Vi bygger två separata produkter som säljs till små och nystartade propfirms:

1. **Handelsplattform**: en webbaserad plattform för simulerad handel, i stil med TradeLocker och cTrader men utan riktig orderutförande. Den är vårt eget varumärke och inte white label. Varje firma har en egen server, och traders loggar in med firmans server som i MetaTrader och TradeLocker.
2. **Propfirm-plattform**: allt som behövs för att driva en challenge-verksamhet, det vill säga challenges, regelmotor, traderportal, adminpanel och utbetalningsflöde. Den är white label med firmans namn, logga, färger och domän, och använder vår handelsplattform.

Produkterna kan säljas tillsammans som ett billigt allt-i-ett-paket eller var för sig. De byggs med ett tydligt gränssnitt emellan.

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

Ungefärliga priser hos konkurrenterna (offentliga uppgifter, 2026). Många tar bara offert, och verkliga priser förhandlas ofta.

**Allt-i-ett med egen handelsplattform**

| Lösning | Pris | Kommentar |
|---|---|---|
| Fintatech | €1 000 i startavgift. €0/mån upp till 50 aktiva konton, sedan €9,90/konto. €1 750/mån (350 konton), €3 500/mån (750), €7 000/mån (1 600). | Närmast vår idé. Egen plattform (FintaTrader), CRM och riskmotor. Litet bolag, grundat 2018, utan externt kapital. Inga namngivna kunder hittade. |
| TradeLocker | $3 000/mån för upp till 500 aktiva konton, ingen startavgift. Tillägg, till exempel fuskdetektering $1 000/mån. | Simulerad miljö med prisflöde ingår. Ett konto är aktivt om det haft minst en öppen position under månaden. |
| Match-Trader | White label från $2 500/mån. Turnkey med prop-CRM $4 000/mån. | Prislista från november 2024. |
| cTrader | $8 000-25 000 i startavgift + $3 000-8 000/mån. Prop-paket från ca €6 000/mån. | |
| DXtrade | Offert | |

**Challenge-system ovanpå andras plattformar**

| Lösning | Pris | Kommentar |
|---|---|---|
| FXPropTech | $1 000/mån upp till 500 aktiva konton, $2 500/mån upp till 5 000, $5 000/mån obegränsat. | MT4/5, TradeLocker och cTrader. Oklart om plattformslicensen ingår. |
| B2Prop (B2Broker) | $1 000-3 000/mån, ingen startavgift, inga avgifter per konto. | Kräver en plattform, till exempel cTrader. |
| FPFX, Axcera, Kenmore Design, YourPropFirm, Trade Tech Solutions | Offert | Axcera och Kenmore har fast pris utan avgift per konto. |

**Med intäktsdelning.** Leverantören tar risken för utbetalningar. Riktar sig till influencers utan eget kapital.

| Lösning | Pris |
|---|---|
| Match-Prop | $2 500 i startavgift + 30-45 % av bruttoförsäljningen |
| PropAccount | $3 000 i startavgift. WL1: firman behåller 30 % av bruttointäkterna. WL2: ofta 50/50 netto. |
| StartPropFirm.Today | $2 000 i startavgift + $2 000/mån + 50 % i intäktsdelning |

Som jämförelse kostar en egen MT5-licens ca $10 000-15 000 i startavgift + $7 000-12 000/mån.

Slutsatser:

- **Marknadspriset per aktivt konto** ligger på ca $4,50-10 (Fintatech €4,50-9,90, TradeLocker ca $6 vid 500 konton).
- **Ingen erbjuder självbetjäning.** Alla vi har hittat går via säljsamtal eller demo. Den snabbaste lovar lansering på 48 timmar, och PropAccount och FXPropTech tar 1-2 veckor.
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

- **Ett eget varumärke för traders.** Handelsplattformen har samma namn hos alla firmor, som TradeLocker. Traders som känner igen den litar lättare på en ny firma, och varumärket växer med varje firma som använder den.
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

### Krav

- **Färdiga mallar** för vanliga challenges, till exempel 100k i två steg, som firman kan justera.
- **Varumärke och domän för portalen.** Underdomän direkt. Egen domän med automatiskt TLS-certifikat. Handelsplattformen behåller vårt varumärke, och firman syns där som server och namn vid kontot.
- **Sandlåda med hela kedjan.** Firman skapar en challenge, handlar och ser regelmotorn godkänna eller stänga ett konto. Bara firmans egna testanvändare, så att kostnaden för prisdata hålls nere.
- **Kontroll innan live.** Sandlådan kräver ingen kontroll. Innan firman får ta emot riktiga traders kontrolleras bolag och ägare automatiskt, med manuell granskning inom ett dygn. Det skyddar mot firmor som tar avgifter och aldrig betalar ut, vilket annars skadar vårt rykte.
- **Avtal i portalen.** Användarvillkor och personuppgiftsbiträdesavtal godkänns vid registrering.
- **Automatisk fakturering** med kort, månadsvis i förskott.
- **Hjälp med det som tar längst tid.** Plattformen är sannolikt inte det enda som försenar en ny firma. Erbjud färdiga integrationer mot betalleverantörer som accepterar propfirms, och guider för KYC-leverantör, villkor och bolag.
- **Bra dokumentation**, så att kunderna klarar sig utan support.

## Produkt 1: Handelsplattform

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

## Produkt 2: Propfirm-plattform

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

Viktigt i designen:

- **Mät på equity**, inklusive öppna förluster, inte bara saldo.
- **Definiera "dag" exakt**: tidszon, klockslag och om daglig förlust räknas från saldo eller equity vid dagens start.
- **Handelsplattformens equity är facit.** Räkna inte på ett separat prisflöde.
- **Spara bevis för varje regelbrott**: tidpunkt, equity, priser och öppna positioner. Visa det för tradern.
- **Hantera trassliga händelser.** De kan komma dubbelt, sent eller i fel ordning. Samma händelse ska kunna tas emot flera gånger utan fel, och tillståndet ska stämmas av regelbundet.

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
| Räkna ut beloppet: vinst gånger vinstandel, minus tidigare uttag | Vi |
| KYC-kontroll | Firmans KYC-leverantör |
| Godkänna | Firman, i adminpanelen |
| Skicka pengarna | Firman, via egen bank, utbetalningstjänst eller krypto |
| Markera som betald, ta bort uttagen vinst från kontot, spara historik | Vi |

```
tradern begär -> vi kontrollerar och räknar ut -> firman godkänner
-> firman betalar -> firman markerar som betald -> vi uppdaterar kontot och sparar historiken
```

### API och webhooks

Exempel:

```
POST /v1/accounts        { "email": "...", "challenge_id": "100k-2-step" }
-> skapar ett simulerat konto och skickar inloggningsuppgifter till tradern

Webhooks till firman:
account.passed       tradern klarade fasen
account.breached     tradern bröt en regel
payout.requested     tradern vill ta ut sin vinst
```

## Gränssnitt mellan produkterna

- Handelsplattformen erbjuder ett internt API med ett fåtal funktioner: skapa och stänga konton, sätta gränser och skicka händelser om affärer och kontovärde.
- Propfirm-plattformen pratar med handelsplattformen genom en adapter. Samma adaptergränssnitt kan senare användas mot Match-Trader, cTrader eller DXtrade.
- Det gör att produkterna kan byggas och testas var för sig och säljas separat.
- Under utvecklingen kan en låtsasversion av handelsplattformen skicka påhittade händelser, så att regelmotorn kan byggas och testas fristående.

Flöde:

```
Trader -> traderportal -> köp -> propfirm-plattformen skapar konto i handelsplattformen
Trader -> handelsplattform -> affärer och kontovärde -> regelmotor
regelmotor -> beslut (fas klar eller regelbrott) -> propfirm-plattformen stänger kontot eller byter fas
```

## Arkitektur och teknik

Besluten beskrivs i detalj i [docs/adr](adr/README.md).

- **Kodbas:** ett repo med hårda gränser mellan produkterna som kontrolleras automatiskt (ADR 0001).
- **Webb, traderportal och adminpanel:** Next.js med TypeScript.
- **Backend (handelsmotor, regelmotor och API):** C# på .NET 10 med Postgres. Inbyggda decimaltal gör pengaberäkningarna exakta (ADR 0002).
- **Inloggning:** handelsplattformen har egna användare per firma, så samma e-postadress kan finnas hos flera firmor. Tradern väljer firmans server när den loggar in (ADR 0009).
- **Handelsmotor och regelmotor:** egna tjänster som körs hela tiden, inte serverless, eftersom de håller öppna anslutningar och aktuellt tillstånd i minnet. Kärnan i handelsmotorn är deterministisk (ADR 0005) och beskrivs i [specen för handelsmotorn](spec/handelsmotor.md).
- **Aktuellt tillstånd och kö:** motorn håller tillståndet i minnet och sparar ögonblicksbilder i Postgres. NATS JetStream för händelser mellan tjänsterna. Inget Redis i början (ADR 0003). Alla indata och händelser sparas i en journal i Postgres, så att tillståndet kan byggas upp igen efter en omstart (ADR 0008).
- **Kontrakt mellan produkterna:** protobuf, med gRPC för kommandon och NATS för händelser (ADR 0004).
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
- Kontrollera kunderna innan de går live: ägare, bolag och vilka villkor de har mot sina traders. Med självbetjäning sker det automatiskt med manuell granskning (se Kom igång själv). Avtalet ska ge rätt att stänga av kunder som lurar sina traders.
- Brokers som kunder senare: DORA-krav på IT-leverantörer.

## Affärsmodell och prissättning

- Låg startavgift + månadsavgift + avgift per aktivt konto. Priset växer med kundens storlek.
- Publik prislista på webbplatsen. Det hör till självbetjäningen.
- Hypotes att testa i intervjuer: ca $500 i startavgift, ca $500/mån och några dollar per aktivt konto. Med självbetjäning kostar uppstarten oss lite, så pröva om startavgiften kan tas bort eller tas ut först när firman går live.
- Marknadens nivå är ca $4,50-10 per aktivt konto (se Marknad och konkurrens).
- Definiera "aktivt konto" tydligt. TradeLocker räknar ett konto som aktivt om det haft minst en öppen position under månaden.
- Billigt men inte gratis och inte billigast. För lågt pris skadar förtroendet och är svårt att höja senare.
- Månadsbetalning i förskott och korta avtal, eftersom små firms ofta läggs ner.
- Undvik stora intäktsdelningar. Det är det kunderna klagar på hos konkurrenterna.
- Kontrollera kostnaden för prisdata per slutanvändare innan priset sätts.

## Drift

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
2. Ta in priser och licensvillkor för vidaredistribution från 2-3 dataleverantörer.
3. Gör en landningssida med väntelista, till exempel "Starta din propfirm i dag. Fast pris. Ingen intäktsdelning. Inget säljsamtal." Dela den där blivande grundare finns och mät anmälningarna.
4. Titta på Fintatechs produkt via deras demo. Var öppen med att du undersöker marknaden.
5. Skriv teknisk specifikation för version 1 av båda produkterna: datamodell, internt API, regelmotor, händelseflöden och flödet för självbetjäning.
6. Hitta en lanseringskund, till exempel en trading-influencer eller en nystartad firma, som får lågt pris mot feedback och referens.

## Öppna frågor

- Namn på produkterna och företaget.
- Vilken dataleverantör och vilka licensvillkor? Under utvecklingen används Tiingos gratisplan (ADR 0010), som inte får visas för andra. Tiingo har även en plan för vidaredistribution.
- Slutlig prismodell efter intervjuerna.
- Bolagsform och vilket land bolaget ska ligga i.
- Vilka plattformar adaptrarna ska stödja först, utöver vår egen handelsplattform.
- Behövs en gratis eller mycket billig nivå för de minsta firmorna, som hos Fintatech?
- Ska startavgiften tas bort?
- Hur görs kontrollen av kunden automatiskt, och med vilken leverantör?

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
