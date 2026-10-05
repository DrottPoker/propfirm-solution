# propfirm-solution

Två produkter för små och nystartade propfirms, som kan säljas var för sig eller tillsammans:

1. **Kronant Trader**, en handelsplattform för simulerad handel med riktiga livepriser.
2. **Kronant Prop**, en propfirm-plattform med challenges, regelmotor, traderportal, adminpanel och utbetalningsflöde.

Bolaget bakom heter Ludware. Se [ADR 0046](docs/adr/0046-namn-pa-bolaget-och-produkterna.md) för namnen.

Se [produktplanen](docs/produktplan-handelsplattform-propfirm.md), [arkitekturbesluten](docs/adr/README.md) och specarna för [handelsmotorn](docs/spec/handelsmotor.md), [handelstjänsten](docs/spec/handelstjanst.md), [handelsterminalen](docs/spec/handelsterminal.md), [regelmotorn](docs/spec/regelmotor.md), [propfirm-tjänsten](docs/spec/propfirm-tjanst.md), [portalen](docs/spec/portal.md), [registreringen](docs/spec/registrering.md), [platserna och betalningen](docs/spec/platser-och-betalning.md), [köp i portalen](docs/spec/kop.md) och [granskningen av firmor](docs/spec/granskning.md).

## Struktur

```
propfirm-solution/
├── docs/                 # produktplan, arkitekturbeslut (ADR) och specar
├── contracts/            # kontraktet mellan produkterna, det enda de delar: handelstjänstens OpenAPI
├── trading/              # Produkt 1: handelsplattformen
│   ├── src/
│   │   ├── Trading.Engine/    # deterministisk kärna, ingen I/O
│   │   └── Trading.Service/   # motorloop, journal i Postgres, prisflöde, candles, REST och SignalR
│   ├── tests/
│   │   ├── Trading.Engine.Tests/   # beteenden, uppspelning mot facit, arkitektur
│   │   └── Trading.Service.Tests/  # tjänsten i minnet med styrda priser och klocka
│   └── terminal/         # webbgränssnittet för traders (Next.js), med genererade API-typer
├── prop/                 # Produkt 2: propfirm-plattformen
│   ├── src/
│   │   ├── Prop.Rules/        # regelmotorn: deterministisk, ingen I/O
│   │   └── Prop.Api/          # firmans och portalens API, regelmotorn mot handelsplattformen, utkorg och webhooks
│   ├── tests/
│   │   ├── Prop.Rules.Tests/  # regler, livscykel, uppspelning mot facit, arkitektur
│   │   └── Prop.Api.Tests/    # tjänsten mot Postgres med en låtsad handelsplattform och styrd klocka
│   └── portal/           # vitmärkt traderportal och adminpanel (Next.js), med genererade API-typer
├── shared/               # generell kod utan affärslogik, till exempel migreringar för Postgres
└── deploy/               # docker compose för lokal utveckling
```

## Produktgränser

En produkt får bara använda kod från sin egen mapp, `contracts/` och `shared/`. Det kontrolleras automatiskt: .NET-bygget stoppar med `BOUNDARY001`, och ESLint stoppar import mellan webbapparna. Varje produkt har också egen databas. Se [ADR 0001](docs/adr/0001-monorepo-med-produktgranser.md).

## Förutsättningar

- [.NET SDK 10](https://dotnet.microsoft.com/download) (versionen styrs av `global.json`)
- [Node.js 22](https://nodejs.org/) eller senare
- [pnpm](https://pnpm.io/installation) (versionen styrs av `packageManager` i `package.json`)
- [Docker](https://www.docker.com/) för Postgres, NATS och Mailpit, och för testerna mot Postgres

## Kom igång

Starta Postgres, NATS och Mailpit:

```bash
docker compose -f deploy/docker-compose.yml up -d
```

Bygg och testa backend:

```bash
dotnet build PropfirmSolution.slnx
```

```bash
dotnet test
```

Installera och kontrollera webben:

```bash
pnpm install
```

```bash
pnpm lint && pnpm typecheck && pnpm build
```

Kör tjänsterna lokalt:

| Del | Kommando | Adress |
|---|---|---|
| Handelsplattformens tjänst | `dotnet run --project trading/src/Trading.Service` | http://localhost:5101/health |
| Propfirm-plattformens API | `dotnet run --project prop/src/Prop.Api` | http://localhost:5201/health |
| Handelsterminalen | `pnpm dev:terminal` | http://localhost:3001 |
| Portalen | `pnpm dev:portal` | http://localhost:3002 |
| Postgres | | localhost:5432, databaserna `trading` och `prop` |
| NATS | | localhost:4222, övervakning på http://localhost:8222 |
| Mailpit | | SMTP på localhost:1025. Mejlen från plattformen läses på http://localhost:8025 |

Lösenorden i `deploy/` gäller bara lokal utveckling.

I Claude Code-appen kan handelsplattformen och propfirm-plattformen startas i förhandsvisningen med konfigurationerna i `.claude/launch.json`. Starta Postgres med docker compose först.

| Konfiguration | Startar |
|---|---|
| `trading-service` | Handelstjänsten med prisflödet från dina user secrets (se Riktiga priser från Tiingo) |
| `trading-service-synthetic` | Handelstjänsten med syntetiska priser, till exempel när valutamarknaden är stängd |
| `trading-terminal` | Handelsterminalen |
| `prop-api` | Propfirm-tjänsten. Behöver handelstjänsten. |
| `prop-portal` | Portalen. Behöver propfirm-tjänsten. |

## Prova handelstjänsten

Tjänsten sparar allt i Postgres, så starta databasen först (se Kom igång). Den startar med syntetiska priser och kontot `demo` (100 000 USD). Den är inte klar för produktion och startar därför bara i miljön Development.

Konton, positioner och historik finns kvar efter en omstart. Stäng av med Ctrl+C, så sparas en ögonblicksbild och nästa start går snabbt. Börja om från noll med `docker compose -f deploy/docker-compose.yml down -v`.

```bash
dotnet run --project trading/src/Trading.Service
```

Lokalt finns firman `demo-firm` (servern Demo Firm) med API-nyckeln `dev-admin-key`, kontot `demo` som ägs av `demo@example.com` med lösenordet `demo-password` och kontot `test` som ägs av `test@test.com` med lösenordet `test`. De finns i `appsettings.Development.json` och gäller bara lokal utveckling.

Skapa en egen trader med firmans nyckel:

```bash
curl -X POST http://localhost:5101/api/admin/v1/users -H "X-Api-Key: dev-admin-key" -H "Content-Type: application/json" -d "{\"email\":\"me@example.com\",\"password\":\"my-password-1\"}"
```

Alla vägar finns i OpenAPI-dokumentet på http://localhost:5101/openapi/v1.json och i [specen för handelstjänsten](docs/spec/handelstjanst.md).

## Prova handelsterminalen

Starta handelstjänsten enligt ovan och starta sedan terminalen i en annan terminal:

```bash
pnpm dev:terminal
```

Öppna http://localhost:3001 och välj servern Demo Firm. Firmans traders loggar in genom portalen med knappen Log in through Demo Firm. Klicka i stället på I have a password for the terminal och logga in med `demo@example.com` och `demo-password`, eller med `test@test.com` och `test`. Har tradern flera konton byter den i listan i kontoraden, eller väljer ett med `?account=`, till exempel http://localhost:3001/?account=demo.

## Prova propfirm-tjänsten

Propfirm-tjänsten använder handelsplattformen, så starta handelstjänsten först. Starta sedan propfirm-tjänsten:

```bash
dotnet run --project prop/src/Prop.Api
```

Lokalt finns firman `demo-firm` med nyckeln `dev-prop-key` och challengerna `two-step-100k` och `quick-test-100k` (se Prova utbetalningar). Starta en challenge åt en trader. Tjänsten öppnar kontot på handelsplattformen och sätter golven:

```bash
curl -X POST http://localhost:5201/api/firm/v1/accounts -H "X-Api-Key: dev-prop-key" -H "Content-Type: application/json" -d "{\"email\":\"anna@example.com\",\"challengeId\":\"two-step-100k\"}"
```

Svaret har kontots `id`. Hämta en inloggningslänk till terminalen och öppna `url` inom två minuter:

```bash
curl -X POST http://localhost:5201/api/firm/v1/accounts/KONTO-ID/login-link -H "X-Api-Key: dev-prop-key"
```

Följ kontot med `GET /api/firm/v1/accounts/KONTO-ID` och varje beslut med `/history`. Alla vägar finns i [specen för propfirm-tjänsten](docs/spec/propfirm-tjanst.md).

## Prova portalen

Portalen använder propfirm-tjänsten, som använder handelstjänsten. Starta båda enligt ovan och sedan portalen:

```bash
pnpm dev:portal
```

| Inloggning | Adress | E-post och lösenord |
|---|---|---|
| Administratör | http://localhost:3002/admin/login | `admin@test.com` och `admin` |
| Trader | http://localhost:3002/login | `anna@test.com` och `anna`, eller `test@test.com` och `test` |
| Vår personal | http://ops.localhost:3002/ops/login | `ops@test.com` och `ops` |

1. Logga in som administratör och starta en challenge åt en trader, till exempel `anna@test.com`.
2. Logga in som trader i en ny flik och se kontot på startsidan. Administratören är fortfarande inloggad, eftersom rollerna har var sin session.
3. Klicka på Open terminal för att handla på kontot. Starta terminalen först (`pnpm dev:terminal`). Terminalen visar firmans namn och kontot som portalen, till exempel "#1001 Two-step 100K · Phase 1", med vinstmålet, gränserna, tiderna i challengens tidszon och länken Back to Demo Firm. Konton som öppnades innan får sitt namn när propfirm-tjänsten startar om.
4. Klicka på Details för kontots sida, med målen, grafen över saldot, dag för dag, statistik, de stängda affärerna och reglerna. Affärerna dyker upp där några sekunder efter att de stängts i terminalen. Ett underkänt konto förklarar varför saldot slutade under gränsen och har knappen Try again, och ett konto som klarat utvärderingen eller fått en utbetalning har diplom att ladda ned.
5. Andra traders får en inbjudningslänk från kontots sida i adminpanelen. Där finns också traderns kort med firmans kontroller (ID och adress), mejlet till tradern som visas innan det skickas, och regelloggen i vanliga meningar.
6. Glömt lösenord finns på alla inloggningar. Länken kommer till Mailpit på http://localhost:8025 och gäller i en timme.
7. Under Notifications i adminpanelen väljer firman vilka mejl vi skickar åt den, och under Trading conditions vilka instrument traderna handlar med vilken hävstång, påslag och provision. Demo Firm har sina villkor i konfigurationen och kan bara se dem, men en firma som registrerat sig kan ändra sina.

Lokalt finns inga krav på lösenordens längd, ingen gräns för antalet inloggningar och sessionerna gäller i 30 dagar (`Login` i `appsettings.Development.json` för båda tjänsterna). Vill du vara inloggad som två traders samtidigt, använd http://localhost:3002 för den ena och http://127.0.0.1:3002 för den andra. Webbläsaren håller isär inloggningarna per värdnamn.

Portalen ser ut som firman vars adress den öppnas på. Lokalt hör `localhost` och `127.0.0.1` till `demo-firm`. Allt detta gäller bara lokal utveckling. Se [specen för portalen](docs/spec/portal.md).

## Prova registreringen

En firma kan registrera sig själv och prova allt i en sandlåda. Starta handelstjänsten, propfirm-tjänsten och portalen enligt ovan, och Mailpit med docker compose.

1. Öppna http://app.localhost:3002, plattformens förstasida med vad en firma får och vad det kostar, och klicka på Start free sandbox. Fyll i firmans namn, ett kort namn (är det taget föreslås lediga), kontonas valuta (USD, EUR eller GBP), din e-post och ett lösenord, och godkänn villkoren. Har du glömt var din firma finns, klickar du på Log in to your firm och får en länk per mejl.
2. Du hamnar inloggad i guiden i firmans adminpanel på dess egen adress, till exempel http://acme.localhost:3002/admin/get-started: logga och färg, pris på den första challengen, testbetalning och att prova som trader. Firmans server på handelsplattformen skapas på några sekunder, tillsammans med en tvåstegs-challenge på 100 000 i firmans valuta. Ett välkomstmejl med adressen till adminpanelen kommer till Mailpit.
3. Starta en challenge, bjud in en trader och prova hela kedjan som med Demo Firm. Firman är i sandlådan, med högst 10 öppna konton, och portalen visar att det är en testmiljö. Under Go live går firman live, se nedan.
4. Under Your domain lägger du till en egen domän, till exempel `portal.dinfirma.se`, och får de två DNS-poster som behövs. Lokalt är målet för CNAME bara ett exempel, så domänen blir inte aktiv, men en riktig domän med posterna slås upp med DNS över HTTPS. Se [ADR 0039](docs/adr/0039-egen-doman-for-firmans-portal.md).
5. Översikten visar stegen till live. Under Portal design laddar du upp en logga och ändrar färgerna, under Trading conditions väljer du instrument och villkor, och under Integrations skapar du en nyckel för firmans API och sätter en webhook. Under Challenges sätter du priset direkt på varje challenge och gör nya från fyra mallar (ett, två eller tre steg, eller direkt funded) i flera storlekar på en gång, och under Team bjuder du in fler administratörer. Under Integrations finns API:ts adress med ett exempel, och knappen som skickar en testhändelse till din webhook. Mejlen syns i Mailpit på http://localhost:8025.

Lokalt behöver e-postadressen inte bekräftas. Vill du prova bekräftelsen, sätt `Signup:RequireEmailVerification` till `true` i propfirm-tjänstens `appsettings.Development.json`, så kommer länken i Mailpit. Webbläsare skickar alla adresser som slutar på `.localhost` till den egna datorn, så inga DNS-inställningar behövs. Se [specen för registreringen](docs/spec/registrering.md).

## Prova granskningen och vår adminvy

Innan en firma går live granskar vi den i vår egen adminvy. Firman skickar uppgifter om bolaget, ägarna och sina villkor, och betalar en handpenning som dras av från startavgiften. Lokalt är det testbetalningar, så inga pengar dras.

1. Registrera en firma enligt ovan och öppna Go live i dess adminpanel, till exempel http://acme.localhost:3002/admin/go-live. Sidan har fyra steg: bolagets uppgifter, handpenningen, vårt svar, och platser och betalning.
2. Fyll i bolagets uppgifter, där allt som saknas är markerat, släpp gärna in ett dokument och klicka på Save and continue. Handpenningen har moms för ett svenskt bolag, och ingen för ett bolag i ett annat EU-land med momsnummer. Klicka på knappen som betalar handpenningen och skickar, och på Pay på testsidan. Kvittot och att vi har fått ansökan kommer till Mailpit.
3. Öppna vår adminvy på http://ops.localhost:3002 och logga in med `ops@test.com` och `ops`. Översikten visar att firman väntar på granskning. Öppna den, bocka i kontrollerna och godkänn, be om ändringar eller neka. Firmans administratörer får ett mejl, som syns i Mailpit på http://localhost:8025. Under Firms finns alla firmor och under Billing vad de betalar.
4. På samma sida stänger du av en firma med en orsak. Dess challenges pausas och butiken stänger tills du slår på den igen.

Se [specen för granskningen](docs/spec/granskning.md).

## Prova att gå live och betala för platser

En firma som vi har godkänt går live genom att betala startavgiften minus handpenningen och sina platser för resten av månaden. Lokalt är det testbetalningar, så inga pengar dras. Priserna i `appsettings.json` är vårt förslag: 700 USD i startavgift varav 200 USD i handpenning, ett paket med 25 platser för 500 USD i månaden och sedan 5 USD per plats till och med plats 100 och 4 USD därefter.

1. Låt en firma bli godkänd enligt ovan och öppna Go live i dess adminpanel, till exempel http://acme.localhost:3002/admin/go-live, som nu öppnar sista steget.
2. Välj hur traders kontrolleras under KYC, vår inbyggda kontroll eller en egen tjänst. Innan dess går det inte att betala, och sista steget säger det.
3. Välj antal platser, se vad en automatisk utökning kostar och vad som händer när du går live, och klicka på knappen som betalar och går live. På testsidan nekar Try a card that declines, och Pay betalar. Firman är live, dess konton från sandlådan avslutas och deras traders får ett mejl, kvittot kommer till Mailpit och dess server syns i terminalens serverlista. Lokalt fortsätter testbetalningarna i butiken efter go-live. Utanför utveckling hindras go-live tills butiken tar riktiga pengar.
4. Starta challenges som vanligt. Varje challenge som inte har tagit slut tar en plats, och en order i portalen som väntar på betalning håller en. När platserna är slut stänger butiken. Köp fler under Plan and billing, som ersatt Go live i menyn, eller slå på automatisk utökning. Där finns också varje betald debitering som faktura i PDF, och bolagets uppgifter under en egen flik.
5. Under Change card sparar du ett testkort som nekas, för att se vad som händer när en månad inte går att dra. Månaden dras 5 dagar innan den börjar, och är den obetald när den börjar pausas firmans challenges tills ett kort som fungerar betalar den.

Med Stripe betalar firmorna till vårt eget Stripe-konto: sätt `Billing:Provider` till `Stripe`, `Billing:StripeSecretKey` till en testnyckel och `Billing:StripeWebhookSecret` till hemligheten från `stripe listen --forward-to http://localhost:5201/api/payments/v1/billing/stripe`. Se [specen för platser och betalning](docs/spec/platser-och-betalning.md).

## Prova köp i portalen

`demo-firm` säljer `two-step-100k` för 499 USD och `quick-test-100k` för 9 USD med testbetalningar, så inga riktiga pengar dras. Starta handelstjänsten, propfirm-tjänsten och portalen enligt ovan, och gärna Mailpit med docker compose.

1. Öppna http://localhost:3002, som skickar en besökare till butiken. Välj en storlek i pristabellen, ange e-post, namn och land och klicka på Pay.
2. Klicka på Pay på testsidan. Ordern blir betald och challengen startar.
3. Välj lösenord direkt på orderns sida och öppna kontot. Mejlet med länken som bekräftar e-posten kommer till Mailpit på http://localhost:8025, i firmans utseende. Utbetalningar kräver att den är bekräftad. Är du redan inloggad som trader köper du i stället med din egen e-post och går direkt till kontot.
4. Under Orders i adminpanelen syns ordern med köparens namn, under Challenges ändrar du priserna, under Checkout hur portalen tar betalt och under Notifications vart traders svar på mejlen går.
5. Under Discount codes skapar du en kod, till exempel `SPRING20` med 20 % rabatt, och skriver den i butiken innan du betalar. En kod med Only for a new try gäller bara den vars challenge har underkänts, och erbjuds på det underkända kontot med Try again.

Vill du prova Stripe, välj Stripe under Checkout hos en firma du har registrerat och klistra in din hemliga testnyckel. Med en adress som Stripe når lägger tjänsten till webhooken själv. Stripe når inte din dator, så kryssa i I add the webhook in Stripe myself och skicka webhooks med Stripe CLI: `stripe listen --forward-to http://localhost:5201/api/payments/v1/stripe/FIRMA` ger signeringshemligheten att klistra in. Se [specen för köp i portalen](docs/spec/kop.md).

## Prova utbetalningar

Challengen `quick-test-100k` finns bara lokalt. Den har vinstmål på 0,1 % (100 USD) och inga krav på antal handelsdagar, så hela vägen till en utbetalning tar några minuter. Starta handelstjänsten, propfirm-tjänsten, portalen och terminalen enligt ovan.

1. Logga in som administratör och starta challengen Quick test åt `anna@test.com`.
2. Logga in som `anna@test.com` i en ny flik och klicka på Open terminal. Handla tills en stängd affär ger minst 100 USD i vinst, till exempel 10 lot EURUSD som stängs efter ett par pips uppgång. Gör om det i fas 2.
3. Godkänn funded-kontot på kontots sida i adminpanelen.
4. Gör en vinst på funded-kontot och stäng alla positioner. Ange hur du vill få betalt under Payouts i portalen, och klicka sedan på Request payout på kontots sida. Hela vinsten tas från handelskontot direkt, och tradern får 80 % av den.
5. Godkänn utbetalningen under Payouts i adminpanelen, där kontonumret står att kopiera, och markera den som betald. Tradern ser den på kontots sida och under Payouts i portalen, och får ett mejl vid varje steg. Nekar du i stället väljer du om vinsten är förlorad eller läggs tillbaka på kontot. Har du inte bockat för dina kontroller av tradern frågar godkännandet först.
6. Under Challenges kan funded-fasen få en konsistensregel, "Best day at most", som håller en utbetalning tills den bästa dagen är en tillräckligt liten del av vinsten.

Challenges i `SeedChallenges` skapas eller ersätts vid varje start. Konton som startades innan har kvar sina regler, och ett funded-konto utan vinstandel kan inte få utbetalningar. Börja om från noll med `docker compose -f deploy/docker-compose.yml down -v` om du vill. Se [specen för regelmotorn](docs/spec/regelmotor.md) och [ADR 0015](docs/adr/0015-utbetalningar-tar-ut-vinsten-direkt.md).

## Prova support

Traders skriver till sin firma i portalen, och firman svarar i adminpanelen. Starta handelstjänsten, propfirm-tjänsten och portalen enligt ovan, och Mailpit med docker compose.

1. Logga in som trader på http://localhost:3002/login, till exempel `test@test.com` med `test`, och klicka på Support. Klicka på New ticket, skriv en rubrik och ett meddelande, välj gärna ett konto och lägg till en skärmbild (PDF, PNG eller JPEG, högst 5 MB och tre filer). På ett kontos sida gör knappen Ask about this account samma sak med kontot valt.
2. Administratören får ett mejl i Mailpit på http://localhost:8025. Logga in som administratör på http://localhost:3002/admin/login. Vid Support i menyn och i Needs you på översikten står hur många ärenden som väntar.
3. Öppna ärendet under Support och svara. Send and close svarar och stänger på en gång, och Close without answering stänger utan mejl.
4. Tradern får svaret per mejl i firmans namn, och ser det som oläst vid Support i portalen tills ärendet öppnas. Tradern skriver tillbaka eller stänger ärendet med Close ticket, och ett nytt meddelande öppnar ett stängt ärende igen.
5. Firman kan också skriva först, med Write to a trader under Support eller Write to the trader på traderns kort på ett kontos sida. Tradern får meddelandet per mejl och ser det som oläst vid Support.
6. Under Notifications stänger firman av mejlen om ärenden, till teamet och till traderna, var för sig.

Se [specen för supportärenden](docs/spec/support.md) och [ADR 0041](docs/adr/0041-supportarenden-mellan-traders-och-firman.md).

## Prova ID-kontrollen

Firman väljer hur traders kontrolleras under KYC i adminpanelen: vår inbyggda kontroll eller sin egen tjänst. Inget är förvalt. En firma i sandlådan kan skicka sin ansökan och bli godkänd utan att ha valt, men kan inte gå live förrän den valt, och fått sin egen tjänst att fungera hela vägen. Demo Firm är live utan val, så inget väntar på en kontroll där förrän du väljer. Lokalt är alla kontroller testkontroller, utan Didit och utan kostnad.

1. Logga in som administratör på http://localhost:3002/admin/login, öppna KYC och välj Our built-in KYC och Before the first payout. Spara.
2. Logga in som trader med ett funded-konto, eller öppna Payouts, och klicka på Verify your identity. Testsidan låter dig välja Approve eller Decline.
3. Godkänd visas tradern som kontrollerad, och traderns kort i adminpanelen har ID-kontrollen med ID checked bockad. Nekad visar orsaken, och tradern kan försöka igen. Mejlen kommer till Mailpit på http://localhost:8025.
4. Välj Your own KYC service för att skicka traders till en egen adress, och berätta resultatet med `PUT /api/firm/v1/traders/identity` och firmans nyckel, till exempel `dev-prop-key` för Demo Firm. När en trader har klickat på Verify with Demo Firm och du rapporterat Approved eller Declined för den tradern visar KYC att hela flödet fungerar.

För riktiga kontroller behövs ett konto hos Didit. Sätt `Identity:Provider` till `Didit` och lägg nyckeln, webhookens hemlighet och arbetsflödena i user secrets, aldrig i en fil i repot:

```bash
dotnet user-secrets set "Identity:Didit:ApiKey" "din-nyckel" --project prop/src/Prop.Api
```

Se [specen för ID-kontroll](docs/spec/id-kontroll.md) och [ADR 0042](docs/adr/0042-id-kontroll-med-en-extern-tjanst.md).

## Riktiga priser från Tiingo

Tjänsten använder syntetiska priser som standard. För riktiga priser under utvecklingen (ADR 0010):

1. Skapa ett gratis konto på [tiingo.com](https://www.tiingo.com) och kopiera din API-nyckel från [kontosidan för API](https://www.tiingo.com/account/api/token).
2. Spara den utanför repot med user secrets:

```bash
dotnet user-secrets set "PriceFeed:Provider" "Tiingo" --project trading/src/Trading.Service
```

```bash
dotnet user-secrets set "PriceFeed:Tiingo:ApiKey" "din-nyckel" --project trading/src/Trading.Service
```

Tiingos gratisplan tillåter inte att priserna visas för andra, så de är bara för utveckling. När valutamarknaden är stängd, från fredag kväll till söndag kväll, kommer inga nya priser och ordrar avvisas som för gamla. Använd då de syntetiska priserna. Gå tillbaka till syntetiska priser med `dotnet user-secrets remove "PriceFeed:Provider" --project trading/src/Trading.Service`. Testerna läser aldrig user secrets, så de påverkas inte av valet.

## Tester av hela flödet

Playwright startar egna tjänster med egna portar och databaser, så testerna kan köras medan du utvecklar. Portalens tester registrerar också firmor på http://app.localhost:3022 och granskar dem på http://ops.localhost:3022. Terminalens tester använder handelstjänsten och terminalen mot `trading_e2e`. Portalens tester använder handelstjänsten, propfirm-tjänsten och portalen mot `trading_portal_e2e` och `prop_e2e`. Postgres måste vara igång och tjänsterna byggda i Release.

```bash
dotnet build trading/src/Trading.Service -c Release
```

```bash
dotnet build prop/src/Prop.Api -c Release
```

```bash
pnpm --filter @trading/terminal exec playwright install chromium
```

```bash
pnpm --filter @trading/terminal e2e
```

```bash
pnpm --filter @prop/portal e2e
```

## När API:t ändras

Handelstjänsten skriver OpenAPI-dokumentet till `contracts/trading/` när den byggs. Det är kontraktet mot firmornas system och propfirm-plattformen (ADR 0012). Propfirm-tjänsten skriver sitt till `prop/portal/openapi/` på samma sätt, för portalen. Generera sedan terminalens och portalens typer:

```bash
pnpm generate:api
```

Committa filerna. CI stoppar ändringar där de inte är aktuella.

## Kontroller i CI

Vid varje push till `main` och varje pull request körs:

- **Backend:** formatering (`dotnet format`), bygge med varningar som fel, kontroll att OpenAPI-dokumenten är aktuella, tester. Testerna mot Postgres kör i en container.
- **Webb:** kontroll att API-typerna är aktuella, lint, typkontroll, tester och bygge.
- **Infrastruktur:** validering av docker compose.
- **Hela flödet:** Playwright mot handelstjänsten och terminalen, och mot hela kedjan med handelstjänsten, propfirm-tjänsten och portalen.
