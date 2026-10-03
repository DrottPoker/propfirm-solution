# propfirm-solution

Två produkter för små och nystartade propfirms, som kan säljas var för sig eller tillsammans:

1. **Handelsplattform** för simulerad handel med riktiga livepriser.
2. **Propfirm-plattform** med challenges, regelmotor, traderportal, adminpanel och utbetalningsflöde.

Se [produktplanen](docs/produktplan-handelsplattform-propfirm.md), [arkitekturbesluten](docs/adr/README.md) och specarna för [handelsmotorn](docs/spec/handelsmotor.md), [handelstjänsten](docs/spec/handelstjanst.md) och [handelsterminalen](docs/spec/handelsterminal.md).

## Struktur

```
propfirm-solution/
├── docs/                 # produktplan, arkitekturbeslut (ADR) och specar
├── contracts/            # kontraktet mellan produkterna, det enda de delar
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
│   │   └── Prop.Api/          # regelmotor, kontots livscykel, API och webhooks
│   ├── tests/
│   └── portal/           # traderportal, adminpanel och uppstart (Next.js)
├── shared/               # generell kod utan affärslogik
└── deploy/               # docker compose för lokal utveckling
```

## Produktgränser

En produkt får bara använda kod från sin egen mapp, `contracts/` och `shared/`. Det kontrolleras automatiskt: .NET-bygget stoppar med `BOUNDARY001`, och ESLint stoppar import mellan webbapparna. Varje produkt har också egen databas. Se [ADR 0001](docs/adr/0001-monorepo-med-produktgranser.md).

## Förutsättningar

- [.NET SDK 10](https://dotnet.microsoft.com/download) (versionen styrs av `global.json`)
- [Node.js 22](https://nodejs.org/) eller senare
- [pnpm](https://pnpm.io/installation) (versionen styrs av `packageManager` i `package.json`)
- [Docker](https://www.docker.com/) för Postgres och NATS, och för testerna mot Postgres

## Kom igång

Starta Postgres och NATS:

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

Lösenorden i `deploy/` gäller bara lokal utveckling.

## Prova handelstjänsten

Tjänsten sparar allt i Postgres, så starta databasen först (se Kom igång). Den startar med syntetiska priser och kontot `demo` (100 000 USD). Den är inte klar för produktion och startar därför bara i miljön Development.

Konton, positioner och historik finns kvar efter en omstart. Stäng av med Ctrl+C, så sparas en ögonblicksbild och nästa start går snabbt. Börja om från noll med `docker compose -f deploy/docker-compose.yml down -v`.

```bash
dotnet run --project trading/src/Trading.Service
```

Lokalt finns firman `demo-firm` med API-nyckeln `dev-admin-key`, och kontot `demo` som ägs av `demo@example.com` med lösenordet `demo-password`. De finns i `appsettings.Development.json` och gäller bara lokal utveckling.

Skapa en egen trader med firmans nyckel:

```bash
curl -X POST http://localhost:5101/api/admin/users -H "X-Api-Key: dev-admin-key" -H "Content-Type: application/json" -d "{\"email\":\"me@example.com\",\"password\":\"my-password-1\"}"
```

Alla vägar finns i OpenAPI-dokumentet på http://localhost:5101/openapi/v1.json och i [specen för handelstjänsten](docs/spec/handelstjanst.md).

## Prova handelsterminalen

Starta handelstjänsten enligt ovan och starta sedan terminalen i en annan terminal:

```bash
pnpm dev:terminal
```

Öppna http://localhost:3001 och logga in med `demo@example.com` och `demo-password`. Har tradern flera konton väljs ett med `?account=`, till exempel http://localhost:3001/?account=demo.

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

Playwright startar en egen tjänst och terminal mot databasen `trading_e2e`, så testerna kan köras medan du utvecklar. Postgres måste vara igång och tjänsten byggd i Release.

```bash
dotnet build trading/src/Trading.Service -c Release
```

```bash
pnpm --filter @trading/terminal exec playwright install chromium
```

```bash
pnpm --filter @trading/terminal e2e
```

## När API:t ändras

Handelstjänsten skriver OpenAPI-dokumentet till `trading/terminal/openapi/` när den byggs. Generera sedan terminalens typer:

```bash
pnpm generate:api
```

Committa båda filerna. CI stoppar ändringar där de inte är aktuella.

## Kontroller i CI

Vid varje push till `main` och varje pull request körs:

- **Backend:** formatering (`dotnet format`), bygge med varningar som fel, kontroll att OpenAPI-dokumentet är aktuellt, tester.
- **Webb:** kontroll att API-typerna är aktuella, lint, typkontroll, tester och bygge.
- **Infrastruktur:** validering av docker compose.
- **Hela flödet:** Playwright mot tjänsten, terminalen och Postgres.
