# Spec: handelstjänsten

- Fas: 2a, 2b och 3b, insättningar och uttag i 5, firmor i databasen och partner-API i 6, pausade konton i 7, handelsvillkor, listning, inloggning genom firmans portal och kontonas uppgifter för terminalen efter genomgången som ny firma, öppettider, orderverktygen och längre historik efter jämförelsen med konkurrenterna, och personalpanelen (ADR 0057)
- Status: Implementerad i `trading/src/Trading.Service`
- Datum: 2026-10-08

## Syfte

Tjänsten kör handelsmotorn (se [specen för handelsmotorn](handelsmotor.md)) och gör den nåbar för handelsterminalen. Den tar emot priser från ett prisflöde, tar emot kommandon via REST och skickar priser, kontovärde och händelser i realtid via SignalR. Alla indata och händelser sparas i en journal i Postgres, så att tjänsten kan startas om utan att något går förlorat.

## Delar

| Del | Ansvar |
|---|---|
| `EngineHost` | Äger motorn. Alla indata och frågor går genom en kö och körs en i taget. Sätter tidsstämplar och löpnummer, skriver till journalen och återställer från den vid start. |
| `EngineMetrics` | Mätningar av motorns loop för personalpanelen (ADR 0057), minut för minut den senaste timmen i minnet: inputs per slag, nekade förfrågningar, den längsta kön, hur lång tid sparningarna tog och priser per symbol. Priser och inputs per sekund räknas över de senaste 60 sekunderna. |
| `IEngineJournal`, `PostgresEngineJournal` | Journalen: indata, händelser och ögonblicksbilder i Postgres. |
| `EventLog` | Skickar sparade händelser vidare till realtidsdelen. |
| `EngineHealthCheck` | `/health` är friskt först när journalen är uppspelad, och bara så länge den går att skriva. |
| `IPriceFeed` | Gränssnitt för prisflöden. En adapter per dataleverantör. Ger livepriser och historik för graferna. Flödets namn sparas med varje pris det ger. `FollowsTradingHours` säger om priserna kommer från marknader som öppnar och stänger. Bara då får motorn instrumentens öppettider (ADR 0050). |
| `SyntheticPriceFeed` | Slumpvandring för lokal utveckling. Samma frö ger samma priser. Historiken går bakåt från det senaste priset, så den slutar där livepriserna fortsätter. Går dygnet runt, så med det är alla marknader alltid öppna. Standard. |
| `TiingoPriceFeed` | Riktiga priser för valutor och metaller från Tiingos gratisplan, för utveckling (ADR 0010). Hämtar de senaste priserna vid varje anslutning och skickar högst ett pris per symbol och kvart sekund. Historiken kommer från Tiingos staplar av mittpriset, sänkta med halva spreaden till bid (ADR 0048). Tiingo har inga index, råvaror eller krypto, så de instrumenten får inga priser. |
| `CapitalComPriceFeed` | Riktiga priser för alla instrumentets kategorier från ett gratis demokonto hos Capital.com, för utveckling (ADR 0049). Öppnar en session, hämtar de senaste priserna, prenumererar på högst 40 instrument och pingar strömmen var fjärde minut. Historiken kommer från Capital.com:s staplar av bid, högst 950 per anrop. Nya sessioner startar minst 1,2 sekunder isär (`SessionInterval`), eftersom Capital.com tillåter en i sekunden och strömmen och historiken öppnar var sin, till exempel när ett glapp fylls direkt efter att strömmen anslutit. Capital.com:s namn (epic) skiljer sig för några symboler, till exempel GOLD för XAUUSD och J225 för JP225. |
| `PriceFeedPump` | Flyttar priser från flödet till graferna och motorn, när graferna är fyllda. Motorn får dem direkt, också medan graferna fyller ett glapp. |
| `CandleStore` | Bygger candles av bid per symbol och tidsram (M1, M5, M15, M30, H1, H4, D1, W1 och MN, där veckor börjar på måndag och månader den första, ADR 0058), av priser och av staplar. En stapel går in i sin egen tidsram och alla längre. Vet när graferna är fyllda efter en start. |
| `ChartHistory`, `ChartRecorder`, `ChartGapFiller` | Grafernas historik (ADR 0048, ADR 0056). Laddar flödets historik vid ett byte, fyller graferna från sparade staplar vid start, sparar varje färdig minutstapel och fyller glapp efter en omstart eller ett avbrott i flödet från flödets historik. Sparar vad som kom av varje glapp, och fyller ett glapp igen eller laddar om hela historiken när personalen ber om det (ADR 0057). Graferna byggs då om medan nya priser väntar, och motorn får priserna som vanligt. |
| `IChartStore`, `PostgresChartStore` | Grafernas staplar per flöde i Postgres, när historiken laddades och glappen med försök och utfall. |
| `TradingHub`, `RealtimePublisher` | Realtid via SignalR. |
| `AccountSeeder` | Skapar utvecklingskonton och deras ägare vid start om de inte redan finns. |
| `TenantCatalog`, `AdminApiKeyFilter` | Firmorna i minnet: server, namn, grupper och hash av API-nyckeln. Laddas från databasen vid start (ADR 0016). |
| `TenantSeeder` | Sparar de konfigurerade firmorna i databasen vid start och laddar alla firmor. Stoppar starten om konfigurationen är fel. |
| `PartnerCatalog`, `PartnerApiKeyFilter`, `TenantProvisioner` | Partnerna som får skapa firmor, och skapandet: en kopia av mallgrupperna i motorn, sedan firman i databasen. Också personalen gör firmor på samma sätt. Varje ny firma, ny nyckel och ändrad listning skrivs i plattformens logg med vem som gjorde det. |
| `TenantActivity`, `PartnerActivity` | När varje firmas system senast använde sin nyckel och frågade efter sina händelser, och när varje partner senast anropade. I minnet. |
| `StaffAuth`, `IStaffStore`, `StaffSeeder` | Vår personals inloggning till personalpanelen (ADR 0057): egen tabell, egen cookie och eget schema. Personal skapas från `Staff:SeedUsers` vid start. |
| `IPlatformLog`, `PlatformEvents` | Plattformens logg för personalen: servrar, nycklar, listningar, starter, tysta symboler och flöden, glapp och omladdad historik. Ett fel i loggen stoppar aldrig det den beskriver. |
| `PlatformWatcher` | Ser var 5:e sekund vilka symboler som inte fått något pris på en minut medan deras marknad är öppen, och om hela flödet är tyst, och skriver när det börjar och slutar i loggen. Skriver varje start i loggen. |
| `StaffFigures`, `StaffEndpoints` | Personal-API:t: läser motorn, lagren och mätningarna när personalen frågar. |
| `IUserStore`, `AuthEndpoints`, `AccountOwnerFilter` | Inloggning för traders och ägarskap för konton (ADR 0009). |

## Motorloopen

- Motorn är inte trådsäker. Därför går priser, kommandon och frågor genom samma kö och körs i tur och ordning.
- Varje indata får tjänstens aktuella tid som tidsstämpel. Om klockan skulle gå bakåt används föregående tidsstämpel, eftersom motorn avvisar indata som går bakåt i tiden.
- Priser stämplas när de tas emot, inte med leverantörens tid. Det gör att åldern på ett pris mäts med samma klocka som kommandona.
- Frågor ändrar inget tillstånd, så en fråga som misslyckas påverkar bara den som frågade. Ett kommando eller pris som orsakar ett fel i motorn betyder att tillståndet inte längre går att lita på. Då stoppas tjänsten.
- Realtidsdelen läser händelser från en egen kö, så nätverket kan aldrig blockera motorn.
- Tidsstämplar avrundas till hela mikrosekunder, som Postgres lagrar dem, så att en uppspelning ser exakt samma tider.

## Journal och återstart

Se [ADR 0008](../adr/0008-journal-av-indata.md) för besluten.

| Tabell | Innehåll |
|---|---|
| `engine_inputs` | Varje indata med löpnummer. Priser i egna kolumner med flödet de kom från (`feed`, tomt för priser sparade före ADR 0048), kommandon som JSON. |
| `chart_bars` | Grafernas staplar av bid per flöde, symbol och längd: minutstaplar från livepriserna och flödets historik. Bara de senaste 30 dagarna behålls. |
| `chart_histories` | Flöden vars historik är laddad sedan tjänsten senast bytte till dem. |
| `engine_events` | Varje händelse med löpnummer, konto, kontots grupp och löpnumret på indatan som orsakade den (`input_sequence`, tomt för händelser sparade före ADR 0053). Används för `GET /events`, firmans händelseström, kvitton och rapporter. |
| `tenant_notices` | Firmans meddelande överst i terminalen under en incident (ADR 0053). |
| `engine_snapshots` | Motorns tillstånd efter ett visst indata, med konfigurationens fingeravtryck. De tre senaste behålls. |
| `users`, `account_owners` | Traders och vilka konton de äger. |
| `tenants`, `tenant_groups` | Firmorna: server, namn, SHA-256 av nyckeln till admin-API:t, partnern som skapade firman, om servern listas och var firmans traders loggar in (`login_url`), och vilken firma varje grupp hör till. |
| `data_protection_keys` | Nycklarna som skyddar inloggningscookies. |
| `login_links` | Engångslänkar för inloggning: hash av token, trader, konto och när länken går ut. Länkar som gick ut för mer än ett dygn sedan tas bort när nya skapas. |
| `schema_migrations` | Vilka migreringar som körts. Tabellerna skapas och uppgraderas en gång per start, innan något lager använder databasen första gången. Nycklarna för cookies läses nämligen innan motorn startar. |

**Skrivning:** Motorn tillämpar indata direkt. En separat skrivare sparar dem i batcher i en transaktion. Svar på kommandon, händelser i realtid och svar på frågor släpps först när det de bygger på är sparat. Misslyckas en skrivning tre gånger stoppas tjänsten.

**Start:**

1. Kör migreringar.
2. Läs den senaste ögonblicksbilden och återställ motorn från den.
3. Spela upp indata efter den. Har konfigurationen ändrats sedan ögonblicksbilden, till exempel med fler instrument, jämförs varje uppspelad händelse med den sparade. Vägra starta om någon skiljer sig, eftersom indatan då tillämpades med en annan konfiguration än den nya.
4. Kontrollera att uppspelningen gav lika många händelser som journalen har. Vägra starta annars.
5. Spara en ny ögonblicksbild med den aktuella konfigurationen.
6. Låt det syntetiska flödet fortsätta från de senaste priserna.
7. Fyll graferna med det aktuella flödets priser (ADR 0048). Kommer det senaste sparade priset från ett annat flöde, eller finns inget, har tjänsten bytt flöde, och flödets historik laddas först. Den laddas också när den sparade historiken inte når så långt bakåt som `Charts:History` säger (ADR 0051). Därefter läses flödets sparade staplar, och priserna efter den sista spelas upp från journalen. Ett annat flödes priser kommer aldrig med. `GET /candles` väntar tills graferna är fyllda. En hel minut eller mer utan något pris, mellan de sparade staplarna och de första priserna efter starten eller medan tjänsten kör, fylls från flödets historik när priserna kommer igen (ADR 0056). Säger leverantören nej försöker tjänsten igen efter 2, 10 och 30 sekunder. Graferna håller tillbaka sina priser under tiden, men motorn får dem direkt.

**Grafernas historik:** de senaste 2 dagarna och i dag laddas i minutstaplar, som ger M1 och M5. De senaste 30 dagarna laddas i staplar på 15 minuter, som ger M15 och M30, och resten av de 180 dagarna i timstaplar, som ger H1 och längre (ADR 0051). Lägre tidsramar får alltså kortare historik. Terminalen visar 500 staplar och hämtar äldre när tradern bläddrar bakåt. Varje färdig minutstapel sparas några sekunder efter att minuten slutat, och de sista när tjänsten stängs. Går historiken inte att ladda startar tjänsten ändå, och nästa start försöker igen.

**Avstängning:** Arbete som inte hunnit köras avbryts. Den sista batchen sparas tillsammans med en ögonblicksbild, så att nästa start inte behöver spela upp något.

**Ändrad konfiguration:** Stäng av tjänsten på vanligt sätt (Ctrl+C), ändra konfigurationen och starta igen. Efter en krasch startar tjänsten med den nya konfigurationen när uppspelningen ger samma händelser som sparades, vilket gäller en ändring som bara lägger till något. Annars måste den först startas en gång med den gamla konfigurationen.

## REST-API

Tjänsten publicerar ett OpenAPI-dokument på `/openapi/v1.json`. Samma dokument skrivs till kontraktet `contracts/trading/trading-service.json` när tjänsten byggs. Terminalens typer och propfirm-plattformens klient genereras från det (ADR 0012).

### Inloggning

| Metod och väg | Beskrivning |
|---|---|
| `POST /api/auth/login` | Loggar in med `{ "server", "email", "password" }`, där `server` är firmans id. Utan `server` letar tjänsten bland firmorna som tillåter lösenord i terminalen (profilens `passwordLogin`, ADR 0058): passar uppgifterna hos en loggas tradern in där, och hos flera svarar den 409 med `servers`, firmorna att välja mellan. Sätter sessionscookien. Fel server, e-post eller lösenord ger samma svar, också hos en firma som inte tillåter lösenord. Högst `Login:AttemptsPerMinute` försök per minut och IP-adress, som standard 10. |
| `POST /api/auth/link` | Loggar in med `{ "token" }` från en inloggningslänk. Länken fungerar en gång. Samma begränsning av försök som vid inloggning. |
| `POST /api/auth/logout` | Loggar ut. |
| `GET /api/auth/me` | Den inloggade tradern med `name` när firman har berättat det, firmans server med `logoUrl` och `profile` (se Profilen per server), kontona tradern äger och deras uppgifter för terminalen i `accountDetails`: `label`, `profitTarget`, `timeZone` och `detailsUrl` (ADR 0035), och `maxPriceAgeSeconds`, hur gammalt ett pris får vara innan motorn avvisar order på det (ADR 0053). |
| `GET /api/servers?search=` | Servrar som listas och vars namn eller id innehåller `search`, minst två tecken, högst fem, med id, firmans namn, `loginUrl`, `logoUrl` och `profile`. Utan en sökning på minst två tecken är svaret tomt, så att ingen kan se alla kunder (ADR 0058). `loginUrl` är firmans portal när firmans traders loggar in där (ADR 0027). Firmor som en partner har skapat listas när partnern säger att de är live, men går att logga in på innan. Kräver ingen inloggning. |
| `GET /api/servers/{id}` | En server, listad eller inte, med samma fält. 404 för en okänd. Terminalen hittar så en firma i sandlådan och dess portal. |
| `GET /api/me/settings` | Den inloggades inställningar i terminalen, som `{ "settings": { "nyckel": värde } }` (ADR 0052). |
| `PUT /api/me/settings/{key}` | Sparar vilket JSON-värde som helst under nyckeln och ersätter det förra. Nycklar har bokstäver, siffror, punkter, bindestreck och understreck, högst 120 tecken, annars 422. Ett värde över 64 KB svarar 413, och en ny nyckel när tradern redan har 200 svarar 422. Terminalen bestämmer vad varje nyckel innehåller. |
| `DELETE /api/me/settings/{key}` | Tar bort inställningen. |

### För tradern

Alla vägar börjar med `/api/accounts/{accountId}` och kräver att tradern är inloggad och äger kontot. Andras konton svarar 404.

| Metod och väg | Beskrivning |
|---|---|
| `GET` | Kontot värderat till senaste priser. Varje golv har `headroom`: hur långt equity kan falla innan golvet bryts. `ownLimits` är traderns egna spärrar (ADR 0054): gränserna som gäller nu (`limits`), de som väntar till nästa handelsdag (`pending`), handelsdagen (`tradingDay`), saldot när dagen började (`dayStartBalance`), equity där förlustgränsen och dagsmålet nås (`lossLevel`, `targetLevel`), affärer i dag (`tradesToday`), när nästa dag börjar (`nextDayStart`) och låset (`lock`, med `until` och `reason`, eller null utan lås). |
| `GET /instruments` | Gruppens instrument med namn (till exempel "Euro / US Dollar" och "Gold", från `Trading:Instruments:N:Name`), kategori (`Forex`, `Metals`, `Indices`, `Commodities` eller `Crypto`) och villkor: hävstång, påslag och provision. |
| `GET /market-hours` | När varje symbol i gruppen går att handla, sett från nu (ADR 0050): `isOpen`, `nextChange`, när marknaden stänger om den är öppen eller öppnar om den är stängd, och `sessions`, perioderna den är öppen från nu och en vecka framåt i UTC, med den pågående först. En marknad som aldrig stänger, som krypto eller alla med det syntetiska flödet, är öppen med `nextChange` och `sessions` som null. `nextChange` är också null om marknaden inte öppnar inom en månad. |
| `GET /notice` | Firmans meddelande överst i terminalen, som `{ "notice": { "title", "text", "level", "url", "updatedAt" } }`, eller `null` (ADR 0053). Nya kommer i realtid som `Notice`. |
| `GET /positions/{positionId}/receipt` | Detaljerna för en position, det terminalen och portalen visar som Trade details (ADR 0053): ordern den kom från, öppningen och varje stängning med tid, volym, pris och provision, flödets råa pris bakom fyllningen med dess löpnummer i journalen (`feed`), påslaget i punkter på den sida som fylldes (`markupPoints`) och flödets bid och ask en minut före och efter (`prices`, högst 240 punkter), som terminalen och portalen inte visar men firmans egna system kan använda. En stängning med stop loss eller take profit har nivån den stod på (`level`), utom med trailing stop. 404 för en okänd position. |
| `GET /breach-report` | Rapporten för kontots senaste brutna golv (ADR 0053): golvet, nivån, equity och saldot i ögonblicket, saldot efter att positionerna stängdes, priserna som bröt det med flödets priser och påslaget, de öppna positionerna, equity pris för pris före brottet (`equityCurve`, högst 600 punkter), perioderna utan priser på minst en minut (`gaps`) och kontots händelser i perioden (`events`). Perioden börjar när den äldsta öppna positionen öppnades, men högst 6 timmar och minst 15 minuter före brottet, och aldrig innan kontot skapades. 404 om inget golv brutits. |
| `GET /rules` | Kontots regler som firmans system senast berättade dem (ADR 0052): `funded`, `tradingDaysRequired`, `tradingDaysCounted`, `passBy`, `openPositionBy`, `consistencyPercent`, `bestDayPercent`, `profitSplitPercent` och `payoutAvailable` (ADR 0058). Alla fält är tomma när firman inte har berättat några. Nya regler kommer i realtid som `Rules`. |
| `GET /instruments/{symbol}/point-value` | Vad en punkt på en lot är värd i kontots valuta vid senaste växelkurs (`perLot`). Vinsten är punkter gånger volym gånger `perLot`, före avrundning och provision. Terminalen använder det för att sätta stop loss och take profit med belopp. 404 om gruppen inte handlar symbolen eller växelkursen saknas än. |
| `GET /prices` | Senaste priser efter påslag. |
| `GET /candles/{symbol}?timeframe=M1&count=500&before=` | Candles av bid som kontot ser det, äldst först. Högst 5 000. Med `before` de senaste som börjar före den tiden, för att bläddra bakåt. Väntar tills graferna är byggda efter en start. |
| `GET /events?limit=500` | Kontots senaste händelser, äldst först. Med `after={sequence}` i stället de första efter sekvensnumret, för att hämta ikapp efter en återanslutning. Med `before={sequence}` de sista före sekvensnumret, för att bläddra bakåt. `after` och `before` tillsammans svarar 422. Högst 1 000 per anrop. |
| `POST /orders` | Lägger en order. Klienten skapar order-id:t. Med `trailingStop` följer stop lossen priset. Medan marknaden är stängd svarar order, stängningar och ändrade stoppar 422 med `reason` `MarketClosed`. |
| `PUT /orders/{orderId}` | Ger en väntande order nytt pris (`price`), `stopLoss`, `takeProfit` och `trailingStop` (ADR 0051). |
| `DELETE /orders/{orderId}` | Tar bort en väntande order. |
| `POST /positions/{positionId}/close` | Stänger en position. Med `{ "volume": 0.4 }` bara den delen. |
| `POST /positions/close-all` | Stänger alla positioner, eller med `{ "symbol": "EURUSD" }` en symbols, i samma ögonblick. De som inte går att stänga nu ligger kvar. 404 utan positioner. |
| `PUT /positions/{positionId}/stops` | Sätter eller tar bort stop loss och take profit, och slår på eller av trailing stop med `trailingStop`. |
| `PUT /limits` | Traderns egna gränser (ADR 0054): `{ "dailyLoss", "dailyTarget", "maxTrades" }`, belopp i kontovalutan och antal affärer per dag. Tomt stänger av en gräns. En strängare gräns gäller direkt och en lösare från nästa handelsdag. 422 med `InvalidLimits` för ett belopp som inte är över noll eller har för många decimaler, eller ett antal utanför 1 till 1 000. |
| `POST /lock` | Låser nya ordrar till nästa handelsdag med `{ "closePositions" }` (ADR 0054). Ingen kan häva det. Ett konto som redan är låst svarar 200 utan händelser. Ordrar på ett låst konto svarar 422 med `AccountLocked`, och när dagens affärer är slut med `TradeLimitReached`. |

### Administration

För firmans egna system, till exempel propfirm-plattformen (ADR 0012). Alla vägar börjar med `/api/admin/v1`, kräver firmans API-nyckel i headern `X-Api-Key` och når bara firmans egna grupper, traders och konton. Andras svarar 404.

| Metod och väg | Beskrivning |
|---|---|
| `POST /users` | Skapar en trader med `{ "email", "password" }`. Lösenordet ska ha minst `Login:MinimumPasswordLength` tecken, som standard 10. E-postadressen är unik inom firman. |
| `GET /users?email=` | Hittar en av firmans traders via e-post. |
| `PUT /users/{userId}/password` | Byter traderns lösenord med `{ "password" }`. |
| `PUT /users/{userId}/name` | Traderns namn som firman känner det, med `{ "name" }`, högst 100 tecken, eller `null` för inget (ADR 0058). Terminalen visar initialerna och namnet. 204. |
| `POST /users/{userId}/login-links` | Skapar en inloggningslänk till terminalen, valfritt med `{ "accountId" }` för kontot som ska öppnas. Svarar med `url` och `expiresAt`. Länken fungerar en gång i 2 minuter. |
| `POST /accounts` | Skapar ett konto i en av firmans grupper, ägt av en av firmans traders (`ownerUserId`). |
| `GET /accounts/{accountId}` | Kontot värderat till senaste priser, som tradern ser det. |
| `PUT /accounts/{accountId}/floors/{floorId}` | Sätter ett golv, till exempel `{ "rule": { "kind": "FixedFloor", "level": 95000 } }` eller `{ "rule": { "kind": "AnchoredFloor", "distance": 5000, "anchor": "Balance" } }`. |
| `DELETE /accounts/{accountId}/floors/{floorId}` | Tar bort ett golv. |
| `POST /accounts/{accountId}/close` | Stänger kontot. |
| `POST /accounts/{accountId}/suspend` | Pausar kontot: väntande ordrar tas bort och nya tas inte emot, men tradern kan stänga positioner och ändra stoppar. Ett konto som redan är pausat svarar 200 utan händelser, och ett avstängt 422 med `AccountDisabled`. |
| `POST /accounts/{accountId}/resume` | Låter ett pausat konto handla igen. Ett konto som inte är pausat svarar 200 utan händelser. |
| `POST /accounts/{accountId}/reopen` | Öppnar ett avstängt konto igen med `{ "balance" }`, utan golv (ADR 0053). Ett konto som inte är avstängt svarar 200 utan händelser, och ett ogiltigt saldo 422 med `InvalidAmount`. |
| `GET /accounts/{accountId}/positions/{positionId}/receipt` | Detaljerna för en position, som tradern ser dem. |
| `GET /accounts/{accountId}/breach-report` | Rapporten för kontots senaste brutna golv, som tradern ser den. |
| `GET /impact?from=&to=` | Vad en period, till exempel en incident, gjorde med firmans konton (ADR 0053): konton med positioner öppna när den började, förfrågningar som avvisades under den för att priset saknades eller var för gammalt (`ordersRefused`, `closesRefused`, `changesRefused`) och golv som bröts under den eller inom 30 minuter efter (`breach`), med saldot och equity när den började och kontot nu. Perioden är högst ett dygn, börjar inom de senaste 30 dagarna och inte i framtiden, annars 422. |
| `PUT /notice` | Visar ett meddelande överst i firmans terminaler: `{ "title", "text", "level", "url" }` med rubrik på 1 till 120 tecken, text på 1 till 1 000, `level` `Info` eller `Warning` och en http- eller https-adress. 422 annars. Terminaler som är öppna får det direkt. |
| `DELETE /notice` | Tar bort meddelandet. |
| `PUT /accounts/{accountId}/trading-day` | När kontots handelsdag börjar, som firman räknar den (ADR 0054): `{ "timeZone", "startsAt" }`, en IANA-zon och en lokal tid som `"00:00:00"`. Traderns egna gränser räknas per sådan dag, och ett lås varar till nästa. Utan den börjar dagen vid midnatt UTC. 422 med `InvalidTradingDay` för en okänd zon, och med `AccountDisabled` för ett avstängt konto. |
| `PUT /accounts/{accountId}/details` | Hur terminalen visar kontot (ADR 0035): `{ "label", "profitTarget", "timeZone", "detailsUrl" }`. `label` är namnet, högst 100 tecken, `profitTarget` saldot som klarar fasen, `timeZone` handelsdagens zon (IANA, till exempel `Europe/Stockholm`) och `detailsUrl` kontot i firmans portal, en absolut http- eller https-adress. Tomt tar bort ett fält. Uppgifterna ligger utanför motorn och journalen, eftersom de bara är för visning. 422 för ett ogiltigt fält. |
| `PUT /accounts/{accountId}/rules` | Kontots regler som firmans system ser dem nu, som terminalen visar och varnar för (ADR 0052): `{ "funded", "tradingDaysRequired", "tradingDaysCounted", "passBy", "openPositionBy", "consistencyPercent", "bestDayPercent", "profitSplitPercent", "payoutAvailable" }`. `funded` säger att handelsdagarna räknas mot en utbetalning, `passBy` när steget måste vara klart och `openPositionBy` när en ny position senast måste öppnas, och `bestDayPercent` är bästa handelsdagens andel av vinsten, som konsekvensregeln tillåter upp till `consistencyPercent`. Ett finansierat kontos `profitSplitPercent` är traderns andel av en utbetalning och `payoutAvailable` om en går att begära nu (ADR 0058). Varje fält ersätts, och ett som utelämnas tas bort. En trader med terminalen öppen får dem direkt. 422 för handelsdagar under 1 eller över 1 000, negativa räknade dagar eller andelar, en konsekvensregel över 100 och en andel som inte är över 0 och högst 100. Reglerna ligger utanför motorn och journalen, eftersom de bara är för visning. |
| `POST /accounts/{accountId}/balance-operations` | Sätter in eller tar ut pengar med `{ "operationId", "amount", "minBalance" }`. Ett negativt belopp är ett uttag. Samma `operationId` igen svarar 409 med `DuplicateId`, så ett nytt försök dras aldrig två gånger. Ett uttag som skulle lämna mindre än `minBalance`, ta mer än den fria marginalen eller bryta ett golv svarar 422 med `InsufficientFunds`. Golv som mäts från kontot följer med saldot (se [specen för handelsmotorn](handelsmotor.md)). |
| `GET /events?after=0&limit=100&wait=0` | Firmans händelser efter ett löpnummer, äldst först, med `cursor` för nästa anrop. Högst 1 000 per anrop. Med `wait` väntar anropet upp till 30 sekunder på nya händelser. Bara sparade händelser visas, så ingen händelse kan försvinna vid en omstart. |
| `GET /instruments` | Alla instrument på plattformen: symbol, kategori, bas- och kursvaluta, kontraktsstorlek och decimaler. |
| `GET /terminal-profile` | Hur firmans terminal fungerar (ADR 0058): `kind` (`Prop`, `Broker`, `Practice` eller `Desk`), `modules` (`rulebook`, `ownLimits`, `riskSizing`, `tradeDetails`, `breachReports`), `confirmOrders`, `startingSize` (`kind` `Smallest`, `Lots` eller `RiskOfRoom` med `value`), `passwordLogin`, `links` (`help`, `support`, `terms`, `privacy`, `passwordReset`) och `riskWarning`. En firma som inte satt någon har sortens standard, `Prop` med lösenord. |
| `PUT /terminal-profile` | Sätter hela profilen. 422 för en okänd sort, en startstorlek utan värde eller med fel värde (lots över 0 och högst 1 000, en andel från 0,01 till 100 och bara med storlek från risk), en länk som inte är http eller https, eller en riskvarning som är tom eller längre än 600 tecken. Terminaler tar den nästa gång de öppnas. |
| `GET /groups` | Firmans grupper med valuta, om villkoren kan ändras (`changeable`, bara grupper som skapats åt firman) och villkoren per symbol: hävstång, påslag i punkter och provision per lot och sida. |
| `PUT /groups/{groupId}/symbols` | `{ "symbols": [{ "symbol", "leverage", "spreadMarkupPoints", "commissionPerLotPerSide" }] }`. Ersätter gruppens symboler och villkor, som gäller alla dess konton direkt. 404 för en grupp som inte är firmans, 422 med `InvalidGroup` för ogiltiga villkor, med `GroupNotChangeable` för en konfigurerad grupp och med `SymbolInUse` när en symbol som tas bort har öppna positioner eller ordrar. |

### Partner

För system som skapar firmor åt andra, till exempel propfirm-plattformen när en firma registrerar sig (ADR 0016). Alla vägar börjar med `/api/partner/v1` och kräver partnerns nyckel i headern `X-Api-Key`. En partner når bara de firmor den har skapat. Andras, och konfigurerade firmor, svarar 404.

| Metod och väg | Beskrivning |
|---|---|
| `GET /server-names/{id}` | `{ "id", "available" }`: om en firma kan skapas med servern nu. |
| `POST /tenants` | Skapar en firma med `{ "id", "name", "currency" }`. Firman får en kopia av varje grupp i `Tenancy:NewTenantGroups`, med id `{id}-{mall}`, i `currency` (en av `Tenancy:Currencies`) eller mallens valuta när den saknas. 422 med `InvalidCurrency` för en annan valuta. Svarar 201 med `id`, `name`, `listed`, `groups` (`id` och `currency`) och `adminApiKey`, nyckeln till admin-API:t, som bara visas nu. 409 med `DuplicateId` om servern eller en av grupperna finns, 422 med `InvalidId` för ett id som inte är 2 till 63 små bokstäver, siffror och bindestreck eller ett namn som inte är 1 till 100 tecken. |
| `GET /tenants/{id}` | Firman med sina grupper, utan nyckel. |
| `POST /tenants/{id}/admin-key` | `{ "adminApiKey" }`, en ny nyckel. Den gamla slutar fungera direkt. |
| `GET /price-feed` | Hur prisflödet mår (ADR 0053): flödets namn, tiden nu, när det senaste priset kom och för varje symbol när dess senaste pris kom och om marknaden är öppen enligt öppettiderna. Gäller hela plattformen, inte en firma. |
| `PATCH /tenants/{id}` | `{ "listed", "loginUrl", "logoUrl" }`. Sätter om servern listas, var firmans traders loggar in, en absolut http- eller https-adress, eller tom för att logga in här, och firmans logga, som terminalen visar i kontoraden. Fält som inte skickas med behålls, och en tom `logoUrl` tar bort loggan. 422 för en ogiltig adress. Propfirm-plattformen listar en firma när den går live och skickar loggan när firman byter den. |

Grupperna skapas i motorn innan firman sparas. Om tjänsten stannar däremellan tar nästa försök över grupperna, eftersom ingen firma äger dem.

### Personal

För vår egen personal i personalpanelen `trading/staff` (ADR 0057). Alla vägar börjar med `/api/staff/v1`. Utom inloggningen och utloggningen kräver de personalens session, med cookien `trading_staff_session`, och svarar annars 401. En traders session och en firmas API-nyckel räcker inte. Personalen ser kontonummer och belopp, aldrig vilka traderna är.

| Metod och väg | Beskrivning |
|---|---|
| `POST /login` | Loggar in med `{ "email", "password" }` och sätter sessionen. Fel e-post eller lösenord ger samma svar, 401. Samma begränsning av försök som traders inloggning. |
| `POST /logout` | Loggar ut. |
| `GET /me` | Den inloggades e-post. |
| `GET /overview` | Översikten: antal servrar och listade, det som behöver oss (`needsUs`, se nedan), nyckeltal (`figures`), prisflödet (`feed`, med priser per minut den senaste timmen), motorn (`engine`), de fem symboler med störst nettovärde (`topExposure`) och de åtta senaste raderna i plattformens logg (`latest`). |
| `GET /servers?group=&search=` | Alla servrar med vem som gjorde dem (`madeBy`: `Partner`, `Staff` eller `Configuration`), partnerns namn, när de gjordes, om de listas, kontovalutorna, traders, konton som handlar, öppna positioner, när firmans system senast frågade efter sina händelser och om det är sent (`eventsStale`). Flest konton först. `group` är `needsUs`, `listed`, `notListed` eller `configuration`, och `search` söker i id och namn. `counts` har antalet i varje grupp. |
| `POST /servers` | Gör en server åt en firma med `{ "id", "name", "currency" }`, som partner-API:t. Svarar 201 med `adminApiKey`, som bara visas nu. 409 med `DuplicateId` för en upptagen server, och 422 med `InvalidId` eller `InvalidCurrency`. |
| `GET /server-names/{id}` | Om en server kan göras med namnet nu. |
| `GET /currencies` | Kontovalutorna en ny server kan få (`Tenancy:Currencies`). |
| `GET /servers/{id}` | En server: nyckeltal, grupperna med villkor och öppna positioner per symbol, inloggning och logga, när den listades, admin-nyckeln (`adminKey`: vem som har den, när den gjordes eller senast byttes och när den senast användes), firmans läsning av händelser (`events`: när, efter vilket löpnummer, hur många som väntar, högst 1 001, och om det är sent), meddelandet i terminalerna och serverns tio senaste rader i loggen. 404 för en okänd. |
| `PATCH /servers/{id}` | `{ "listed" }` sätter servern på listan traders väljer från eller tar den av. Svarar med servern. En partner kan lista den igen. |
| `POST /servers/{id}/admin-key` | Stoppar serverns admin-nyckel med `{ "reason" }`, 1 till 500 tecken, som sparas i loggen. För en server en partner gjort blir den nya nyckeln en som ingen ser, och svaret har `heldBy` med partnerns namn och ingen nyckel: partnern hämtar en ny med `POST /api/partner/v1/tenants/{id}/admin-key` när den gamla nekas. Annars har svaret den nya nyckeln i `adminApiKey`, som bara visas nu. 409 för en konfigurerad server, vars nyckel står i konfigurationen. |
| `GET /servers/{id}/events?account=&limit=50` | Serverns senaste händelser, de nyaste först, eller ett av dess kontons med `account`. `limit` är 1 till 200. |
| `GET /accounts/{accountId}` | Ett konto med sin server, grupp, valuta, status, saldo, equity, marginal, öppna positioner och väntande ordrar. 404 för ett okänt. |
| `GET /search?q=` | Upp till åtta servrar vars id eller namn innehåller texten, och kontot med just det numret, med eller utan `#`. |
| `GET /price-feed` | Flödet: namnet, om det har en riktig historik (`hasHistory`) och följer öppettider, det senaste priset, priser per sekund de senaste 60 sekunderna, sedan när priser kommit eller flödet varit tyst, priser per minut den senaste timmen, varje symbols råa pris före påslag med decimaler, tid, priser den senaste minuten, marknaden och läget (`Live`, `Silent`, `Closed` eller `Waiting`), de 20 senaste glappen i graferna och historiken (när den laddades, hur långt bakåt den når och om den laddas om nu). En symbol är tyst efter en minut utan pris medan dess marknad är öppen. |
| `POST /price-feed/gaps/{gapId}/retry` | Ber flödet om ett glapp igen, i bakgrunden. 202. 404 för ett glapp som inte finns eller redan fyllts, och 409 med syntetiska priser. |
| `POST /price-feed/history/reload` | Laddar om flödets hela historik i bakgrunden och bygger om graferna. Glapp som historiken täcker räknas som fyllda. 202, eller 409 med syntetiska priser. |
| `GET /instruments` | Instrumenten med kategori, valutor, kontrakt, decimaler, lotter, öppettidernas namn med det nuvarande flödet (null när de alltid är öppna), hur många servrar som handlar dem och när de är öppna veckan från söndag i UTC. Öppettiderna som används, med tidszon, sessioner och symboler, och de stängda dagarna framåt i marknadens lokala tid. |
| `GET /exposure?server=` | Vad traders håller nu i USD: lång, kort och netto, tradernas öppna resultat och marginal, per symbol (lotter lång och kort, netto, nettovärde, öppet resultat, konton och positioner), de tio största positionerna och nettot per server. Med `server` bara den. `unvalued` räknar positioner utan växelkurs till USD än, som lämnas utanför värdena. |
| `GET /engine` | Motorn: om den är frisk, kön nu, inputs och priser per sekund, minut för minut den senaste timmen (inputs per slag, nekade, längsta kön, sparningar med snitt och längsta tid), journalens sista input och händelse, storlek på disk och ungefär hur mycket den växer per dygn, ögonblicksbilderna, hur starten gick, versionen, öppna terminaler och partnerna med antal servrar och senaste anrop. |

Det som behöver oss (`needsUs`) har ett slag (`kind`) och fälten som hör till det: `FeedSilent` (det senaste priset och antalet konton med positioner), `SymbolSilent` (symbolen, dess senaste pris och kontona som håller den), `EventsNotRead` (servern, när dess system senast frågade och händelserna som väntar), `ChartGapNotFilled` (glappet), `QueueBehind` (inputs i kön, minst 1 000) och `SlowSaves` (den längsta sparningen de senaste 5 minuterna i millisekunder, minst en sekund). En firma räknas som sen när dess system inte frågat på 5 minuter och händelser väntar. Ett glapp räknas de första 7 dagarna.

### Svar

- Ett kommando som godkänns ger `200` med händelserna det orsakade: `{ "events": [{ "sequence": 4, "event": { "kind": "PositionOpened", ... } }] }`.
- Ett kommando som avvisas ger ett problem-svar med fältet `reason`, till exempel `{ "status": 422, "reason": "StalePrice" }`. Statuskoderna beskrivs i [ADR 0006](../adr/0006-api-mellan-terminal-och-tjanst.md).

## Realtid

SignalR-hubben ligger på `/hubs/trading` och kräver inloggning. Klienten anropar `Subscribe(accountId)` för ett konto den äger och får sedan:

| Meddelande | Innehåll | När |
|---|---|---|
| `Account` | Kontot som i `GET /api/accounts/{accountId}` | Direkt vid prenumeration, därefter högst var 250:e ms när det ändrats |
| `Prices` | Priser som ändrats för kontots grupp | Direkt vid prenumeration, därefter högst var 100:e ms |
| `Events` | Nya händelser för kontot | Direkt när de inträffar |
| `Rules` | Kontots regler som i `GET /api/accounts/{accountId}/rules` | Direkt när firmans system berättar nya (ADR 0052) |
| `Notice` | Firmans meddelande som i `GET /api/accounts/{accountId}/notice`, eller `null` | Direkt vid prenumeration och när firman ändrar det (ADR 0053) |
| `Charts` | Inget | Till alla terminaler när graferna fått staplar för en tid utan priser (ADR 0056) |

Den senaste candlen uppdateras i terminalen med priserna från `Prices`. Vid omladdning, efter en återanslutning och på `Charts` hämtas historiken från `GET /candles`.

## Konfiguration

| Sektion | Innehåll |
|---|---|
| `Trading` | Instrument, grupper, max ålder på priser och utvecklingskonton (`SeedAccounts`). Varje instrument har en kategori (`Category`), annars startar inte tjänsten, och ett namn för traders (`Name`, till exempel "Gold"), som annars blir symbolen. Index, råvaror och krypto har sig själva som basvaluta, till exempel US100 mot USD, på samma sätt som guld har XAU (ADR 0049). |
| `Trading:TradingHours` | Öppettider per namn (ADR 0050): `TimeZone` (IANA, till exempel `America/New_York`), `Sessions` som `Sun 17:00 - Fri 17:00` och `Closures` som `2026-12-25` eller `2026-12-24 13:15 - 2026-12-27 18:00`, i marknadens tidszon. Ett instrument pekar på sina med `TradingHours`, och ett instrument utan är alltid öppet. Konfigurationen har börsernas tider `Forex`, `Globex`, `IceBrent`, `IceFtse` och `EurexDax`, och krypto har inga. Fel i öppettiderna eller ett namn som saknas stoppar starten, med alla prisflöden. |
| `Trading:TradingHoursByFeed` | Ett prisflödes egna öppettider per symbol, där de skiljer sig från börsens (ADR 0050), till exempel `CapitalCom:UK100 = CapitalComIndices`. Ett tomt namn betyder dygnet runt. Capital.com har egna tider för valutor, metaller, index, Brent och krypto, hämtade från deras API. Ett okänt flöde, en okänd symbol eller okända öppettider stoppar starten. |
| `SyntheticFeed` | Frö, intervall och startpriser per symbol. |
| `Charts` | Hur många hela dagar historiken når bakåt (`History`, 180, och för dagar, veckor och månader `DayHistory`, 1 095), hur många av dem som laddas i staplar på 15 minuter (`QuarterHourHistory`, 30) och i minutstaplar (`MinuteHistory`, 2). Resten laddas i timstaplar. |
| `Realtime` | Takt för priser och konto. |
| `Journal` | Antal indata mellan ögonblicksbilder och hur många som behålls. |
| `Tenants` | Firmor som sparas i databasen vid varje start, för utveckling och tester: id (servern, till exempel `nordic-prop`), namn, grupper i `Trading:Groups`, SHA-256 av API-nyckeln, valfri `LoginUrl`, firmans portal där traderna loggar in, och valfri `Terminal` med `Kind`, `PasswordLogin` och `ConfirmOrders` (ADR 0058), som ger sortens standardprofil med de valen. Utan `Terminal` behåller firman den profil den har. De listas alltid. |
| `Partners` | Partnerna som får skapa firmor: `Id`, `Name` och SHA-256 av nyckeln (`ApiKeySha256`). |
| `Staff` | Vår personal i personalpanelen: `SeedUsers`, med `Email` och `Password`, skapas vid start eller får det angivna lösenordet. Bara för utveckling, tills personal kan bjudas in. |
| `Tenancy:NewTenantGroups` | Grupperna i `Trading:Groups` som en ny firma får en kopia av. Standard `standard`. |
| `Tenancy:Currencies` | Kontovalutorna en ny firma kan välja: USD, EUR och GBP. Var och en behöver ett instrument mot USD, och starten stoppas annars. |
| `PriceFeed` | `Provider` (`Synthetic`, `Tiingo` eller `CapitalCom`). För Tiingo även `Tiingo:ApiKey`, och för Capital.com `CapitalCom:ApiKey`, `CapitalCom:Identifier` (kontots e-post) och `CapitalCom:Password` (lösenordet för API-nyckeln). Nycklar och lösenord sätts med `dotnet user-secrets`. |
| `ConnectionStrings:Trading` | Databasen för journalen. Lokalt Postgres från `deploy/docker-compose.yml`. |
| `Cors:AllowedOrigins` | Webbadresser som får anropa API:t, till exempel terminalen på `http://localhost:3001` och personalpanelen på `http://localhost:3003`. |
| `Terminal:Url` | Terminalens adress, till exempel `http://localhost:3001/`. Används i inloggningslänkar. |
| `Login` | Regler för lösenord och inloggning: `MinimumPasswordLength` (standard 10), `AttemptsPerMinute` per IP-adress (standard 10, 0 för ingen gräns) och `SessionLifetime`, hur länge en oanvänd session gäller (standard 12 timmar). I utveckling är reglerna avstängda och sessionen gäller i 30 dagar. |

I utveckling loggar personalen in i personalpanelen med `ops@test.com` och lösenordet `ops`. Där finns också partnern `prop-platform` med nyckeln `dev-partner-key`, och firman `demo-firm` (Demo Firm) med API-nyckeln `dev-admin-key`, vars traders loggar in genom portalen på http://localhost:3002/terminal. Instrumenten är EURUSD, GBPUSD, USDJPY, AUDUSD, USDCAD, USDCHF, NZDUSD, EURGBP, EURJPY, GBPJPY, XAUUSD och XAGUSD, alla i gruppen `standard`. Kontot `demo` skapas med 100 000 USD, ett dagligt golv på 95 000 och ett släpande golv på 10 000 som låses vid 100 000. Det ägs av `demo@example.com` med lösenordet `demo-password`. Kontot `test` har samma inställningar och ägs av `test@test.com` med lösenordet `test`, för snabba inloggningar. Utvecklingskontona skapas direkt och följer inte admin-API:ts krav på e-post och lösenord. Allt detta gäller bara lokal utveckling.

## Begränsningar

- Tjänsten startar bara i miljön Development. Före produktion behövs HTTPS, hantering av hemligheter och ett prisflöde med licens.
- Tiingo- och Capital.com-flödena får inte visas för andra. En leverantör för produktionen väntar på licensvillkoren, och Nasdaq-100 och andra index kräver en egen licens.
- Öppettiderna har bara helgdagarna jul och nyår 2026-2027. Hela kalendern, med till exempel långfredagen och dagar med tidig stängning, ska läggas in från prisleverantören eller börserna innan terminalen går i drift. Kommer inga priser fast öppettiderna säger öppet, till exempel en helgdag som saknas, avvisas ordrar som förut med `StalePrice` efter `MaxQuoteAge`.
- Kontraktsstorlek, decimaler och villkor för index, råvaror och krypto är exempelvärden. Firmor som skapats före ADR 0049 har kvar sina grupper utan de nya instrumenten.
- Tradern kan inte själv byta eller återställa lösenordet än. Firmans system kan byta det via admin-API:t.
- Händelser skickas inte till firmor som webhooks, utan hämtas från händelseströmmen.
- En firmas namn kan inte ändras efter att den skapats, och firmor kan inte tas bort. Grupperna i konfigurationen ändras bara i konfigurationen.
- Journalen växer med alla priser och har ännu ingen arkivering.
- Candles använder UTC och dygnsgräns vid midnatt, inte 17:00 New York-tid.
- Historiken från Tiingo saknar tickvolym, och den görs om till bid med spreaden vid bytet. Tiden tjänsten var avstängd, eller flödet tyst, fylls från flödets historik när priserna kommer igen (ADR 0056). Har leverantören ingen historik för den, eller går den inte att nå, står luckan kvar.

## Tester

Testerna ligger i `trading/tests/Trading.Service.Tests`. De kör den riktiga tjänsten i minnet med ett prisflöde som testet styr och en klocka som bara flyttas när testet säger till. Därför ger de samma resultat varje gång. Testerna täcker API:t, statuskoderna, realtidsmeddelandena, motorloopen, candles, det syntetiska prisflödet och spärren mot andra miljöer än Development.

De flesta tester använder en journal i minnet som går via JSON som i Postgres. Den kan hålla inne eller fälla skrivningar, så att testerna kan visa att inget släpps innan det är sparat, att fel stoppar tjänsten, att omstart efter krasch och efter vanlig avstängning ger samma tillstånd och att skadad journal eller ändrad konfiguration stoppar starten.

`ChartHistoryTests` täcker graferna vid omstarter och byten av flöde: historiken laddas vid ett byte och bara då, och igen när graferna ska nå längre bakåt, i tim-, kvarts- och minutstaplar, äldre candles läses före en tid, den sparas och används vid nästa start, ett flödes grafer visar aldrig ett annat flödes priser, historik som inte gick att ladda försöks igen vid nästa start, och färdiga minuter sparas medan resten spelas upp från journalen efter en krasch. Ett glapp medan tjänsten var avstängd och ett medan flödet var tyst fylls från flödets historik, sparas och säger till terminalerna, en minut med priser på båda sidor är inget glapp, priserna efter ett glapp som inte gick att fylla kommer ändå med, och det påhittade flödet fyller inga glapp. `CandleStoreTests` visar att staplar går in i sin egen tidsram och längre, men aldrig kortare. `SyntheticPriceFeedTests` visar att den påhittade historiken är hela staplar som följer på varandra och slutar där livepriserna fortsätter.

`OrderToolsApiTests` täcker orderverktygen genom API:t: delstängning med en volym och en tom kropp som stänger resten, stäng allt och en symbols positioner, en väntande order som flyttas och en som avvisas, och trailing stop med ordern och stopparna.

`OwnLimitsApiTests` täcker traderns egna spärrar genom API:t (ADR 0054): gränser som syns på kontot med en lösare som väntar, firmans handelsdag i Stockholm, ett lås som stänger positionen och kan begäras igen, en order som avvisas med `AccountLocked`, ogiltiga gränser och tidszoner, och att en annan firma inte når kontots handelsdag.

`RulesAndSettingsTests` täcker regler och inställningar (ADR 0052): firmans regler som tradern får direkt i realtid och med `GET /rules`, kontrollen av dem och att en firma bara når sina egna konton, inställningar som följer tradern till en ny inloggning men inte når en annan trader, borttagning, och gränserna för nycklar, storlek och antal. `PostgresIdentityTests` visar att reglerna och inställningarna kommer tillbaka exakt ur databasen.

`TerminalProfileTests` täcker profilen per server (ADR 0058): standarden för varje sort, en profil som firman sätter och som kommer med inloggningen, kontrollen av startstorleken, länkarna och riskvarningen, att en firma i konfigurationen får sin och behåller en satt profil, och traderns namn. `AuthTests` visar inloggningen utan server, med valet mellan firmor när uppgifterna passar hos flera, att en firma utan lösenord i terminalen aldrig loggar in med ett, och sökningen på servrar som kräver två tecken och visar högst fem listade. `RulesAndSettingsTests` visar också andelen och utbetalningen i reglerna.

`MarketHoursTests` täcker öppettiderna: vad tradern ser för valutor, DAX och krypto, att en order på lördagen avvisas med `MarketClosed` medan krypto går att handla, att Capital.com:s egna tider gäller med det flödet så att UK100 är öppet efter börsens stängning, att ett flöde kan ge en symbol dygnet runt, att det syntetiska flödet alltid är öppet, att fel i öppettiderna och i flödenas tider stoppar starten och att stängda dagar kan vara hela eller delar.

`TradingConditionsTests` täcker instrumenten, firmans grupper med villkor, ändrade villkor som gäller direkt, en symbol i bruk, en konfigurerad grupp som inte kan ändras och ändrade villkor som finns kvar efter en krasch. `RecoveryTests` visar också att en konfiguration med fler instrument godtas efter en krasch, och att en som ändrar händelserna stoppar starten.

`PartnerApiTests` täcker partner-API:t: en ny firma handlar direkt i sin egen grupp, en firma i EUR handlar guld genom USD och en valuta som inte erbjuds nekas, dess traders ser gruppens instrument, priser och grafer som terminalen laddar, firman ser bara sina egna händelser och konton, servrar är unika och giltiga, bara partners skapar firmor, en partner ser bara sina egna firmor, en ny nyckel ersätter den gamla, nya firmor listas inte men deras traders loggar in, en firma listas och får sin portal som inloggning, en ogiltig adress nekas, firmor och grupper finns kvar efter en krasch och en vanlig omstart, och en grupp från ett avbrutet försök tas över.

`StaffApiTests` täcker personal-API:t (ADR 0057): personalens egen session, som en traders session och en firmas nyckel inte ersätter, översiktens siffror, en symbol och ett helt flöde utan priser medan marknaderna är öppna, en firmas system som slutar läsa sina händelser, servrarna med vem som gjorde dem, grupper och sök, en server som personalen gör och vars nyckel visas en gång, en partners nyckel som stoppas utan att visas medan partnern hämtar en ny, en konfigurerad servers nyckel som inte kan stoppas, listningen, serverns grupper, siffror och händelser med ett kontos, sökningen, exponeringen i USD, ett glapp som inte gick att fylla och fylls vid ett nytt försök, historiken som laddas om, syntetiska priser utan historik, instrumentens öppettider och veckor, och motorns kö, sparningar, start och partners. `PostgresStaffPanelTests` visar mot riktig Postgres personalen, plattformens logg, en server personalen gjort som konfigurationen inte tar över, glappen och historikens tid, journalens senaste händelser per grupp, dagens aktivitet och storlek, och traders per firma.

`AuthTests` täcker inloggning, utloggning, begränsningen av försök, ägarskap, API-nycklar och att firmor inte når varandras grupper, traders eller konton. `IntegrationApiTests` täcker admin-API:t som firmornas system bygger på: versionen, uppslag av traders, byte av lösenord, kontot, det förankrade golvet, uttag som bara dras en gång, händelseströmmen per firma med väntan, och inloggningslänkar som fungerar en gång, går ut och bara gäller firmans traders och deras konton, och konton som pausas och återupptas och kan få samma kommando igen. `TiingoPriceFeedTests` täcker tolkning, avrundning, de senaste priserna och nya anslutningar mot en låtsad Tiingo med riktig WebSocket, och historiken: att den görs om till bid utan oförändrade och ofärdiga staplar, och delas upp i anrop som håller sig inom Tiingos gräns, och att Tiingo bara får valutor och metaller. `CapitalComPriceFeedTests` täcker på samma sätt mot en låtsad Capital.com: inloggning med nyckel och lösenord, de senaste priserna, prenumeration och ping med sessionen, nya försök efter en nekad eller avslutad session, historik i bid utan oförändrade och ofärdiga staplar, perioder om högst 950 staplar, och att för många instrument stoppar starten. Testerna läser aldrig utvecklarens user secrets.

`PostgresJournalTests`, `PostgresChartStoreTests` och `PostgresIdentityTests` kör mot riktig Postgres i en container via Testcontainers och kräver Docker. De visar att decimaler, tider och prisflöden kommer tillbaka exakt, att priser kan läsas från ett flöde, att bara priser har ett flöde, att flödet för det senaste priset går att få fram, att ett flödes historik bara ersätter dess egna tidigare staplar, att gamla staplar tas bort, att ett kontos händelser läses i sidor både framåt och bakåt från de senaste, att hela tjänsten kan startas om mot Postgres, att firmor behåller grupper och nycklar, och att en firmas id och grupper bara kan tas en gång.
