# Arkitekturbeslut (ADR)

Varje viktigt tekniskt beslut får en egen fil. Filen beskriver sammanhanget, beslutet och konsekvenserna, så att det går att förstå i efterhand varför något är som det är.

## Beslut

| Nr | Beslut | Status |
|---|---|---|
| [0001](0001-monorepo-med-produktgranser.md) | Monorepo med hårda gränser mellan produkterna | Beslutad |
| [0002](0002-csharp-backend-typescript-webb.md) | C# i backend och TypeScript i webben | Beslutad |
| [0003](0003-postgres-och-nats.md) | Postgres och NATS JetStream, inget Redis i början | Föreslagen |
| [0004](0004-kontrakt-mellan-produkterna.md) | Protobuf för kontraktet mellan produkterna | Föreslagen |
| [0005](0005-deterministisk-handelsmotor.md) | Deterministisk handelsmotor | Föreslagen |
| [0006](0006-api-mellan-terminal-och-tjanst.md) | REST och SignalR mellan terminalen och handelstjänsten | Föreslagen |
| [0007](0007-graf-lightweight-charts.md) | TradingView Lightweight Charts för grafen | Beslutad |
| [0008](0008-journal-av-indata.md) | Journal av indata med ögonblicksbilder | Föreslagen |
| [0009](0009-inloggning-och-firmor.md) | Inloggning och firmor i handelsplattformen | Föreslagen |
| [0010](0010-prisflode-for-utveckling.md) | Tiingo som riktigt prisflöde under utvecklingen | Beslutad |
| [0011](0011-regelmotorn-satter-golv.md) | Regelmotorn sätter golv och avgör faserna | Föreslagen |

## Så skriver du en ny ADR

1. Kopiera strukturen nedan till en ny fil med nästa lediga nummer.
2. Sätt status till "Föreslagen". Ändra till "Beslutad" när beslutet är taget.
3. Ändra aldrig ett beslutat ADR i efterhand. Skriv ett nytt som ersätter det och sätt det gamla till "Ersatt av NNNN".

```markdown
# NNNN. Rubrik

- Status: Föreslagen | Beslutad | Ersatt av NNNN
- Datum: ÅÅÅÅ-MM-DD

## Sammanhang
## Beslut
## Konsekvenser
```
