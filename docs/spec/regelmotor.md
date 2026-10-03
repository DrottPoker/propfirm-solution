# Spec: regelmotorn

- Fas: 4a
- Status: Implementerad i `prop/src/Prop.Rules`
- Datum: 2026-10-03

## Syfte

Regelmotorn avgör hur det går för en trader i en challenge: när ett konto ska öppnas, vilka förlustgränser som gäller, vilka dagar som räknas som handelsdagar, när en fas är klar och när challengen är underkänd. Den är deterministisk: samma indata ger alltid samma beslut (se [ADR 0011](../adr/0011-regelmotorn-satter-golv.md)). Tjänsten runt den byggs i fas 4c.

## Vem gör vad

| Del | Ansvar |
|---|---|
| Handelsplattformen | Räknar equity och kontrollerar golven vid varje pris. Vid ett brott stängs allt, kontot stängs av och bevisen sparas. |
| Regelmotorn | Sätter golven, lägger om det dagliga golvet varje handelsdag, räknar handelsdagar, avgör när en fas är klar och styr livscykeln. |
| Tjänsten (fas 4c) | Gör om firmans handelsdag till indata, hämtar fakta från handelsplattformen, utför det regelmotorn begär och sparar allt. |

## Challenge

En challenge är det firman säljer. Varje challenge sparar en kopia av sin definition när den köps.

| Fält | Innehåll |
|---|---|
| `InitialBalance`, `Currency` | Kontostorlek, till exempel 100 000 USD. Samma storlek i varje fas. |
| `TradingDay` | När en handelsdag börjar: tidszon och klockslag. |
| `Evaluation` | Faserna som ska klaras, i ordning. Minst en. |
| `Funded` | Reglerna för funded-kontot. Inget vinstmål. |

Varje fas har:

- **Vinstmål** i procent av kontostorleken. Mäts på saldot och räknas när alla positioner är stängda.
- **Minsta antal handelsdagar.** En handelsdag är en dag då minst en position öppnades.
- **Max daglig förlust** i procent av kontostorleken, räknad från dagens startpunkt: saldot, eller det högsta av saldo och equity, när dagen börjar.
- **Max total förlust** i procent av kontostorleken. Fast under kontostorleken, eller släpande efter högsta equity och låst vid kontostorleken.

Procentsatser blir belopp avrundade till hela cent. Definitionen kontrolleras innan en challenge startas.

### Standardmall

`ChallengeTemplates.TwoStep` följer det vanligaste upplägget:

| | Fas 1 | Fas 2 | Funded |
|---|---|---|---|
| Vinstmål | 10 % | 5 % | inget |
| Minsta antal handelsdagar | 4 | 4 | 0 |
| Max daglig förlust | 5 % från saldot vid dagens start | 5 % | 5 % |
| Max total förlust | 10 %, fast | 10 %, fast | 10 %, fast |

En handelsdag börjar vid midnatt svensk tid (`Europe/Stockholm`).

## Livscykel

```
OpeningAccount -> Active -> (fas klar) -> OpeningAccount -> Active -> ...
                                       -> AwaitingFunding -> (firman godkänner) -> OpeningAccount -> Active (funded)
Active -> Failed      (ett golv bröts)
alla utom slut -> Cancelled  (firman avbröt, eller kontot stängdes av på handelsplattformen)
```

## Indata

| Indata | Från | Innehåll |
|---|---|---|
| `AccountOpened` | Tjänsten | Kontot för fasen är öppnat, med handelsplattformens löpnummer och handelsdagen. |
| `TradingDayStarted` | Tjänsten | En ny handelsdag har börjat. |
| `ApproveFunding` | Firman | Tradern får ett funded-konto efter firmans kontroller, till exempel KYC och avtal. |
| `CancelChallenge` | Firman | Challengen avbryts. |
| `AccountUpdated` | Handelsplattformen | Saldo, equity och antal öppna positioner. |
| `PositionOpened` | Handelsplattformen | En position öppnades under en viss handelsdag. |
| `FloorBreached` | Handelsplattformen | Ett golv bröts, med nivå och equity från bevisen. |
| `AccountDisabled` | Handelsplattformen | Kontot stängdes av av någon annan anledning. |

## Utdata

| Utdata | Innebär |
|---|---|
| `OpenAccountRequested` | Öppna ett konto för fasen. |
| `FloorRequested` | Sätt ett golv: `max-loss` som fast eller släpande, `daily` som dagens startpunkt minus ett avstånd. |
| `CloseAccountRequested` | Stäng kontot, efter en klar fas eller när challengen avbryts. |
| `StageStarted`, `TradingDayCounted` | Framsteg för traderns dashboard. |
| `StagePassed` | Fasen är klar. Blir webhooken `account.passed`. |
| `FundingAwaited` | Alla faser är klara. Firman ska godkänna funded-kontot. |
| `ChallengeFailed` | Ett golv bröts: daglig förlust, total förlust eller ett annat golv. Blir webhooken `account.breached`. |
| `ChallengeCancelled` | Challengen avbröts. |
| `InputIgnored` | Ett kommando från tjänsten eller firman passar inte tillståndet, till exempel ett godkännande innan faserna är klara. |

## Regler

- **När kontot öppnas** sätts golvet för total förlust och det dagliga golvet. Vid en ny handelsdag läggs det dagliga golvet om. Handelsplattformen räknar ut nivån från kontot just då.
- **Fasen är klar** när saldot når vinstmålet, inga positioner är öppna och fasen har minst det antal handelsdagar som krävs. Kontot stängs, och nästa fas får ett nytt konto. Efter sista fasen väntar challengen på firman.
- **Brott:** handelsplattformen har redan stängt allt och stängt av kontot. Challengen blir underkänd med nivån och equity från bevisen.
- **Ett konto som öppnas efter att challengen tagit slut** stängs direkt.
- **Trassliga fakta:** ett faktum används bara om det gäller det aktuella kontot och har ett högre löpnummer än alla tidigare. Upprepade och sena fakta, och fakta om en tidigare fas konto, ändrar ingenting. En handelsdag som redan har börjat ignoreras.

## Tester

Testerna ligger i `prop/tests/Prop.Rules.Tests`:

- Definitioner, standardmallen och avrundning.
- Varje regel och övergång i livscykeln, och trassliga fakta.
- En hel challenge från köp till brott på funded-kontot spelas upp och jämförs med facitfilen `Golden/two-step-challenge.jsonl`. Varje ändring av ett beslut syns som en diff. Skapa om facit med `UPDATE_GOLDEN=1` och granska diffen.
- Att regelmotorn aldrig använder flyttal, och att den inte når klocka, slump eller I/O (`BannedSymbols.txt`).

## Begränsningar

- Handelsplattformen saknar än golvet som räknas från dagens startpunkt (fas 4b).
- Ingen regel för jämna resultat, inaktivitet eller nyhetshandel än.
- Utbetalningar och skalning av funded-konton kommer senare.
