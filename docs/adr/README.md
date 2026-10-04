# Arkitekturbeslut (ADR)

Varje viktigt tekniskt beslut får en egen fil. Filen beskriver sammanhanget, beslutet och konsekvenserna, så att det går att förstå i efterhand varför något är som det är.

## Beslut

| Nr | Beslut | Status |
|---|---|---|
| [0001](0001-monorepo-med-produktgranser.md) | Monorepo med hårda gränser mellan produkterna | Beslutad |
| [0002](0002-csharp-backend-typescript-webb.md) | C# i backend och TypeScript i webben | Beslutad |
| [0003](0003-postgres-och-nats.md) | Postgres och NATS JetStream, inget Redis i början | Föreslagen |
| [0004](0004-kontrakt-mellan-produkterna.md) | Protobuf för kontraktet mellan produkterna | Ersatt av 0012 |
| [0005](0005-deterministisk-handelsmotor.md) | Deterministisk handelsmotor | Föreslagen |
| [0006](0006-api-mellan-terminal-och-tjanst.md) | REST och SignalR mellan terminalen och handelstjänsten | Föreslagen |
| [0007](0007-graf-lightweight-charts.md) | TradingView Lightweight Charts för grafen | Beslutad |
| [0008](0008-journal-av-indata.md) | Journal av indata med ögonblicksbilder | Föreslagen |
| [0009](0009-inloggning-och-firmor.md) | Inloggning och firmor i handelsplattformen | Föreslagen |
| [0010](0010-prisflode-for-utveckling.md) | Tiingo som riktigt prisflöde under utvecklingen | Beslutad |
| [0011](0011-regelmotorn-satter-golv.md) | Regelmotorn sätter golv och avgör faserna | Föreslagen |
| [0012](0012-publikt-admin-api-som-kontrakt.md) | Handelsplattformens publika admin-API är kontraktet mellan produkterna | Föreslagen |
| [0013](0013-journal-och-utkorg-i-propfirm-tjansten.md) | Journal och utkorg i propfirm-tjänsten | Föreslagen |
| [0014](0014-vitmarkt-portal-pa-firmans-adress.md) | Vitmärkt portal på firmans adress med sessioner i propfirm-tjänsten | Föreslagen |
| [0015](0015-utbetalningar-tar-ut-vinsten-direkt.md) | Utbetalningar tar ut vinsten direkt | Föreslagen |
| [0016](0016-firmor-skapas-medan-handelsplattformen-kor.md) | Firmor och deras grupper skapas medan handelsplattformen kör | Föreslagen |
| [0017](0017-firmor-registrerar-sig-sjalva.md) | Firmor registrerar sig själva och börjar i en sandlåda | Föreslagen |
| [0018](0018-domaner-och-underdomaner.md) | Domäner och underdomäner för produkterna | Föreslagen |
| [0019](0019-kop-i-portalen-med-firmans-betalningsleverantor.md) | Köp i portalen med firmans egen betalningsleverantör | Föreslagen |
| [0020](0020-forbetalda-platser-for-aktiva-challenges.md) | Förbetalda platser för aktiva challenges | Föreslagen |
| [0021](0021-vi-granskar-firmor-innan-de-gar-live.md) | Vi granskar firmor själva innan de går live | Föreslagen |

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
