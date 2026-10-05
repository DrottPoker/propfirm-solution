# Spec: ID-kontroll av traders

- Fas: ID-kontroll med Didit eller firmans egen tjänst, efter supportärendena
- Status: Implementerad i `prop/src/Prop.Api/Identity` och `prop/portal`
- Datum: 2026-10-05

## Syfte

En firma måste veta vem den betalar ut till. Tradern ska kunna visa sitt ID på ett sätt som känns seriöst, utan att skicka passbilder i ett ärende eller ett mejl, och varken vi eller firman ska behöva spara bilderna. Firman väljer själv: vår inbyggda kontroll genom Didit eller sin egen tjänst. Inget är förvalt. Firman kan skicka sin ansökan och bli godkänd utan att ha valt, men måste ha valt, och fått sin egen tjänst att fungera hela vägen, innan den går live. Att bocka för "ID checked" för hand finns kvar som ett undantag. Besluten finns i [ADR 0042](../adr/0042-id-kontroll-med-en-extern-tjanst.md).

## Firmans val

I adminpanelen och i vår adminvy heter det KYC (know your customer). Traderna ser "Verify your identity" och sin ID-kontroll, utan ordet KYC.

Under KYC i adminpanelen står kort varför KYC är viktigt: firman vet vem den betalar, det stoppar en person med många konton eller från ett land firman inte tar emot, och banker och betalningsleverantörer frågar ofta hur firman kontrollerar dem den betalar. Där väljer firman ett av två sätt. Inget är förvalt, så firman tar ställning själv, och Save går först när ett är valt:

| Sätt | Vad det betyder |
|---|---|
| `BuiltIn`, "Our built-in KYC" | Tradern gör kontrollen hos Didit på mobilen eller datorn: ID-handlingen, att personen är levande framför kameran och att ansiktet stämmer. Ett godkännande bockar för "ID checked". Firman kan också slå på adresskontroll, som bockar för "Address checked", och kontroll mot sanktions- och PEP-listor. Kostar enligt priserna nedan. |
| `External`, "Your own KYC service" | Tradern skickas till firmans egen sida, till exempel `https://kyc.firma.se/start?trader={traderId}&email={email}`, där `{traderId}` och `{email}` fylls i. Firmans system berättar resultatet genom firmans API. |

Firman väljer också vad som väntar på kontrollen:

| Krav | Vad som väntar |
|---|---|
| `FirstPayout`, "Before the first payout" (standard) | Tradern kan inte begära en utbetalning i portalen. |
| `Funding`, "Before the funded account" | Firman kan inte godkänna funded-kontot, varken i adminpanelen eller genom firmans API. |

En trader räknas som kontrollerad när en kontroll godkänts, eller när firman har bockat för "ID checked" för hand. Bocken är ett undantag för en trader firman kontrollerat på annat sätt, och släpper alltid igenom tradern.

Tidigare kunde firman välja att kontrollera för hand. Det sättet finns inte längre, och en firma som hade valt det har inget val och väljer igen (migreringen `0023_identity_choice.sql`).

## Innan firman går live

En firma som inte har valt har inget som väntar på en kontroll, och dess traders kan inte starta någon. Firmans ID-kontroll är i ett av tre lägen, `IdentityReadiness`:

| Läge | Vad det betyder |
|---|---|
| `NotChosen` | Firman har inte valt hur traders kontrolleras. |
| `NotTested` | Firman har valt sin egen tjänst, men hela flödet har inte fungerat sedan adressen sparades. |
| `Ready` | Vår inbyggda kontroll, eller firmans egen tjänst när hela flödet har fungerat. |

Utan `Ready` kan firman inte betala för att gå live (`goLiveProblem`, och 409 från betalningen). Ansökan och vårt godkännande väntar inte på KYC, så firman kan välja medan vi granskar eller efter att vi godkänt den. En firma som redan är live påverkas inte, men KYC säger till om den inte valt eller om en ny adress inte har fungerat.

Hela flödet har fungerat när en trader har startat kontrollen i portalen och skickats till firmans adress, och firmans tjänst sedan har rapporterat `Approved` eller `Declined` för den tradern genom firmans API. `Pending` och `InReview` räknas inte, och inte heller ett beslut om en trader som aldrig skickades till sidan eller skickades dit innan adressen ändrades. Tiden sparas, och visas för firman under KYC och för oss i granskningen. Att spara samma adress behåller den, och en ny adress måste fungera igen. I sandlådan prövar firman det med en challenge till sig själv: tradern klickar på "Verify with {firma}" under Payouts, och firmans tjänst rapporterar resultatet.

## Flöde med den inbyggda kontrollen

1. Tradern ser "Verify your identity" på startsidan när ett funded-konto är nära (ett konto väntar på funded eller är funded), under Payouts och på sidan `/identity`.
2. Knappen skapar en kontroll hos Didit med arbetsflödet för firmans val och skickar tradern till Didits sida. En kontroll som startats de senaste 30 minuterna och inte skickats in öppnas igen i stället för att en ny startas.
3. När tradern är klar skickar Didit tillbaka till `/identity?returned=1`, som visar att resultatet väntas och frågar igen av sig självt.
4. Didit meddelar oss med en signerad webhook. Vi läser beslutet från Didits API och sparar bara resultatet: status, namn, födelsedatum och land från handlingen, om adressen och listorna kontrollerades, och orsaken vid ett nej.
5. Ett godkännande bockar för "ID checked", och "Address checked" när adressen kontrollerades, med Didit som den som bockade. Tradern får ett mejl, och firmans webhook `trader.identity_verified`. Ett nej mejlar tradern orsaken, skickar `trader.identity_declined`, och tradern kan försöka igen.
6. En kontroll som granskas av en person hos Didit visas som "Being reviewed". En kontroll som gavs upp eller gick ut kan startas igen.

Om Didits webhook inte kommer fram frågar portalen Didit själv, högst var tionde sekund, medan tradern tittar på sin kontroll.

**Sandlådan och utveckling:** en firma i sandlådan, och alla firmor när `Identity:Provider` är `Test`, får en testkontroll i stället. Den öppnar portalens sida `/identity/test?session=`, där tradern väljer Approve eller Decline. Ingen handling kontrolleras och inget kostar. Ett godkännande ger namnet från köpet, eller "Test Trader".

## Flöde med firmans egen tjänst

1. Tradern klickar på "Verify with {firma}" och skickas till firmans adress med sitt id och sin e-post. Vi sparar att tradern skickades dit, som en kontroll med leverantören `External`.
2. Firmans system anropar `PUT /api/firm/v1/traders/identity` med traderns e-post och resultatet. Ett godkännande bockar för "ID checked", och "Address checked" med `addressChecked`, med "Your KYC service" som den som bockade.
3. Ett beslut avgör traderns senaste kontroll hos firmans sida. Startades den efter att adressen sparades har hela flödet fungerat.
4. Vi mejlar inte tradern och skickar ingen webhook, eftersom firmans egen tjänst redan vet.

## Sidor

| Sida | För | Innehåll |
|---|---|---|
| `/identity?returned=` | Traders | Traderns kontroll: vad den betyder, var den är, och knappen som startar, fortsätter eller försöker igen. Efter Didit visar sidan att resultatet väntas och frågar igen av sig själv. |
| `/identity/test?session=` | Traders | Testkontrollen: Approve, Decline eller Cancel. |
| `/payouts` | Traders | Kontrollen visas under utbetalningsmetoden när firman har valt hur traders kontrolleras. Väntar utbetalningen på kontrollen säger panelen det, med en länk till `/identity`, och knappen går inte att trycka på. |
| `/` | Traders | En rad om att verifiera sig när ett funded-konto är nära och kontrollen inte är klar. |
| `/admin/identity` | Administratörer | Firmans val, där inget är förvalt och firman får veta att det behövs innan den går live, priserna för den inbyggda kontrollen, adress och listor, kraven, firmans egen adress och ett exempel på anropet till firmans API, och hur många kontroller som gjorts sedan förra debiteringen. Säger när kontrollerna är testkontroller. För firmans egen tjänst: när hela flödet fungerade, eller hur det prövas, och sidan frågar igen var tionde sekund medan det väntar. I sandlådan en knapp till Go live. |
| `/admin`, `/admin/go-live` | Administratörer | Stegen till live har steget "Set up KYC". Steget med platser och betalning har KYC bland det att kontrollera innan betalningen, med en länk till KYC, och knappen som betalar går inte att trycka på förrän KYC är klar. Innan vi godkänt firman säger steget att KYC behövs före live. |
| `/ops/firms/{id}` | Vår personal | Granskningen visar firmans ID-kontroll: sättet, firmans adress och när hela flödet fungerade. Godkännandet säger till när den inte är klar, men väntar inte på den. |
| `/admin/accounts/{id}` | Administratörer | Traderns kort visar ID-kontrollen: status, namnet, födelsedatumet och landet från handlingen, om tradern finns på sanktionslistorna, vem som kontrollerade och när, eller orsaken vid ett nej. |
| `/admin/payouts` | Administratörer | En utbetalning varnar när namnet på traderns ID inte är kontoinnehavaren i traderns utbetalningsmetod. Stora och små bokstäver, accenter, ordningen och ett mellannamn på bara ena sidan räknas inte. |

## API

| Metod och väg | Roll | Beskrivning |
|---|---|---|
| `GET /api/portal/identity` | Trader | `mode` (`null` när firman inte valt, och då väntar inget), `requiredBefore`, `status` (`NotStarted`, `Pending`, `InReview`, `Approved`, `Declined` eller `Expired`), `verified` (också när firman bockat för hand), `canStart`, `reason` vid ett nej och `decidedAt`. Frågar Didit om en kontroll som väntar, högst var tionde sekund. |
| `POST /api/portal/identity/start` | Trader | `{ "url" }`: Didits sida, testsidan eller firmans egen. 409 när firman inte valt, när tradern redan är kontrollerad eller när kontrollen granskas, 503 när Didit inte svarar eller inte är inställt. |
| `POST /api/portal/identity/test/{sessionId}` | Trader | `{ "approve" }`. Testkontrollens utfall. 404 för en annan traders kontroll eller en riktig. |
| `GET /api/portal/admin/identity` | Admin | Firmans val, med `mode` `null` när den inte valt, `prices` (valuta, pris per månad, kontroller som ingår, per kontroll, adress och listor), `checksSinceLastCharge`, `testChecks`, `readiness` och `externalTestedAt`, när firmans egen tjänst fungerade hela vägen. |
| `PUT /api/portal/admin/identity` | Admin | `{ "mode", "requiredBefore", "checkAddress", "checkSanctions", "externalUrl" }`. 422 med `field: "mode"` utan ett val, och med `field: "externalUrl"` för `External` utan en hel https-adress (http bara till den egna datorn). |
| `GET /api/firm/v1/traders/identity?email=` | Firmans API | Traderns kontroll, från vilken tjänst som helst. 404 utan trader eller kontroll. |
| `PUT /api/firm/v1/traders/identity` | Firmans API | `{ "email", "status", "reason", "fullName", "dateOfBirth", "country", "addressChecked" }`, där `status` är `Pending`, `InReview`, `Approved` eller `Declined`. `Approved` eller `Declined` för en trader som skickades till firmans sida visar att hela flödet fungerar. 409 när firman inte valt sin egen tjänst, 404 för en okänd e-post, 422 för en annan status eller för långa texter. |
| `POST /api/identity/v1/didit` | Didit | Didits webhook. `X-Signature` måste vara HMAC-SHA256 i hex av kroppen med `Identity:Didit:WebhookSecret`, annars 401. Beslutet läses alltid från Didits API. 503 när Didits API inte svarar, så att Didit skickar igen. En kontroll som inte är vår besvaras med 200. |

`GET /api/portal/admin/verification` har `identity`, firmans läge, och betalningen för att gå live (`POST /api/portal/admin/billing/activate`) svarar 409 utan `Ready`, med orsaken i `goLiveProblem` från `GET /api/portal/admin/billing`. `GET /api/portal/ops/firms/{id}` har `identity` med sättet, adressen, `externalTestedAt` och `readiness`.

`POST /api/portal/accounts/{id}/payouts` svarar 409 "Verify your identity first, under Payouts." och `approve-funding` i adminpanelen och firmans API 409 när kravet inte är uppfyllt.

## Priser och debitering

Den inbyggda kontrollen kostar 15 USD i månaden, med 25 kontroller som ingår. Varje månad debiteras som en egen debitering, `IdentityChecks`, när nästa månad debiteras (5 dagar innan den börjar), bara för firmor som är live och betalar. En månad räknas från en sådan debitering till nästa:

- `Billing:IdentityChecks:MonthlyPrice` (15 USD) när firman gjorde kontroller i månaden, eller hade kontrollen påslagen hela månaden medan den var live. Att stänga av kontrollen innan månaden debiteras gör alltså inte kontrollerna i den gratis. En månad firman slår på kontrollen utan att göra någon kontroll kostar inget.
- `Included` (25) kontroller ingår i månadens pris. Fler kontroller som skickats in hos Didit kostar `PerCheck` (0,80 USD) styck. Kontroller som inte används sparas inte till nästa månad.
- Adresskontroll kostar `Address` (0,30 USD) och listorna `Sanctions` (0,30 USD) per kontroll, och ingår aldrig.
- Testkontroller och kontroller som aldrig skickades in kostar inget.
- En obetald debitering för ID-kontroller pausar inte firmans challenges.

Priserna är förslag och utan moms, som våra andra priser.

## Tabeller

| Tabell | Innehåll |
|---|---|
| `firm_identity_settings` | Firmans val: sätt, krav, adress, listor, firmans adress, sedan när den inbyggda kontrollen är påslagen, sedan när firmans adress gäller och när hela flödet fungerade med den, när och av vem. En firma utan rad har inte valt. |
| `identity_sessions` | Varje kontroll hos en leverantör: trader, leverantör (`Didit`, `Test`, eller `External` när portalen skickade tradern till firmans sida), leverantörens id, sidan, adress och listor, status, när den skapades, senast frågades om, beslutades och skickades in, och debiteringen som tog betalt för den. |
| `trader_identity` | Varje traders kontroll som den står: leverantör (`Didit`, `Test` eller `External`), senaste kontrollen, status, namn, födelsedatum, land, adress och listor, orsak och när den beslutades. |

## Konfiguration

| Inställning | Innehåll |
|---|---|
| `Identity:Provider` | `Didit` (standard) eller `Test`, bara i utveckling. |
| `Identity:Didit:ApiUrl` | Didits API, standard https://verification.didit.me/. |
| `Identity:Didit:ApiKey`, `WebhookSecret` | Vår nyckel hos Didit och hemligheten som signerar Didits webhooks. Hemligheter. Webhooken ska peka på `{Platform:ApiUrl}api/identity/v1/didit`. |
| `Identity:Didit:Workflow`, `WorkflowWithAddress`, `WorkflowWithSanctions`, `WorkflowWithAddressAndSanctions` | Arbetsflödena hos Didit för varje val. Alla har ID-handling, liveness och ansiktsjämförelse. |
| `Billing:IdentityChecks` | Priserna ovan. |

Utanför utveckling startar tjänsten inte utan Didits nyckel, hemlighet och första arbetsflöde. I utveckling är `Identity:Provider` `Test`. Med `Didit` utan nycklar säger startknappen att kontrollerna inte är inställda än.

## Begränsningar

- Didits webhook signeras över kroppen. Den gamla tidsstämpeln kontrolleras inte, men en upprepad webhook gör ingen skada, eftersom beslutet alltid läses från Didits API.
- Firmans utseende på Didits sida är inte inställt per firma än. Det behöver bekräftas med Didit.
- Bilderna sparas hos Didit så länge som ställs in i Didits konsol. Vi sparar dem aldrig.
- Firmans egen tjänst får inget mejl eller webhook från oss om att tradern klickat. Firman ser det på sin egen sida.
- Att hela flödet fungerat visar att firmans tjänst rapporterar ett beslut om en trader som portalen skickade dit. Vi kontrollerar inte själva att firmans sida svarar.

## Tester

- `prop/tests/Prop.Api.Tests/IdentityTests`: testkontrollen godkänd och nekad med mejl, webhook och bockade kontroller, att samma kontroll öppnas igen, att en trader inte kan avgöra en annans; utbetalningen och funded-kontot som väntar och att en bock för hand släpper igenom; Didit med rätt arbetsflöde, webhooks med och utan rätt signatur, granskning, godkännande med namn, födelsedatum, land och listor, en okänd kontroll, Didit som inte svarar och att portalen frågar Didit själv; firmans egen tjänst med adressen och firmans API, och att hela flödet fungerar först när en trader som skickades dit får ett beslut och igen efter en ny adress; att inget är förvalt och inget kan startas förrän firman valt; och debiteringen: månadens pris och listorna också när kontrollen stängts av innan månaden debiterats, en gång, ingenting för en månad den slogs på utan kontroller och månadens pris för en hel månad påslagen.
- `prop/tests/Prop.Api.Tests/DiditDecisionTests`: Didits statusar, besluten med namn, födelsedatum, land, adress och listor, orsaken vid ett nej och signaturen.
- `prop/tests/Prop.Api.Tests/BillingRulesTests`: raderna för ID-kontroller.
- `prop/tests/Prop.Api.Tests/ReviewTests`: att ansökan skickas och godkänns utan KYC, och att betalningen för att gå live väntar på ett val och på att firmans egen tjänst fungerat, också efter en ny adress.
- `prop/portal/src/lib/identity.test.ts`: texterna för tradern, när startsidan frågar, när utbetalningen väntar, vad som behövs innan firman går live, statusen, namnen som jämförs, landet och födelsedatumet.
- `prop/portal/src/lib/goLive.test.ts`: steget för KYC efter granskningen bland stegen till live, och att live väntar på det.
- `prop/portal/e2e/identity.spec.ts`: en ny firma har inget förvalt och kan inte spara utan ett val, väljer den inbyggda kontrollen, en trader verifierar sig med testkontrollen från Payouts och firman ser resultatet på traderns kort.
- `prop/portal/e2e/verification.spec.ts`: en godkänd firma kan inte betala för att gå live förrän den har valt KYC.
