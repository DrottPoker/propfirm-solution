# Spec: handelstjänsten

- Fas: 2a, 2b och 3b
- Status: Implementerad i `trading/src/Trading.Service`
- Datum: 2026-10-02

## Syfte

Tjänsten kör handelsmotorn (se [specen för handelsmotorn](handelsmotor.md)) och gör den nåbar för handelsterminalen. Den tar emot priser från ett prisflöde, tar emot kommandon via REST och skickar priser, kontovärde och händelser i realtid via SignalR. Alla indata och händelser sparas i en journal i Postgres, så att tjänsten kan startas om utan att något går förlorat.

## Delar

| Del | Ansvar |
|---|---|
| `EngineHost` | Äger motorn. Alla indata och frågor går genom en kö och körs en i taget. Sätter tidsstämplar och löpnummer, skriver till journalen och återställer från den vid start. |
| `IEngineJournal`, `PostgresEngineJournal` | Journalen: indata, händelser och ögonblicksbilder i Postgres. |
| `EventLog` | Skickar sparade händelser vidare till realtidsdelen. |
| `EngineHealthCheck` | `/health` är friskt först när journalen är uppspelad, och bara så länge den går att skriva. |
| `IPriceFeed` | Gränssnitt för prisflöden. En adapter per dataleverantör. |
| `SyntheticPriceFeed` | Slumpvandring för lokal utveckling. Samma frö ger samma priser. Standard. |
| `TiingoPriceFeed` | Riktiga priser för valutor och guld från Tiingos gratisplan, för utveckling (ADR 0010). Hämtar de senaste priserna vid varje anslutning och skickar högst ett pris per symbol och kvart sekund. |
| `PriceFeedPump` | Flyttar priser från flödet till candles och motorn. Bygger graferna från sparade priser vid start, eller från flödets historik första gången. |
| `CandleStore` | Bygger candles av bid per symbol och tidsram (M1, M5, M15, M30, H1, H4, D1). |
| `TradingHub`, `RealtimePublisher` | Realtid via SignalR. |
| `AccountSeeder` | Skapar utvecklingskonton och deras ägare vid start om de inte redan finns. |
| `TenantCatalog`, `AdminApiKeyFilter` | Firmorna: server, namn, grupper och API-nycklar. |
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
| `engine_inputs` | Varje indata med löpnummer. Priser i egna kolumner, kommandon som JSON. |
| `engine_events` | Varje händelse med löpnummer, konto och kontots grupp. Används för `GET /events` och firmans händelseström. |
| `engine_snapshots` | Motorns tillstånd efter ett visst indata, med konfigurationens fingeravtryck. De tre senaste behålls. |
| `users`, `account_owners` | Traders och vilka konton de äger. |
| `data_protection_keys` | Nycklarna som skyddar inloggningscookies. |
| `login_links` | Engångslänkar för inloggning: hash av token, trader, konto och när länken går ut. Länkar som gick ut för mer än ett dygn sedan tas bort när nya skapas. |
| `schema_migrations` | Vilka migreringar som körts. Tabellerna skapas och uppgraderas en gång per start, innan något lager använder databasen första gången. Nycklarna för cookies läses nämligen innan motorn startar. |

**Skrivning:** Motorn tillämpar indata direkt. En separat skrivare sparar dem i batcher i en transaktion. Svar på kommandon, händelser i realtid och svar på frågor släpps först när det de bygger på är sparat. Misslyckas en skrivning tre gånger stoppas tjänsten.

**Start:**

1. Kör migreringar.
2. Läs den senaste ögonblicksbilden och återställ motorn från den.
3. Spela upp indata efter den. Vägra starta om konfigurationen har ändrats sedan ögonblicksbilden och det finns indata att spela upp.
4. Kontrollera att uppspelningen gav lika många händelser som journalen har. Vägra starta annars.
5. Spara en ny ögonblicksbild med den aktuella konfigurationen.
6. Bygg graferna från sparade priser och låt det syntetiska flödet fortsätta från de senaste priserna.

**Avstängning:** Arbete som inte hunnit köras avbryts. Den sista batchen sparas tillsammans med en ögonblicksbild, så att nästa start inte behöver spela upp något.

**Ändrad konfiguration:** Stäng av tjänsten på vanligt sätt (Ctrl+C), ändra konfigurationen och starta igen. Efter en krasch måste tjänsten först startas en gång med den gamla konfigurationen.

## REST-API

Tjänsten publicerar ett OpenAPI-dokument på `/openapi/v1.json`. Samma dokument skrivs till kontraktet `contracts/trading/trading-service.json` när tjänsten byggs. Terminalens typer och propfirm-plattformens klient genereras från det (ADR 0012).

### Inloggning

| Metod och väg | Beskrivning |
|---|---|
| `POST /api/auth/login` | Loggar in med `{ "server", "email", "password" }`, där `server` är firmans id. Sätter sessionscookien. Fel server, e-post eller lösenord ger samma svar. Högst `Login:AttemptsPerMinute` försök per minut och IP-adress, som standard 10. |
| `POST /api/auth/link` | Loggar in med `{ "token" }` från en inloggningslänk. Länken fungerar en gång. Samma begränsning av försök som vid inloggning. |
| `POST /api/auth/logout` | Loggar ut. |
| `GET /api/auth/me` | Den inloggade tradern, firmans server och kontona tradern äger. |
| `GET /api/servers` | Servrarna som går att logga in på, med id och firmans namn, sorterade efter namn. Kräver ingen inloggning. |

### För tradern

Alla vägar börjar med `/api/accounts/{accountId}` och kräver att tradern är inloggad och äger kontot. Andras konton svarar 404.

| Metod och väg | Beskrivning |
|---|---|
| `GET` | Kontot värderat till senaste priser. Varje golv har `headroom`: hur långt equity kan falla innan golvet bryts. |
| `GET /instruments` | Gruppens instrument med villkor: hävstång, påslag och provision. |
| `GET /prices` | Senaste priser efter påslag. |
| `GET /candles/{symbol}?timeframe=M1&count=500` | Candles av bid som kontot ser det, äldst först. Högst 5 000. |
| `GET /events?after={sequence}&limit=500` | Kontots händelser efter ett sekvensnummer. Högst 1 000 per anrop. |
| `POST /orders` | Lägger en order. Klienten skapar order-id:t. |
| `DELETE /orders/{orderId}` | Tar bort en väntande order. |
| `POST /positions/{positionId}/close` | Stänger en position. |
| `PUT /positions/{positionId}/stops` | Sätter eller tar bort stop loss och take profit. |

### Administration

För firmans egna system, till exempel propfirm-plattformen (ADR 0012). Alla vägar börjar med `/api/admin/v1`, kräver firmans API-nyckel i headern `X-Api-Key` och når bara firmans egna grupper, traders och konton. Andras svarar 404.

| Metod och väg | Beskrivning |
|---|---|
| `POST /users` | Skapar en trader med `{ "email", "password" }`. Lösenordet ska ha minst `Login:MinimumPasswordLength` tecken, som standard 10. E-postadressen är unik inom firman. |
| `GET /users?email=` | Hittar en av firmans traders via e-post. |
| `PUT /users/{userId}/password` | Byter traderns lösenord med `{ "password" }`. |
| `POST /users/{userId}/login-links` | Skapar en inloggningslänk till terminalen, valfritt med `{ "accountId" }` för kontot som ska öppnas. Svarar med `url` och `expiresAt`. Länken fungerar en gång i 2 minuter. |
| `POST /accounts` | Skapar ett konto i en av firmans grupper, ägt av en av firmans traders (`ownerUserId`). |
| `GET /accounts/{accountId}` | Kontot värderat till senaste priser, som tradern ser det. |
| `PUT /accounts/{accountId}/floors/{floorId}` | Sätter ett golv, till exempel `{ "rule": { "kind": "FixedFloor", "level": 95000 } }` eller `{ "rule": { "kind": "AnchoredFloor", "distance": 5000, "anchor": "Balance" } }`. |
| `DELETE /accounts/{accountId}/floors/{floorId}` | Tar bort ett golv. |
| `POST /accounts/{accountId}/close` | Stänger kontot. |
| `GET /events?after=0&limit=100&wait=0` | Firmans händelser efter ett löpnummer, äldst först, med `cursor` för nästa anrop. Högst 1 000 per anrop. Med `wait` väntar anropet upp till 30 sekunder på nya händelser. Bara sparade händelser visas, så ingen händelse kan försvinna vid en omstart. |

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

Den senaste candlen uppdateras i terminalen med priserna från `Prices`. Vid omladdning hämtas historiken från `GET /candles`.

## Konfiguration

| Sektion | Innehåll |
|---|---|
| `Trading` | Instrument, grupper, max ålder på priser och utvecklingskonton (`SeedAccounts`). |
| `SyntheticFeed` | Frö, intervall, längd på historiken och startpriser per symbol. |
| `Realtime` | Takt för priser och konto. |
| `Journal` | Antal indata mellan ögonblicksbilder, hur många som behålls och hur lång prishistorik graferna byggs från vid start. |
| `Tenants` | Firmorna: id (servern, till exempel `nordic-prop`), namn, grupper och SHA-256 av API-nyckeln. |
| `PriceFeed` | `Provider` (`Synthetic` eller `Tiingo`). För Tiingo även `Tiingo:ApiKey`, som sätts med `dotnet user-secrets`. |
| `ConnectionStrings:Trading` | Databasen för journalen. Lokalt Postgres från `deploy/docker-compose.yml`. |
| `Cors:AllowedOrigins` | Webbadresser som får anropa API:t, till exempel terminalen på `http://localhost:3001`. |
| `Terminal:Url` | Terminalens adress, till exempel `http://localhost:3001/`. Används i inloggningslänkar. |
| `Login` | Regler för lösenord och inloggning: `MinimumPasswordLength` (standard 10), `AttemptsPerMinute` per IP-adress (standard 10, 0 för ingen gräns) och `SessionLifetime`, hur länge en oanvänd session gäller (standard 12 timmar). I utveckling är reglerna avstängda och sessionen gäller i 30 dagar. |

I utveckling finns firman `demo-firm` (Demo Firm) med API-nyckeln `dev-admin-key`. Kontot `demo` skapas med 100 000 USD, ett dagligt golv på 95 000 och ett släpande golv på 10 000 som låses vid 100 000. Det ägs av `demo@example.com` med lösenordet `demo-password`. Kontot `test` har samma inställningar och ägs av `test@test.com` med lösenordet `test`, för snabba inloggningar. Utvecklingskontona skapas direkt och följer inte admin-API:ts krav på e-post och lösenord. Allt detta gäller bara lokal utveckling.

## Begränsningar

- Tjänsten startar bara i miljön Development. Före produktion behövs HTTPS, hantering av hemligheter och ett prisflöde med licens.
- Tiingo-flödet får inte visas för andra. En leverantör för produktionen väntar på licensvillkoren.
- Det finns inga handelstider. Med Tiingo kommer inga nya priser när marknaden är stängd, så ordrar avvisas med `StalePrice` efter `MaxQuoteAge`. Det syntetiska flödet går dygnet runt.
- Tradern kan inte själv byta eller återställa lösenordet än. Firmans system kan byta det via admin-API:t.
- Händelser skickas inte till firmor som webhooks, utan hämtas från händelseströmmen.
- Journalen växer med alla priser och har ännu ingen arkivering.
- Candles använder UTC och dygnsgräns vid midnatt, inte 17:00 New York-tid.

## Tester

Testerna ligger i `trading/tests/Trading.Service.Tests`. De kör den riktiga tjänsten i minnet med ett prisflöde som testet styr och en klocka som bara flyttas när testet säger till. Därför ger de samma resultat varje gång. Testerna täcker API:t, statuskoderna, realtidsmeddelandena, motorloopen, candles, det syntetiska prisflödet och spärren mot andra miljöer än Development.

De flesta tester använder en journal i minnet som går via JSON som i Postgres. Den kan hålla inne eller fälla skrivningar, så att testerna kan visa att inget släpps innan det är sparat, att fel stoppar tjänsten, att omstart efter krasch och efter vanlig avstängning ger samma tillstånd och att skadad journal eller ändrad konfiguration stoppar starten.

`AuthTests` täcker inloggning, utloggning, begränsningen av försök, ägarskap, API-nycklar och att firmor inte når varandras grupper, traders eller konton. `IntegrationApiTests` täcker admin-API:t som firmornas system bygger på: versionen, uppslag av traders, byte av lösenord, kontot, det förankrade golvet, händelseströmmen per firma med väntan, och inloggningslänkar som fungerar en gång, går ut och bara gäller firmans traders och deras konton. `TiingoPriceFeedTests` täcker tolkning, avrundning, de senaste priserna och nya anslutningar mot en låtsad Tiingo med riktig WebSocket. Testerna läser aldrig utvecklarens user secrets.

`PostgresJournalTests` och `PostgresIdentityTests` kör mot riktig Postgres i en container via Testcontainers och kräver Docker. De visar att decimaler och tider kommer tillbaka exakt och att hela tjänsten kan startas om mot Postgres.
