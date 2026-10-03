# Spec: regelmotorn

- Fas: 4a, utbetalningar i 5
- Status: Implementerad i `prop/src/Prop.Rules`
- Datum: 2026-10-03

## Syfte

Regelmotorn avgör hur det går för en trader i en challenge: när ett konto ska öppnas, vilka förlustgränser som gäller, vilka dagar som räknas som handelsdagar, när en fas är klar, när challengen är underkänd och vad en funded trader får i utbetalning. Den är deterministisk: samma indata ger alltid samma beslut (se [ADR 0011](../adr/0011-regelmotorn-satter-golv.md)). Propfirm-tjänsten kör den mot handelsplattformen (se [specen för propfirm-tjänsten](propfirm-tjanst.md)).

## Vem gör vad

| Del | Ansvar |
|---|---|
| Handelsplattformen | Räknar equity och kontrollerar golven vid varje pris. Vid ett brott stängs allt, kontot stängs av och bevisen sparas. |
| Regelmotorn | Sätter golven, lägger om det dagliga golvet varje handelsdag, räknar handelsdagar, avgör när en fas är klar, styr livscykeln och tar utbetalningar från begäran till betald. |
| Tjänsten (fas 4c) | Gör om firmans handelsdag till indata, hämtar fakta från handelsplattformen, utför det regelmotorn begär och sparar allt. |

## Challenge

En challenge är det firman säljer. Varje challenge sparar en kopia av sin definition när den köps.

| Fält | Innehåll |
|---|---|
| `InitialBalance`, `Currency` | Kontostorlek, till exempel 100 000 USD. Samma storlek i varje fas. |
| `TradingDay` | När en handelsdag börjar: tidszon och klockslag. |
| `Evaluation` | Faserna som ska klaras, i ordning. Minst en. |
| `Funded` | Reglerna för funded-kontot. Inget vinstmål, men en vinstandel. |

Varje fas har:

- **Vinstmål** i procent av kontostorleken. Mäts på saldot och räknas när alla positioner är stängda.
- **Minsta antal handelsdagar.** En handelsdag är en dag då minst en position öppnades.
- **Max daglig förlust** i procent av kontostorleken, räknad från dagens startpunkt: saldot, eller det högsta av saldo och equity, när dagen börjar.
- **Max total förlust** i procent av kontostorleken. Fast under kontostorleken, eller släpande efter högsta equity och låst vid kontostorleken.

Funded-fasen har i stället för vinstmål en **vinstandel** (`ProfitSplitPercent`): hur stor del av vinsten tradern får, över 0 och högst 100 %. Dess minsta antal handelsdagar gäller mellan utbetalningarna. Utvärderingsfaserna har ingen vinstandel.

Procentsatser blir belopp avrundade till hela cent. Definitionen kontrolleras innan en challenge startas.

### Standardmall

`ChallengeTemplates.TwoStep` följer det vanligaste upplägget:

| | Fas 1 | Fas 2 | Funded |
|---|---|---|---|
| Vinstmål | 10 % | 5 % | inget |
| Minsta antal handelsdagar | 4 | 4 | 5 mellan utbetalningarna |
| Max daglig förlust | 5 % från saldot vid dagens start | 5 % | 5 % |
| Max total förlust | 10 %, fast | 10 %, fast | 10 %, fast |
| Vinstandel | | | 80 % |

En handelsdag börjar vid midnatt svensk tid (`Europe/Stockholm`).

## Livscykel

```
OpeningAccount -> Active -> (fas klar) -> OpeningAccount -> Active -> ...
                                       -> AwaitingFunding -> (firman godkänner) -> OpeningAccount -> Active (funded)
Active -> Failed      (ett golv bröts)
alla utom slut -> Cancelled  (firman avbröt, eller kontot stängdes av på handelsplattformen)
```

En utbetalning på funded-kontot har en egen livscykel. Bara en utbetalning åt gången kan vara på gång.

```
tradern begär -> Withdrawing (vinsten tas ut från kontot) -> Pending -> (firman godkänner) -> Approved -> (firman har betalat) -> Paid
Withdrawing -> Failed               (handelsplattformen nekade uttaget, inget hände)
Pending eller Approved -> Rejected  (firman nekar, vinsten återförs inte)
```

## Indata

| Indata | Från | Innehåll |
|---|---|---|
| `AccountOpened` | Tjänsten | Kontot för fasen är öppnat, med handelsplattformens löpnummer och handelsdagen. |
| `TradingDayStarted` | Tjänsten | En ny handelsdag har börjat. |
| `ApproveFunding` | Firman | Tradern får ett funded-konto efter firmans kontroller, till exempel KYC och avtal. |
| `CancelChallenge` | Firman | Challengen avbryts. |
| `RequestPayout` | Tradern | Tradern begär en utbetalning. Tjänsten väljer utbetalningens id. |
| `ApprovePayout`, `MarkPayoutPaid`, `RejectPayout` | Firman | Firman godkänner, markerar som betald med en egen referens, eller nekar med en orsak. |
| `WithdrawalRejected` | Tjänsten | Handelsplattformen nekade uttaget för en utbetalning. Tjänsten rapporterar det, eftersom den ser varje nej, även de som aldrig når handelsmotorn. |
| `AccountUpdated` | Handelsplattformen | Saldo och antal öppna positioner efter att en position stängts. |
| `PositionOpened` | Handelsplattformen | En position öppnades under en viss handelsdag. |
| `FloorBreached` | Handelsplattformen | Ett golv bröts, med nivå och equity från bevisen. |
| `AccountDisabled` | Handelsplattformen | Kontot stängdes av av någon annan anledning. |
| `BalanceAdjusted` | Handelsplattformen | Pengar sattes in eller togs ut, med operationens id, beloppet och saldot efteråt. |

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
| `PayoutRequested` | Tradern har begärt en utbetalning. Innehåller vinsten, vinstandelen och traderns belopp. |
| `WithdrawalRequested` | Ta ut vinsten från kontot en gång, med utbetalningens id, men bara om startsaldot finns kvar efteråt. |
| `PayoutWithdrawn` | Vinsten är uttagen. Firman ska godkänna utbetalningen. Blir webhooken `payout.requested`. |
| `PayoutApproved`, `PayoutPaid`, `PayoutRejected` | Firmans beslut. Blir webhooks `payout.approved`, `payout.paid` och `payout.rejected`. |
| `PayoutFailed` | Uttaget nekades, så utbetalningen blev inte av. |
| `InputIgnored` | Ett kommando från tjänsten eller firman passar inte tillståndet, till exempel ett godkännande innan faserna är klara. |

## Regler

- **När kontot öppnas** sätts golvet för total förlust och det dagliga golvet. Vid en ny handelsdag läggs det dagliga golvet om. Handelsplattformen räknar ut nivån från kontot just då.
- **Fasen är klar** när saldot når vinstmålet, inga positioner är öppna och fasen har minst det antal handelsdagar som krävs. Kontot stängs, och nästa fas får ett nytt konto. Efter sista fasen väntar challengen på firman.
- **Brott:** handelsplattformen har redan stängt allt och stängt av kontot. Challengen blir underkänd med nivån och equity från bevisen.
- **Ett konto som öppnas efter att challengen tagit slut** stängs direkt.
- **Öppna positioner** räknas upp när en position öppnas och sätts till plattformens antal när en stängs.
- **Trassliga fakta:** ett faktum används bara om det gäller det aktuella kontot och har ett högre löpnummer än alla tidigare. Upprepade och sena fakta, och fakta om en tidigare fas konto, ändrar ingenting. En handelsdag som redan har börjat ignoreras.

## Utbetalningar

Se [ADR 0015](../adr/0015-utbetalningar-tar-ut-vinsten-direkt.md) för besluten.

- **Vad tradern får:** vinsten är saldot minus kontostorleken, mätt på det saldo handelsplattformen senast rapporterade. Tradern får vinstandelen av den, avrundad nedåt till hela cent. Firman behåller resten.
- **När en utbetalning kan begäras:** kontot är ett aktivt funded-konto, challengen har en vinstandel, ingen annan utbetalning är på gång, det finns en vinst att betala ut, inga positioner är öppna och funded-fasens minsta antal handelsdagar har gått sedan förra utbetalningen. Annars svarar regelmotorn med orsaken.
- **Hela vinsten tas ut direkt.** Uttaget har utbetalningens id och kräver att startsaldot finns kvar efteråt. Kontot börjar alltså om från startsaldot, och tradern kan inte handla vidare på pengar som betalas ut.
- **När uttaget har gått igenom** är utbetalningen `Pending`, och en ny period börjar: handelsdagarna räknas från noll. Uttaget räknas även om firman avbröt challengen under tiden.
- **Nekar handelsplattformen uttaget**, till exempel för att en position stängdes med förlust just innan, blir utbetalningen `Failed`. Kontot är som innan, och tradern kan begära igen.
- **Firman** godkänner en utbetalning som väntar, markerar en godkänd som betald, eller nekar en som väntar eller är godkänd. Vid nej återförs inte vinsten. Firman nekar när tradern brutit mot villkoren, och låter annars utbetalningen vänta, till exempel på KYC.
- **Andra insättningar och uttag** ändrar bara saldot. De räknas inte som handelsresultat.
- `ChallengeRules.QuotePayout` visar vad en utbetalning skulle ge just nu, eller varför den inte kan begäras. Samma funktion avgör begäran, så portalen och regelmotorn är alltid överens.

## Tester

Testerna ligger i `prop/tests/Prop.Rules.Tests`:

- Definitioner, standardmallen och avrundning.
- Varje regel och övergång i livscykeln, och trassliga fakta.
- Utbetalningar (`PayoutRulesTests`): beloppet och avrundningen, varje orsak att neka, en åt gången, uttaget som startar en ny period, godkännande, betalning, nej, nekat uttag och uttag efter avbruten challenge.
- En hel challenge från köp via en utbetalning till brott på funded-kontot spelas upp och jämförs med facitfilen `Golden/two-step-challenge.jsonl`. Varje ändring av ett beslut syns som en diff. Skapa om facit med `UPDATE_GOLDEN=1` och granska diffen.
- Att regelmotorn aldrig använder flyttal, och att den inte når klocka, slump eller I/O (`BannedSymbols.txt`).

## Begränsningar

- Regelmotorn används av propfirm-tjänsten (se [specen för propfirm-tjänsten](propfirm-tjanst.md)), som gör `StartOfDayFloor` till handelsplattformens `AnchoredFloor`.
- Ingen regel för jämna resultat, inaktivitet eller nyhetshandel än.
- Bara hela vinsten kan betalas ut, inte en del av den.
- Skalning av funded-konton kommer senare.
