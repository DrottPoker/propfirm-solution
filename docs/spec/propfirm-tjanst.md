# Spec: propfirm-tjänsten

- Fas: 4c, portalens del i 4d, utbetalningar i 5, firmor i databasen, registrering och sandlåda i 6, platser och betalning i 7, köp i portalen i 8, granskning och avstängning i 9a
- Status: Implementerad i `prop/src/Prop.Api`
- Datum: 2026-10-03

## Syfte

Tjänsten driver firmornas challenges. Den har firmans API och portalens API, kör regelmotorn (se [specen för regelmotorn](regelmotor.md)) för varje challenge-konto och kopplar den till handelsplattformen genom dess publika admin-API (ADR 0012). Besluten om hur det hålls korrekt finns i [ADR 0013](../adr/0013-journal-och-utkorg-i-propfirm-tjansten.md). Portalen och dess API beskrivs i [specen för portalen](portal.md), hur firmor registrerar sig själva i [specen för registrering och sandlåda](registrering.md), köp av challenges i portalen i [specen för köp i portalen](kop.md), vad firman betalar oss i [specen för platser och betalning](platser-och-betalning.md) och vår granskning av firmor och avstängning i [specen för granskning och avstängning](granskning.md).

## Delar

| Del | Ansvar |
|---|---|
| `FirmCatalog`, `FirmApiKeyFilter` | Firmorna i minnet: id, namn, status, hash av nyckeln för firmans API, server och nyckel på handelsplattformen, webhook, portal och avstängning. Den som ändrar en firma lägger in den nya versionen (ADR 0017). |
| `FirmStore`, `SecretProtector` | Firmorna i databasen. Handelsplattformens nyckel, webhook-hemligheten och firmornas dokument krypteras med AES-GCM och `Secrets:Key`. |
| `FirmSeeder` | Sparar de konfigurerade firmorna i databasen vid start och laddar alla firmor. Stoppar starten om konfigurationen är fel, eller om en konfigurerad firma har samma id som en som registrerat sig. |
| `FirmProvisioner` | Skapar servern på handelsplattformen för varje firma som registrerat sig, och flyttar firman till sandlådan med en första challenge. Försöker igen tills det lyckas. |
| `FirmLoops` | Startar bakgrundsjobben per firma när firman har en server, även för firmor som blir klara medan tjänsten kör. |
| `SignupService` | Registrering, bekräftelse av e-postadressen och skapandet av firman, adressen och administratören. |
| `FirmAdmins` | Administratörernas engångslänkar, inbjudningar och borttagning. |
| `IEmailSender`, `SmtpEmailSender` | E-post från plattformen med SMTP. Lokalt fångas allt av Mailpit. |
| `AccountActions` | Det som går att göra med ett konto, gemensamt för firmans API och portalen. |
| `PayoutActions`, `PayoutQueries` | Utbetalningar: begäran, firmans beslut och listor, gemensamt för firmans API och portalen. |
| `PortalEndpoints`, `PortalAuth`, `PortalFirmFilter` | Portalens API, sessioner och att firman känns igen på värdnamnet. |
| `PortalUsers`, `PortalSeeder` | Traders och administratörer i portalen, inbjudningar, och de administratörer och traders som konfigurerats för utveckling. |
| `ChallengeCatalog` | Challenges per firma. Kontrolleras av regelmotorn och mot kända tidszoner. |
| `ChallengeService` | Kör regelmotorn med kontot låst. Steget, tillståndet, kommandona och webhooks sparas i samma transaktion. |
| `ITradingPlatform`, `TradingPlatformClient` | Handelsplattformens admin-API v1, även kontot värderat just nu för portalen. Fler plattformar kan få egna adaptrar. |
| `ITradingPartner`, `TradingPartnerClient` | Handelsplattformens partner-API v1, som skapar servrar åt firmor som registrerar sig (ADR 0016). |
| `TradingEventConsumer` | Läser firmans händelseström och gör om händelser till fakta. Löpnumret sparas i samma transaktion som besluten. |
| `TradingCommandWorker` | Kör kommandona mot handelsplattformen i ordning per firma och försöker igen vid avbrott. En firmas kommandon väntar tills den har en server. Ett nekat uttag rapporteras till regelmotorn i samma transaktion, så att utbetalningen blir `Failed`. |
| `TradingDayScheduler`, `TradingStreamProgress` | Startar handelsdagen för varje öppet konto när dagen börjar, och kommer ikapp efter en omstart. En dag som avslutar en challenge på tid väntar tills firmans händelseström är läst förbi dagens början. |
| `WebhookWorker`, `WebhookOutbox` | Webhooks till firman köas i samma transaktion som ändringen bakom dem, och skickas signerade tills de tas emot. |
| `OrderService`, `OrderStore` | Köp i portalen: ordern med priset, betalningen som startar kontot i samma transaktion, återbetalningar, bestridanden och inbjudan till köparen (ADR 0019). |
| `PriceCatalog` | Challengernas priser i portalen, skilda från challengerna. |
| `StripeClient`, `StripeSignature` | Stripe Checkout med firmans egen nyckel, och kontrollen av Stripes webhooks. |
| `ShopEndpoints`, `PaymentEndpoints`, `OrderActions` | Butiken och köparens order i portalen, adminpanelens ordrar, priser och betalningar, firmans API för ordrar och priser och Stripes webhook. |
| `ChallengeSeeder` | Skapar firmans konfigurerade challenges vid start. |
| `SlotService` | Firmans platser: hur många challenges den kan ha öppna och hur många som är tagna, också av ordrar som väntar på betalning. Starter och ordrar låser firmans platser i sin transaktion. |
| `BillingService`, `BillingStore`, `BillingRules` | Vad firman betalar oss: go-live, månaden i förskott, fler och färre platser, nytt kort, nekade betalningar, paus av firmans challenges och priserna (ADR 0020). |
| `IBillingGateway`, `StripeBillingGateway`, `TestBillingGateway` | Betalsidor och dragningar av det sparade kortet hos Stripe med vårt konto, eller testbetalningar i utveckling. |
| `BillingWorker` | Varje minut och när något ändras: betalsidor som gått ut, kort som ska dras, nästa månad, paus och återupptagande, automatisk utökning och varningen om platserna. |
| `BillingEndpoints` | Adminpanelens betalning, firmans API för platserna och Stripes webhook för våra egna betalningar. |
| `ReviewService`, `ReviewStore`, `ReviewEndpoints` | Vår granskning av firman: ansökan, dokumenten, att skicka den med handpenningen, våra beslut och avstängning (ADR 0021). |
| `StaffUsers`, `StaffAuth`, `StaffSeeder`, `OpsHost` | Vår personal, dess session på vår adminvys adress och att vår adminvy bara finns där. |
| `OpsEndpoints`, `OpsFirms`, `StaffNotifier` | Vår adminvys API, firman som personalen ser den och mejlet till personalen när en ansökan kommer. |

## Flöde

```
firmans system -> POST /accounts -> regelmotorn: öppna konto för fas 1
-> kommando: öppna konto -> handelsplattformen -> händelsen AccountCreated i strömmen
-> regelmotorn: kontot är öppet -> kommandon: sätt golv för total och daglig förlust
tradern handlar -> PositionOpened och PositionClosed i strömmen -> handelsdagar och saldo
-> fasen klar -> kommandon: stäng kontot, öppna nästa -> webhook account.passed
midnatt i challengens tidszon -> regelmotorn: ny handelsdag -> kommando: lägg om det dagliga golvet
golvet bryts -> EquityFloorBreached i strömmen -> underkänd, med händelsen som bevis -> webhook account.breached
ingen ny position på 30 dagar, eller fasens tidsgräns passerad -> ny handelsdag -> underkänd, kontot stängs -> webhook account.expired
firmans månad börjar obetald -> challengen pausas -> kommando: pausa kontot -> webhook account.paused
tradern begär utbetalning -> kommando: ta ut vinsten -> BalanceAdjusted i strömmen -> väntar på firman -> webhook payout.requested
firman godkänner -> webhook payout.approved -> firman betalar själv och markerar som betald -> webhook payout.paid
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
| `BalanceAdjusted` | `BalanceAdjusted` med operationens id, beloppet och saldot efteråt. Hela händelsen sparas i steget. |

Händelser om konton som tjänsten inte har öppnat åt firman, och andra händelser, flyttar bara läspositionen framåt.

## Handelsdagar

En handelsdag börjar vid challengens klockslag i dess tidszon och har namn efter det lokala datum den börjar på. En dag som avslutar en challenge för att tiden tagit slut startas först när firmans händelseström är läst till slutet med en läsning som började minst 30 sekunder efter dagens början. Då räknas en affär som gjordes i sista stund, även när händelsen kommer sent, till exempel efter en omstart. En dag som börjar 17:00 på en söndag är alltså söndagens. Övergången mellan sommartid och vintertid följs. Ett klockslag i timmen som hoppas över flyttas till första tiden som finns, och ett klockslag i timmen som upprepas räknas första gången.

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
| `challenge_accounts` | Challenge-konton med regelmotorns tillstånd, status, fas, handelsdag, om challengen är pausad och firmans referens. |
| `challenge_steps` | Varje indata och beslut i ordning, med handelsplattformens händelse som bevis. |
| `trading_accounts` | Konton på handelsplattformen per fas, med saldo, öppna positioner och golvens nivåer som plattformen senast rapporterade. |
| `trading_cursors` | Hur långt varje firmas händelseström är läst. |
| `trading_commands` | Kommandon till handelsplattformen, i ordning per firma. |
| `webhook_deliveries` | Webhooks till firman, med försök och svar. |
| `payouts` | Utbetalningar med status, vinst, vinstandel, belopp, tider, orsak och firmans referens. Regelmotorns steg är revisionsloggen, tabellen är för att hitta utbetalningar. |
| `firm_counters` | Nästa kontonummer per firma. |
| `firms`, `firm_hosts` | Firmorna och värdnamnen deras portaler nås på. Se [specen för registrering och sandlåda](registrering.md). |
| `firm_signups`, `admin_invites`, `admin_login_links` | Registreringar som väntar på bekräftelse, inbjudningar till administratörer och engångslänkar som loggar in en administratör. |
| `firm_billing`, `billing_periods`, `billing_charges`, `billing_checkouts`, `billing_events` | Hur firman betalar oss och dess kort, betalda månader med platser, debiteringar, betalsidor och allt som hänt. Se [specen för platser och betalning](platser-och-betalning.md). |
| `challenge_prices` | Vad challengerna kostar i portalen och om de säljs där. |
| `orders`, `order_events`, `order_counters` | Köp i portalen med status, pris, leverantör, betalning och kontot de startade, allt som hänt varje order och nästa ordernummer per firma. Firmans val av leverantör, krypterade Stripe-nycklar, betalsida och villkor ligger i `firms`. Se [specen för köp i portalen](kop.md). |

## Firmans API

Alla vägar börjar med `/api/firm/v1` och kräver firmans nyckel i headern `X-Api-Key`. Andra firmors data svarar 404.

| Metod och väg | Beskrivning |
|---|---|
| `PUT /challenges/{challengeId}` | Skapar eller ersätter en challenge. Den ska vara i valutan för firmans konton på handelsplattformen. Konton som redan har startat behåller sina regler. |
| `GET /challenges` | Firmans challenges. |
| `POST /accounts` | Startar en challenge med `{ "email", "challengeId", "reference" }`. Samma `reference` igen ger samma konto. Svarar 201, eller 200 för ett konto som redan finns. 409 med orsaken när ingen plats är ledig, när en firma i sandlådan redan har `Sandbox:MaxOpenAccounts` öppna konton eller när firmans månad är obetald. |
| `GET /slots` | Firmans platser: gräns, tagna, reserverade av ordrar, lediga, om månaden är betald och om de flesta är tagna. Se [specen för platser och betalning](platser-och-betalning.md). |
| `GET /accounts?email=` | Traderns konton. |
| `GET /accounts/{id}` | Status, fas, konto på handelsplattformen, startsaldo och valuta, handelsdagar, vinstmål, saldo, golvens nivåer, om challengen är pausad, och handelsdagen då fasen tar slut på tid (`stageDeadline`) och då challengen tar slut utan en ny position (`inactivityDeadline`). |
| `GET /accounts/{id}/history` | Varje indata och beslut, med bevisen vid brott. |
| `POST /accounts/{id}/approve-funding` | Firman har gjort sina kontroller, och tradern får funded-kontot. 409 innan faserna är klara. |
| `POST /accounts/{id}/cancel` | Avbryter med `{ "reason" }` och stänger kontot på handelsplattformen. |
| `POST /accounts/{id}/login-link` | En engångslänk som loggar in tradern i terminalen på fasens konto. |
| `POST /accounts/{id}/invite` | En inbjudningslänk till portalen, för firman att skicka till tradern. Gäller en gång i 7 dagar och ersätter traderns äldre oanvända. |
| `POST /accounts/{id}/payouts` | Begär en utbetalning åt tradern, för firmor vars egen webbplats låter tradern begära. Svarar 201 med utbetalningen, eller 409 med orsaken när den inte kan begäras nu. |
| `GET /accounts/{id}/payouts` | Kontots utbetalningar, nyaste först. |
| `GET /payouts?status=&limit=` | Firmans nyaste utbetalningar, högst 500. `status` kan anges flera gånger, till exempel `status=Pending&status=Approved` för de som väntar på firman. |
| `GET /payouts/{payoutId}` | En utbetalning. |
| `POST /payouts/{payoutId}/approve` | Firman har gjort sina kontroller, till exempel KYC, och skickar pengarna. 409 om utbetalningen inte väntar på godkännande. |
| `POST /payouts/{payoutId}/mark-paid` | Firman har betalat, med en valfri egen `{ "reference" }`. 409 om utbetalningen inte är godkänd. |
| `POST /payouts/{payoutId}/reject` | Nekar en utbetalning som väntar eller är godkänd, med `{ "reason" }` som visas för tradern. Vinsten återförs inte till kontot. |
| `GET /prices`, `PUT /challenges/{challengeId}/price` | Priserna i portalen, och ett pris med `{ "amount", "currency", "forSale" }`. |
| `GET /orders?status=&limit=`, `GET /orders/{orderId}` | Firmans ordrar från portalen, och en order med allt som hänt den. |
| `POST /orders/{orderId}/mark-paid`, `POST /orders/{orderId}/mark-refunded` | Firmans egen betalsida har fått betalt, och kontot startar, eller har betalat tillbaka. Se [specen för köp i portalen](kop.md). |

`AccountResponse` har `nextPayout` för funded-konton: vinsten, vinstandelen, traderns belopp, handelsdagar sedan förra utbetalningen och om en utbetalning kan begäras nu, annars varför inte. Se [specen för regelmotorn](regelmotor.md).

Tjänsten publicerar OpenAPI på `/openapi/v1.json`. Dokumentet skrivs till `prop/portal/openapi/prop-api.json` när tjänsten byggs, och CI kontrollerar att det är committat.

## Webhooks

| Händelse | När |
|---|---|
| `account.stage_started` | En fas har fått sitt konto. |
| `account.passed` | En fas är klar. |
| `account.funding_awaited` | Alla faser är klara, och firman ska godkänna funded-kontot. |
| `account.breached` | Ett golv bröts. Innehåller orsaken, nivån och equity. |
| `account.cancelled` | Kontot avbröts. |
| `account.expired` | Challengen tog slut på tid: `data.reason` är `TimeLimit` eller `Inactivity`, och `data.day` handelsdagen då den tog slut. |
| `account.paused`, `account.resumed` | Challengen pausades eftersom firmans månad är obetald, eller fortsätter. `data.daysPaused` är dagarna som tidsgränserna flyttades. |
| `payout.requested` | Tradern har begärt en utbetalning och vinsten är uttagen från kontot. Firman ska godkänna. |
| `payout.approved` | Firman godkände utbetalningen. |
| `payout.paid` | Firman markerade utbetalningen som betald. |
| `payout.rejected` | Firman nekade utbetalningen. Innehåller orsaken. |
| `order.paid`, `order.refunded`, `order.disputed` | En order i portalen är betald, återbetald eller bestridd. `data.order` är ordern och `account` kontot den startade. |

- Kroppen är `{ "id", "type", "createdAt", "account": { "id", "number", "email", "challengeId", "reference" }, "data": { ... } }`, där `data` är regelmotorns beslut. För utbetalningar har `data.payout` utbetalningens id, vinst, vinstandel, belopp och status.
- Headrarna `Prop-Webhook-Id` och `Prop-Webhook-Event` anger vilken leverans och händelse det är. `Prop-Signature` är `t={unix-sekunder},v1={hex}`, där `hex` är HMAC-SHA256 med firmans hemlighet av `{t}.{kropp}`.
- Svar 2xx räknas som mottaget. Annars görs ett nytt försök efter 30 sekunder, med dubbel väntan varje gång och högst 6 timmar, i 16 försök (ungefär ett och ett halvt dygn). Samma leverans kan komma mer än en gång och känns igen på sitt id.

## Konfiguration

| Sektion | Innehåll |
|---|---|
| `ConnectionStrings:Prop` | Propfirm-plattformens databas. Lokalt Postgres från `deploy/docker-compose.yml`. |
| `TradingPlatform` | `Url` till handelstjänsten (slutar med `/`), hur länge ett anrop väntar på nya händelser och hur många som läses åt gången. |
| `Firms` | Firmor som sparas i databasen vid varje start och är live, för utveckling och tester: `Id`, `Name`, `ApiKeySha256`, `Trading` (`Server`, `ApiKey`, `Group`, `Currency` med standard `USD`), `Webhook` (`Url`, `Secret` på minst 32 tecken), `Portal` (se [specen för portalen](portal.md)), `SeedChallenges`, `SeedAdmins` och `SeedTraders`. |
| `Firms:N:SeedChallenges` | Challenges som skapas eller ersätts vid varje start, med `Template`, `Id`, `InitialBalance` och `Currency`. Mallen `TwoStep` är standardmallen. `QuickTest` är samma mall med 0,1 % vinstmål och utan minsta antal handelsdagar, så att hela vägen till en utbetalning kan provas på några minuter. Den är bara för utveckling. Konton som redan har startat behåller sina regler. |
| `TradingPlatform:PartnerApiKey`, `Platform`, `Signup`, `Sandbox`, `Email`, `Secrets` | Registreringen och sandlådan. Se [specen för registrering och sandlåda](registrering.md). |
| `Platform:ApiUrl`, `Payments`, `Firms:N:Payments`, `Firms:N:SeedChallenges:M:Price` | Köp i portalen. Se [specen för köp i portalen](kop.md). |
| `Billing`, `Firms:N:Slots` | Platser och vad firman betalar oss. Se [specen för platser och betalning](platser-och-betalning.md). |
| `Platform:OpsUrl`, `Billing:ReviewDeposit`, `Staff` | Vår granskning och vår adminvy. Se [specen för granskning och avstängning](granskning.md). |
| `Login` | Regler för lösenord och inloggning: `MinimumPasswordLength` (standard 10), `AttemptsPerMinute` per IP-adress (standard 10, 0 för ingen gräns) och `SessionLifetime`, hur länge en oanvänd session gäller (standard 12 timmar). I utveckling är reglerna avstängda och sessionen gäller i 30 dagar. |

I utveckling registrerar sig firmor på http://app.localhost:3002/signup utan bekräftelse av e-postadressen, får sin portal på till exempel http://acme.localhost:3002, skickar sin ansökan med testbetalning på http://acme.localhost:3002/admin/verification, godkänns av `ops@test.com` med lösenordet `ops` på vår adminvy http://ops.localhost:3002 och går live med testbetalning på http://acme.localhost:3002/admin/billing. Mejl hamnar i Mailpit på http://localhost:8025. Firman `demo-firm` har nyckeln `dev-prop-key` och ingen gräns för platser. Den använder servern `demo-firm` på handelsplattformen med nyckeln `dev-admin-key`, har challengerna `two-step-100k` och `quick-test-100k`, som säljs för 499 och 9 USD med testbetalningar på http://localhost:3002/buy, portalen på http://localhost:3002, administratören `admin@test.com` med lösenordet `admin` och traderna `anna@test.com` med `anna` och `test@test.com` med `test`. Allt detta gäller bara lokal utveckling.

## Begränsningar

- Tjänsten startar bara i miljön Development. Före produktion behövs HTTPS och hantering av hemligheter, till exempel för handelsplattformens nyckel och webhook-hemligheten.
- Tjänsten körs som en instans. Läsningen av händelseströmmen och kommandona har ännu inga lås mellan instanser, och firmorna hålls i minnet.
- En firma går live först när vi har granskat och godkänt den. Den kan inte byta namn eller kort namn, eller tas bort än.
- Ett kommando som handelsplattformen avvisar läggs åt sidan som misslyckat och syns bara i databasen och loggen. Ett nekat uttag gör dessutom utbetalningen `Failed`.
- Utbetalningar som firman har godkänt betalas av firman själv. Tjänsten hanterar aldrig pengar.
- Köp i portalen betalas till firmans egen leverantör. Tjänsten ser bara att betalningen är gjord.

## Tester

Testerna ligger i `prop/tests/Prop.Api.Tests`. De kör tjänsten mot riktig Postgres i en container via Testcontainers och kräver Docker. Handelsplattformen är en låtsad version i minnet som beter sig som vår. Klockan flyttas bara när testet säger till, och firmans webhooks tas emot av testet.

- `FirmApiTests`: nyckeln, challenges och deras kontroll, start av konto med golven i rätt ordning, firmans referens, felaktiga starter, att firmor inte ser varandras konton, inloggningslänken och inbjudan till portalen.
- `PortalApiTests`: portalens API, se [specen för portalen](portal.md).
- `PayoutFlowTests`: en utbetalning från begäran via uttaget på handelsplattformen till godkänd och betald, med webhooks, orsaker att neka, ett nekat uttag som gör utbetalningen `Failed` och ett nytt försök som lyckas, nej från firman, traderns begäran och administratörens beslut i portalen, att andra firmor inte når utbetalningarna och gränserna för listor.
- `SignupTests`: registrering med och utan bekräftelse av e-postadressen, länkens livslängd, korta namn som är ogiltiga, reserverade eller tagna, också på handelsplattformen, kontroll av formuläret, att registreringen bara finns på plattformens adress, ett mejl som inte gick iväg, en server som skapas när handelsplattformen är tillbaka och ett svar som gick förlorat, välkomstlänken, inloggning med lösenordet från registreringen, sandlådans gräns, omstart och att en konfigurerad firma inte kan ta över en registrerad.
- `AdminSettingsTests`: logga och färger, nyckel för firmans API, webhooks som signeras med firmans hemlighet och en ny hemlighet, inbjudningar till administratörer, en borttagen administratör som loggas ut direkt, att administratörer bara når sin egen firma, challenges från mallen och i firmans valuta, och att inställningarna bara är för administratörer.
- `OrderFlowTests`: köp i portalen med testbetalning, Stripe och firmans egen betalsida. Se [specen för köp i portalen](kop.md).
- `SmtpEmailSenderTests`: ett riktigt mejl genom SMTP till Mailpit i en container, och att en mejlserver som inte svarar ger ett fel som går att hantera.
- `SlotTests`, `BillingFlowTests`, `StripeBillingTests` och `BillingRulesTests`: platserna och betalningen. Se [specen för platser och betalning](platser-och-betalning.md).
- `ReviewTests` och `SuspensionTests`: vår granskning, vår adminvy och avstängning. Se [specen för granskning och avstängning](granskning.md).
- `ExpiryTests`: en challenge utan ny position i 30 dagar som tar slut och stänger kontot med webhooken och orsaken, en affär på sista dagen som räknas fast händelsen kommer efter att dagen tagit slut, en ny position som flyttar sista dagen, en fas med tidsgräns som tar slut, och en tidsgräns som är kortare än fasens handelsdagar.
- `ChallengeFlowTests`: dagliga golvet vid midnatt i Stockholm, en klarad fas som stänger kontot och öppnar nästa, brott med bevis, godkänd finansiering, annullering, avbrott i handelsplattformen där kommandona behåller sin ordning, omstart där varje händelse ändå hanteras exakt en gång, och signerade webhooks som skickas igen.
- `TradingPlatformClientTests`: klienten mot svar som handelsplattformens, att regelmotorns golv blir plattformens regler, att ett konto värderas med sina golv, att ett uttag bara dras en gång och bär plattformens orsak vid nej, och att konton pausas och återupptas.
- `TradingContractTests`: varje väg och fält som klienten använder finns i `contracts/trading/trading-service.json`.
- `TradingDaysTests`: handelsdagar i olika tidszoner, vid klockslag på kvällen och vid sommartid och vintertid.
