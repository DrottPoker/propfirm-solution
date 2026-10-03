# Spec: propfirm-tjänsten

- Fas: 4c, portalens del i 4d
- Status: Implementerad i `prop/src/Prop.Api`
- Datum: 2026-10-03

## Syfte

Tjänsten driver firmornas challenges. Den har firmans API och portalens API, kör regelmotorn (se [specen för regelmotorn](regelmotor.md)) för varje challenge-konto och kopplar den till handelsplattformen genom dess publika admin-API (ADR 0012). Besluten om hur det hålls korrekt finns i [ADR 0013](../adr/0013-journal-och-utkorg-i-propfirm-tjansten.md). Portalen och dess API beskrivs i [specen för portalen](portal.md).

## Delar

| Del | Ansvar |
|---|---|
| `FirmCatalog`, `FirmApiKeyFilter` | Firmorna: id, namn, nyckel för firmans API, server och nyckel på handelsplattformen, webhook och portal. |
| `AccountActions` | Det som går att göra med ett konto, gemensamt för firmans API och portalen. |
| `PortalEndpoints`, `PortalAuth`, `PortalFirmFilter` | Portalens API, sessioner och att firman känns igen på värdnamnet. |
| `PortalUsers`, `PortalSeeder` | Traders och administratörer i portalen, inbjudningar, och de administratörer och traders som konfigurerats för utveckling. |
| `ChallengeCatalog` | Challenges per firma. Kontrolleras av regelmotorn och mot kända tidszoner. |
| `ChallengeService` | Kör regelmotorn med kontot låst. Steget, tillståndet, kommandona och webhooks sparas i samma transaktion. |
| `ITradingPlatform`, `TradingPlatformClient` | Handelsplattformens admin-API v1, även kontot värderat just nu för portalen. Fler plattformar kan få egna adaptrar. |
| `TradingEventConsumer` | Läser firmans händelseström och gör om händelser till fakta. Löpnumret sparas i samma transaktion som besluten. |
| `TradingCommandWorker` | Kör kommandona mot handelsplattformen i ordning per firma och försöker igen vid avbrott. |
| `TradingDayScheduler` | Startar handelsdagen för varje öppet konto när dagen börjar, och kommer ikapp efter en omstart. |
| `WebhookWorker` | Skickar signerade webhooks till firman och försöker igen tills de tas emot. |
| `ChallengeSeeder` | Skapar firmans konfigurerade challenges vid start. |

## Flöde

```
firmans system -> POST /accounts -> regelmotorn: öppna konto för fas 1
-> kommando: öppna konto -> handelsplattformen -> händelsen AccountCreated i strömmen
-> regelmotorn: kontot är öppet -> kommandon: sätt golv för total och daglig förlust
tradern handlar -> PositionOpened och PositionClosed i strömmen -> handelsdagar och saldo
-> fasen klar -> kommandon: stäng kontot, öppna nästa -> webhook account.passed
midnatt i challengens tidszon -> regelmotorn: ny handelsdag -> kommando: lägg om det dagliga golvet
golvet bryts -> EquityFloorBreached i strömmen -> underkänd, med händelsen som bevis -> webhook account.breached
```

## Från handelsplattformen till regelmotorn

| Händelse | Blir |
|---|---|
| `AccountCreated` | `AccountOpened` för handelsdagen när kontot öppnades. |
| `PositionOpened` | `PositionOpened` för handelsdagen när positionen öppnades. |
| `PositionClosed` | `AccountUpdated` med saldot efter och antalet öppna positioner. |
| `EquityFloorBreached` | `FloorBreached`. Hela händelsen sparas som bevis i steget. |
| `AccountDisabled` | `AccountDisabled`. |
| `EquityFloorSet` | Bara golvets nivå, för visning. |

Händelser om konton som tjänsten inte har öppnat, och andra händelser, flyttar bara läspositionen framåt.

## Handelsdagar

En handelsdag börjar vid challengens klockslag i dess tidszon och har namn efter det lokala datum den börjar på. En dag som börjar 17:00 på en söndag är alltså söndagens. Övergången mellan sommartid och vintertid följs. Ett klockslag i timmen som hoppas över flyttas till första tiden som finns, och ett klockslag i timmen som upprepas räknas första gången.

## Konton på handelsplattformen

- Id är `{firma}-{nummer}-{fas}`, till exempel `demo-firm-1001-1`. Numret börjar på 1001 för varje firma, och samma id ges vid ett nytt försök.
- Traderns användare på handelsplattformen skapas när det första kontot öppnas, med ett slumpat lösenord som aldrig visas. Tradern loggar in med en länk från `POST /accounts/{id}/login-link`, eller med knappen "Open terminal" i portalen.

## Tabeller

| Tabell | Innehåll |
|---|---|
| `challenge_definitions` | Firmans challenges. |
| `traders` | Firmans traders, deras användare på handelsplattformen och hash av lösenordet till portalen. E-postadressen är unik inom firman. |
| `firm_admins` | Firmans administratörer i portalen. E-postadressen är unik inom firman. |
| `portal_invites` | Inbjudningar till portalen: hash av token, trader, när den går ut och när den användes. |
| `data_protection_keys` | Nycklarna som skyddar portalens sessioner. |
| `challenge_accounts` | Challenge-konton med regelmotorns tillstånd, status, fas, handelsdag och firmans referens. |
| `challenge_steps` | Varje indata och beslut i ordning, med handelsplattformens händelse som bevis. |
| `trading_accounts` | Konton på handelsplattformen per fas, med saldo, öppna positioner och golvens nivåer som plattformen senast rapporterade. |
| `trading_cursors` | Hur långt varje firmas händelseström är läst. |
| `trading_commands` | Kommandon till handelsplattformen, i ordning per firma. |
| `webhook_deliveries` | Webhooks till firman, med försök och svar. |
| `firm_counters` | Nästa kontonummer per firma. |

## Firmans API

Alla vägar börjar med `/api/firm/v1` och kräver firmans nyckel i headern `X-Api-Key`. Andra firmors data svarar 404.

| Metod och väg | Beskrivning |
|---|---|
| `PUT /challenges/{challengeId}` | Skapar eller ersätter en challenge. Konton som redan har startat behåller sina regler. |
| `GET /challenges` | Firmans challenges. |
| `POST /accounts` | Startar en challenge med `{ "email", "challengeId", "reference" }`. Samma `reference` igen ger samma konto. Svarar 201, eller 200 för ett konto som redan finns. |
| `GET /accounts?email=` | Traderns konton. |
| `GET /accounts/{id}` | Status, fas, konto på handelsplattformen, startsaldo och valuta, handelsdagar, vinstmål, saldo och golvens nivåer. |
| `GET /accounts/{id}/history` | Varje indata och beslut, med bevisen vid brott. |
| `POST /accounts/{id}/approve-funding` | Firman har gjort sina kontroller, och tradern får funded-kontot. 409 innan faserna är klara. |
| `POST /accounts/{id}/cancel` | Avbryter med `{ "reason" }` och stänger kontot på handelsplattformen. |
| `POST /accounts/{id}/login-link` | En engångslänk som loggar in tradern i terminalen på fasens konto. |
| `POST /accounts/{id}/invite` | En inbjudningslänk till portalen, för firman att skicka till tradern. Gäller en gång i 7 dagar och ersätter traderns äldre oanvända. |

Tjänsten publicerar OpenAPI på `/openapi/v1.json`. Dokumentet skrivs till `prop/portal/openapi/prop-api.json` när tjänsten byggs, och CI kontrollerar att det är committat.

## Webhooks

| Händelse | När |
|---|---|
| `account.stage_started` | En fas har fått sitt konto. |
| `account.passed` | En fas är klar. |
| `account.funding_awaited` | Alla faser är klara, och firman ska godkänna funded-kontot. |
| `account.breached` | Ett golv bröts. Innehåller orsaken, nivån och equity. |
| `account.cancelled` | Kontot avbröts. |

- Kroppen är `{ "id", "type", "createdAt", "account": { "id", "number", "email", "challengeId", "reference" }, "data": { ... } }`, där `data` är regelmotorns beslut.
- Headrarna `Prop-Webhook-Id` och `Prop-Webhook-Event` anger vilken leverans och händelse det är. `Prop-Signature` är `t={unix-sekunder},v1={hex}`, där `hex` är HMAC-SHA256 med firmans hemlighet av `{t}.{kropp}`.
- Svar 2xx räknas som mottaget. Annars görs ett nytt försök efter 30 sekunder, med dubbel väntan varje gång och högst 6 timmar, i 16 försök (ungefär ett och ett halvt dygn). Samma leverans kan komma mer än en gång och känns igen på sitt id.

## Konfiguration

| Sektion | Innehåll |
|---|---|
| `ConnectionStrings:Prop` | Propfirm-plattformens databas. Lokalt Postgres från `deploy/docker-compose.yml`. |
| `TradingPlatform` | `Url` till handelstjänsten (slutar med `/`), hur länge ett anrop väntar på nya händelser och hur många som läses åt gången. |
| `Firms` | Firmorna: `Id`, `Name`, `ApiKeySha256`, `Trading` (`Server`, `ApiKey`, `Group`), `Webhook` (`Url`, `Secret` på minst 32 tecken), `Portal` (se [specen för portalen](portal.md)), `SeedChallenges`, `SeedAdmins` och `SeedTraders`. |
| `Login` | Regler för lösenord och inloggning: `MinimumPasswordLength` (standard 10), `AttemptsPerMinute` per IP-adress (standard 10, 0 för ingen gräns) och `SessionLifetime`, hur länge en oanvänd session gäller (standard 12 timmar). I utveckling är reglerna avstängda och sessionen gäller i 30 dagar. |

I utveckling finns firman `demo-firm` med nyckeln `dev-prop-key`. Den använder servern `demo-firm` på handelsplattformen med nyckeln `dev-admin-key`, har challengen `two-step-100k`, portalen på http://localhost:3002, administratören `admin@test.com` med lösenordet `admin` och traderna `anna@test.com` med `anna` och `test@test.com` med `test`. Allt detta gäller bara lokal utveckling.

## Begränsningar

- Tjänsten startar bara i miljön Development. Före produktion behövs HTTPS och hantering av hemligheter, till exempel för handelsplattformens nyckel och webhook-hemligheten.
- Tjänsten körs som en instans. Läsningen av händelseströmmen och kommandona har ännu inga lås mellan instanser.
- Firmor konfigureras. Registrering kommer med självbetjäningen.
- Utbetalningar saknas.
- Ett kommando som handelsplattformen avvisar läggs åt sidan som misslyckat och syns bara i databasen och loggen.

## Tester

Testerna ligger i `prop/tests/Prop.Api.Tests`. De kör tjänsten mot riktig Postgres i en container via Testcontainers och kräver Docker. Handelsplattformen är en låtsad version i minnet som beter sig som vår. Klockan flyttas bara när testet säger till, och firmans webhooks tas emot av testet.

- `FirmApiTests`: nyckeln, challenges och deras kontroll, start av konto med golven i rätt ordning, firmans referens, felaktiga starter, att firmor inte ser varandras konton, inloggningslänken och inbjudan till portalen.
- `PortalApiTests`: portalens API, se [specen för portalen](portal.md).
- `ChallengeFlowTests`: dagliga golvet vid midnatt i Stockholm, en klarad fas som stänger kontot och öppnar nästa, brott med bevis, godkänd finansiering, annullering, avbrott i handelsplattformen där kommandona behåller sin ordning, omstart där varje händelse ändå hanteras exakt en gång, och signerade webhooks som skickas igen.
- `TradingPlatformClientTests`: klienten mot svar som handelsplattformens, att regelmotorns golv blir plattformens regler och att ett konto värderas med sina golv.
- `TradingContractTests`: varje väg och fält som klienten använder finns i `contracts/trading/trading-service.json`.
- `TradingDaysTests`: handelsdagar i olika tidszoner, vid klockslag på kvällen och vid sommartid och vintertid.
