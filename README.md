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
│   │   └── Trading.Service/   # motorloop, prisflöde, candles, REST och SignalR
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
- [Docker](https://www.docker.com/) för Postgres och NATS

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

Tjänsten startar med syntetiska priser och kontot `demo` (100 000 USD). Den har ingen inloggning än och startar därför bara i miljön Development.

```bash
dotnet run --project trading/src/Trading.Service
```

Visa kontot och priserna:

```bash
curl http://localhost:5101/api/accounts/demo
```

```bash
curl http://localhost:5101/api/accounts/demo/prices
```

Köp 1 lot EURUSD:

```bash
curl -X POST http://localhost:5101/api/accounts/demo/orders -H "Content-Type: application/json" -d "{\"orderId\":\"o-1\",\"symbol\":\"EURUSD\",\"side\":\"Buy\",\"type\":\"Market\",\"volume\":1.00}"
```

Alla vägar finns i OpenAPI-dokumentet på http://localhost:5101/openapi/v1.json och i [specen för handelstjänsten](docs/spec/handelstjanst.md).

## Prova handelsterminalen

Starta handelstjänsten enligt ovan och starta sedan terminalen i en annan terminal:

```bash
pnpm dev:terminal
```

Öppna http://localhost:3001. Kontot väljs i adressen, till exempel http://localhost:3001/?account=demo.

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
