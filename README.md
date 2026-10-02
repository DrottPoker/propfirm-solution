# propfirm-solution

Två produkter för små och nystartade propfirms, som kan säljas var för sig eller tillsammans:

1. **Handelsplattform** för simulerad handel med riktiga livepriser.
2. **Propfirm-plattform** med challenges, regelmotor, traderportal, adminpanel och utbetalningsflöde.

Se [produktplanen](docs/produktplan-handelsplattform-propfirm.md) och [arkitekturbesluten](docs/adr/README.md).

## Struktur

```
propfirm-solution/
├── docs/                 # produktplan och arkitekturbeslut (ADR)
├── contracts/            # kontraktet mellan produkterna, det enda de delar
├── trading/              # Produkt 1: handelsplattformen
│   ├── src/
│   │   ├── Trading.Engine/    # deterministisk kärna, ingen I/O
│   │   └── Trading.Service/   # tjänsten runt kärnan
│   ├── tests/
│   └── terminal/         # webbgränssnittet för traders (Next.js)
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

## Kontroller i CI

Vid varje push till `main` och varje pull request körs:

- **Backend:** formatering (`dotnet format`), bygge med varningar som fel, tester.
- **Webb:** lint, typkontroll och bygge.
- **Infrastruktur:** validering av docker compose.
