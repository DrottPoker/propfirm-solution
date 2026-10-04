# propfirm-solution

Två produkter för små och nystartade propfirms, som kan säljas var för sig eller tillsammans:

1. **Handelsplattform** för simulerad handel med riktiga livepriser.
2. **Propfirm-plattform** med challenges, regelmotor, traderportal, adminpanel och utbetalningsflöde.

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

Öppna http://localhost:3001, välj servern Demo Firm och logga in med `demo@example.com` och `demo-password`, eller med `test@test.com` och `test`. Har tradern flera konton byter den i listan i kontoraden, eller väljer ett med `?account=`, till exempel http://localhost:3001/?account=demo.

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
3. Klicka på Open terminal för att handla på kontot. Starta terminalen först (`pnpm dev:terminal`).
4. Klicka på Details för kontots sida, med målen, grafen över saldot, dag för dag, statistik, de stängda affärerna och reglerna. Affärerna dyker upp där några sekunder efter att de stängts i terminalen.
5. Andra traders får en inbjudningslänk från kontots sida i adminpanelen.

Lokalt finns inga krav på lösenordens längd, ingen gräns för antalet inloggningar och sessionerna gäller i 30 dagar (`Login` i `appsettings.Development.json` för båda tjänsterna). Vill du vara inloggad som två traders samtidigt, använd http://localhost:3002 för den ena och http://127.0.0.1:3002 för den andra. Webbläsaren håller isär inloggningarna per värdnamn.

Portalen ser ut som firman vars adress den öppnas på. Lokalt hör `localhost` och `127.0.0.1` till `demo-firm`. Allt detta gäller bara lokal utveckling. Se [specen för portalen](docs/spec/portal.md).

## Prova registreringen

En firma kan registrera sig själv och prova allt i en sandlåda. Starta handelstjänsten, propfirm-tjänsten och portalen enligt ovan, och Mailpit med docker compose.

1. Öppna http://app.localhost:3002/signup. Fyll i firmans namn, ett kort namn, din e-post och ett lösenord, och godkänn villkoren.
2. Du hamnar inloggad i firmans adminpanel på dess egen adress, till exempel http://acme.localhost:3002/admin. Firmans server på handelsplattformen skapas på några sekunder, tillsammans med en tvåstegs-challenge på 100 000 USD.
3. Starta en challenge, bjud in en trader och prova hela kedjan som med Demo Firm. Firman är i sandlådan, med högst 10 öppna konton, och portalen visar att det är en testmiljö. Under Verification och Billing går firman live, se nedan.
4. Under Settings ändrar du logga och färger, skapar en nyckel för firmans API och sätter en webhook. Under Challenges gör du egna challenges, och under Team bjuder du in fler administratörer. Mejlen syns i Mailpit på http://localhost:8025.

Lokalt behöver e-postadressen inte bekräftas. Vill du prova bekräftelsen, sätt `Signup:RequireEmailVerification` till `true` i propfirm-tjänstens `appsettings.Development.json`, så kommer länken i Mailpit. Webbläsare skickar alla adresser som slutar på `.localhost` till den egna datorn, så inga DNS-inställningar behövs. Se [specen för registreringen](docs/spec/registrering.md).

## Prova granskningen och vår adminvy

Innan en firma går live granskar vi den i vår egen adminvy. Firman skickar uppgifter om bolaget, ägarna och sina villkor, och betalar en handpenning som dras av från startavgiften. Lokalt är det testbetalningar, så inga pengar dras.

1. Registrera en firma enligt ovan och öppna Verification i dess adminpanel, till exempel http://acme.localhost:3002/admin/verification.
2. Fyll i bolagets uppgifter, lägg gärna till ett dokument och klicka på knappen som betalar handpenningen och skickar. Klicka på Pay på testsidan.
3. Öppna vår adminvy på http://ops.localhost:3002 och logga in med `ops@test.com` och `ops`. Firman väntar under To review. Öppna den och godkänn, be om ändringar eller neka. Firmans administratörer får ett mejl, som syns i Mailpit på http://localhost:8025.
4. På samma sida stänger du av en firma med en orsak. Dess challenges pausas och butiken stänger tills du slår på den igen.

Se [specen för granskningen](docs/spec/granskning.md).

## Prova att gå live och betala för platser

En firma som vi har godkänt går live genom att betala startavgiften minus handpenningen och sina platser för resten av månaden. Lokalt är det testbetalningar, så inga pengar dras. Priserna i `appsettings.json` är vårt förslag: 700 USD i startavgift varav 200 USD i handpenning, ett paket med 25 platser för 500 USD i månaden och sedan 5 USD per plats till och med plats 100 och 4 USD därefter.

1. Låt en firma bli godkänd enligt ovan och öppna Billing i dess adminpanel, till exempel http://acme.localhost:3002/admin/billing.
2. Välj antal platser och klicka på knappen som betalar och går live. På testsidan nekar Try a card that declines, och Pay betalar. Firman är live, och dess konton från sandlådan avslutas.
3. Starta challenges som vanligt. Varje challenge som inte har tagit slut tar en plats, och en order i portalen som väntar på betalning håller en. När platserna är slut stänger butiken. Köp fler på Billing, eller slå på automatisk utökning.
4. Under Change card sparar du ett testkort som nekas, för att se vad som händer när en månad inte går att dra. Månaden dras 5 dagar innan den börjar, och är den obetald när den börjar pausas firmans challenges tills ett kort som fungerar betalar den.

Med Stripe betalar firmorna till vårt eget Stripe-konto: sätt `Billing:Provider` till `Stripe`, `Billing:StripeSecretKey` till en testnyckel och `Billing:StripeWebhookSecret` till hemligheten från `stripe listen --forward-to http://localhost:5201/api/payments/v1/billing/stripe`. Se [specen för platser och betalning](docs/spec/platser-och-betalning.md).

## Prova köp i portalen

`demo-firm` säljer `two-step-100k` för 499 USD och `quick-test-100k` för 9 USD med testbetalningar, så inga riktiga pengar dras. Starta handelstjänsten, propfirm-tjänsten och portalen enligt ovan, och gärna Mailpit med docker compose.

1. Öppna http://localhost:3002/buy, välj en challenge, ange en e-postadress och klicka på Pay.
2. Klicka på Pay på testsidan. Ordern blir betald och challengen startar.
3. Inbjudan att välja lösenord kommer till Mailpit på http://localhost:8025. Är du redan inloggad som trader köper du i stället med din egen e-post och går direkt till kontot.
4. Under Orders i adminpanelen syns ordern, och under Challenges och Settings ändrar du priser och hur portalen tar betalt.

Vill du prova Stripe, välj Stripe under Settings hos en firma du har registrerat och klistra in dina testnycklar. Stripe når inte din dator, så skicka webhooks med Stripe CLI: `stripe listen --forward-to http://localhost:5201/api/payments/v1/stripe/FIRMA` ger signeringshemligheten att klistra in. Se [specen för köp i portalen](docs/spec/kop.md).

## Prova utbetalningar

Challengen `quick-test-100k` finns bara lokalt. Den har vinstmål på 0,1 % (100 USD) och inga krav på antal handelsdagar, så hela vägen till en utbetalning tar några minuter. Starta handelstjänsten, propfirm-tjänsten, portalen och terminalen enligt ovan.

1. Logga in som administratör och starta challengen Quick test åt `anna@test.com`.
2. Logga in som `anna@test.com` i en ny flik och klicka på Open terminal. Handla tills en stängd affär ger minst 100 USD i vinst, till exempel 10 lot EURUSD som stängs efter ett par pips uppgång. Gör om det i fas 2.
3. Godkänn funded-kontot på kontots sida i adminpanelen.
4. Gör en vinst på funded-kontot och stäng alla positioner. Klicka sedan på Request payout på kontots sida i portalen. Hela vinsten tas från handelskontot direkt, och tradern får 80 % av den.
5. Godkänn utbetalningen under Payouts i adminpanelen, och markera den som betald. Tradern ser den på kontots sida och under Payouts i portalen.

Challenges i `SeedChallenges` skapas eller ersätts vid varje start. Konton som startades innan har kvar sina regler, och ett funded-konto utan vinstandel kan inte få utbetalningar. Börja om från noll med `docker compose -f deploy/docker-compose.yml down -v` om du vill. Se [specen för regelmotorn](docs/spec/regelmotor.md) och [ADR 0015](docs/adr/0015-utbetalningar-tar-ut-vinsten-direkt.md).

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
