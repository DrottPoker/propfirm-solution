# Spec: regelmotorn

- Fas: 4a, utbetalningar i 5, inaktivitet, tidsgräns och paus i 7
- Status: Implementerad i `prop/src/Prop.Rules`
- Datum: 2026-10-03

## Syfte

Regelmotorn avgör hur det går för en trader i en challenge: när ett konto ska öppnas, vilka förlustgränser som gäller, vilka dagar som räknas som handelsdagar, när en fas är klar, när challengen är underkänd eller har tagit slut på tid, och vad en funded trader får i utbetalning. Den är deterministisk: samma indata ger alltid samma beslut (se [ADR 0011](../adr/0011-regelmotorn-satter-golv.md)). Propfirm-tjänsten kör den mot handelsplattformen (se [specen för propfirm-tjänsten](propfirm-tjanst.md)).

## Vem gör vad

| Del | Ansvar |
|---|---|
| Handelsplattformen | Räknar equity och kontrollerar golven vid varje pris. Vid ett brott stängs allt, kontot stängs av och bevisen sparas. |
| Regelmotorn | Sätter golven, lägger om det dagliga golvet varje handelsdag, räknar handelsdagar, avgör när en fas är klar eller har tagit slut på tid, styr livscykeln, pausar och tar utbetalningar från begäran till betald. |
| Tjänsten (fas 4c) | Gör om firmans handelsdag till indata, hämtar fakta från handelsplattformen, pausar firmans challenges när månaden är obetald (se [specen för platser och betalning](platser-och-betalning.md)), utför det regelmotorn begär och sparar allt. |

## Challenge

En challenge är det firman säljer. Varje challenge sparar en kopia av sin definition när den köps.

| Fält | Innehåll |
|---|---|
| `InitialBalance`, `Currency` | Kontostorlek, till exempel 100 000 USD. Samma storlek i varje fas. |
| `TradingDay` | När en handelsdag börjar: tidszon och klockslag. |
| `Evaluation` | Faserna som ska klaras, i ordning. Minst en. |
| `Funded` | Reglerna för funded-kontot. Inget vinstmål, men en vinstandel. |
| `InactivityDays` | Hur många dagar utan en ny position challengen får ha, 1 till 365, i alla faser och som funded. Tomt för ingen regel. |

Varje fas har:

- **Vinstmål** i procent av kontostorleken. Mäts på saldot och räknas när alla positioner är stängda.
- **Minsta antal handelsdagar.** En handelsdag är en dag då minst en position öppnades.
- **Max daglig förlust** i procent av kontostorleken, räknad från dagens startpunkt: saldot, eller det högsta av saldo och equity, när dagen börjar.
- **Max total förlust** i procent av kontostorleken. Fast under kontostorleken, eller släpande efter högsta equity och låst vid kontostorleken.
- **Tidsgräns** (`MaxDays`), valfri: fasen ska vara klar inom så många dagar efter dagen den började, 1 till 365 och minst fasens minsta antal handelsdagar. Funded-fasen har ingen tidsgräns.

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
| Tidsgräns | ingen | ingen | ingen |

En handelsdag börjar vid midnatt svensk tid (`Europe/Stockholm`). Challengen tar slut efter 30 dagar utan en ny position.

## Livscykel

```
OpeningAccount -> Active -> (fas klar) -> OpeningAccount -> Active -> ...
                                       -> AwaitingFunding -> (firman godkänner) -> OpeningAccount -> Active (funded)
Active -> Failed      (ett golv bröts, tidsgränsen tog slut eller ingen ny position på för länge)
alla utom slut -> Cancelled  (firman avbröt, eller kontot stängdes av på handelsplattformen)
```

En challenge som inte har tagit slut kan också vara pausad medan firmans månad är obetald. Den har då samma status, men tradern kan inte öppna nya positioner och dagarna räknas inte.

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
| `PauseChallenge` | Tjänsten | Firmans månad är obetald, så challengen pausas under handelsdagen. |
| `ResumeChallenge` | Tjänsten | Firmans månad är betald, så challengen fortsätter under handelsdagen. |
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
| `ChallengeExpired` | Challengen tog slut på tid när en handelsdag började: fasens tidsgräns (`TimeLimit`) eller dagarna utan en ny position (`Inactivity`). Blir webhooken `account.expired`. |
| `ChallengeCancelled` | Challengen avbröts. |
| `ChallengePaused`, `ChallengeResumed` | Challengen pausades, eller fortsätter med sina tidsgränser flyttade så många dagar som den var pausad. Blir webhooks `account.paused` och `account.resumed`. |
| `SuspendAccountRequested`, `ResumeAccountRequested` | Stoppa nya positioner på kontot, eller tillåt dem igen. Tradern kan alltid stänga sina positioner. |
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
- **Inaktivitet:** varje ny position, och starten av en fas, ger challengen `InactivityDays` dagar efter den dagen till nästa position. När dagen efter dem börjar tar challengen slut, och kontot stängs. Att hålla en position öppen räknas inte som aktivitet. Med 30 dagar och en position den 1 november är 1 december den sista dagen att öppna nästa, och challengen tar slut när 2 december börjar.
- **Tidsgräns:** en fas med `MaxDays` tar slut när dagen `MaxDays` dagar efter dagen den började har passerat, om den inte är klar. En fas som börjar den 5 oktober med 10 dagar ska vara klar den 15 oktober och tar slut när 16 oktober börjar. Tidsgränsen kontrolleras före inaktiviteten när båda tar slut samma dag.
- **Dagar som tjänsten missar,** till exempel när den har stått still, räknas ändå: kontrollen gäller dagen som börjar, hur många dagar som än gått. Tjänsten startar en sådan dag först när handelsplattformens händelser från före den är hanterade, så att en affär i sista stund räknas.
- **En challenge som väntar på firmans godkännande** tar aldrig slut på tid, eftersom tradern inte har något konto att handla på.
- **Paus:** kontot pausas på handelsplattformen och dagarna till tidsgränsen och inaktiviteten räknas inte. Väntande ordrar tas bort på handelsplattformen. Tradern kan stänga sina positioner, så en fas kan bli klar under pausen. Nästa fas konto pausas då direkt när det öppnas. Vid återupptagandet flyttas tidsgränserna fram med dagarna från pausen, eller från fasens första dag om den började under pausen. Golven, brott, annullering och utbetalningar fungerar som vanligt under pausen.
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
- Tid (`ExpiryRulesTests`): tidsgränsen, en ny tidsgräns per fas, inaktiviteten i varje fas också funded, en ny position som flyttar den, dagar som missats, tidsgränsen före inaktiviteten, challenges utan reglerna och challenges som väntar på firman.
- Paus (`PauseRulesTests`): kontot som pausas och dagar som inte räknas, tidsgränserna som flyttas, upprepad paus, en fas som börjar under pausen, en fas som blir klar under pausen, challenges som tagit slut och utbetalningar under pausen.
- En hel challenge från köp via en paus och en utbetalning till brott på funded-kontot spelas upp och jämförs med facitfilen `Golden/two-step-challenge.jsonl`. Varje ändring av ett beslut syns som en diff. Skapa om facit med `UPDATE_GOLDEN=1` och granska diffen.
- Att regelmotorn aldrig använder flyttal, och att den inte når klocka, slump eller I/O (`BannedSymbols.txt`).

## Begränsningar

- Regelmotorn används av propfirm-tjänsten (se [specen för propfirm-tjänsten](propfirm-tjanst.md)), som gör `StartOfDayFloor` till handelsplattformens `AnchoredFloor`.
- Ingen regel för jämna resultat eller nyhetshandel än.
- Inaktivitet och tidsgräns räknas i hela handelsdagar, så den dag fasen eller pausen började räknas inte.
- Bara hela vinsten kan betalas ut, inte en del av den.
- Skalning av funded-konton kommer senare.
