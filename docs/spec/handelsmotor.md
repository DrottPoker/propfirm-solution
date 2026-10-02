# Spec: handelsmotorns kärna

- Fas: 1
- Status: Implementerad i `trading/src/Trading.Engine`
- Datum: 2026-10-02

## Syfte

Kärnan simulerar orderutförande mot riktiga priser. Ingenting skickas ut på marknaden. Den är deterministisk: samma indata ger alltid samma händelser (se [ADR 0005](../adr/0005-deterministisk-handelsmotor.md)). Tjänsten runt kärnan, som byggs i fas 2, sköter prisflöde, lagring, API och tidsstämplar.

## Gränssnitt

- `TradingEngine.Apply(EngineInput)` tar emot en indata och returnerar händelserna den orsakade, i ordning. Ogiltig indata ger händelsen `InputRejected` med en orsak. Motorn kastar aldrig undantag för ogiltig indata.
- `TradingEngine.GetAccount(id)` returnerar kontot värderat till senaste priser. För varje golv anges `Headroom`, alltså equity minus golvets nivå.
- `TradingEngine.GetPrices(groupId)` returnerar senaste priser efter gruppens påslag.
- Händelser och kommandon som hör till ett konto är märkta med `IAccountEvent` och `IAccountCommand`, så att tjänsten kan fördela dem per konto.
- Motorn är inte trådsäker. Indata ska tillämpas en i taget och i tur och ordning.

### Indata

| Indata | Beskrivning |
|---|---|
| `Quote` | Rått pris från flödet (bid och ask), före påslag. |
| `CreateAccount` | Skapar ett konto i en grupp med ett startsaldo. |
| `PlaceOrder` | Marknads-, limit- eller stoporder med valfri stop loss och take profit. |
| `CancelOrder` | Tar bort en väntande order. |
| `ClosePosition` | Stänger en position till aktuellt pris. |
| `ModifyPosition` | Sätter eller tar bort stop loss och take profit. |
| `SetEquityFloor` | Lägger till eller ersätter ett namngivet golv för equity. |
| `RemoveEquityFloor` | Tar bort ett golv. |
| `CloseAccount` | Stänger alla positioner, tar bort alla ordrar och stänger av kontot. |

### Händelser

| Händelse | När |
|---|---|
| `AccountCreated` | Kontot skapades. |
| `OrderPlaced` | En limit- eller stoporder väntar på sitt pris. |
| `OrderCancelled` | En order togs bort: manuellt, saknad marginal vid utlösning, brott mot golvet eller stängt konto. |
| `PositionOpened` | En position öppnades. Innehåller pris, provision och saldo efteråt. |
| `PositionModified` | Stop loss eller take profit ändrades. |
| `PositionClosed` | En position stängdes. Innehåller pris, vinst, provision, orsak och saldo efteråt. |
| `EquityFloorSet`, `EquityFloorRemoved` | Ett golv sattes eller togs bort. |
| `EquityFloorBreached` | Equity föll under ett golv. Innehåller bevisen: golvets nivå, equity, priser och öppna positioner innan de stängdes. |
| `StopOutTriggered` | Marginalnivån föll under gränsen för stop out. |
| `AccountDisabled` | Kontot stängdes av efter brott mot golvet eller på begäran. |
| `InputRejected` | Indata avvisades. Innehåller indatan och orsaken. |

## Beslut

### Hedging

Varje fylld order blir en egen position, och positioner stängs var för sig. Ett konto kan ha både köp- och säljpositioner i samma symbol. Netting kan läggas till som ett eget kontoläge senare.

### Konfiguration

- **Instrument:** symbol, bas- och kursvaluta, kontraktsstorlek (100 000 för forex, 100 för guld), antal decimaler i priset och volymgränser i lots.
- **Grupp:** kontovaluta, nivå för stop out och villkor per symbol. Villkoren är hävstång, påslag på spread i punkter och provision per lot och sida. En grupp motsvarar en firmas handelsvillkor.
- **Max ålder på priser:** gäller alla symboler.

### Priser och påslag

Gruppens påslag läggs på det råa priset. Bid sänks med halva påslaget, avrundat nedåt till hela punkter, och ask höjs med resten. Ett påslag på 3 punkter sänker alltså bid med 1 punkt och höjer ask med 2. Priserna ligger därmed alltid på prisrutnätet.

### Fyllningsregler

| Typ | Utlöses när | Fylls till |
|---|---|---|
| Marknad köp | direkt | ask |
| Marknad sälj | direkt | bid |
| Limit köp | ask ≤ limitpriset | ask, alltså limitpriset eller bättre |
| Limit sälj | bid ≥ limitpriset | bid |
| Stop köp | ask ≥ stoppriset | ask, även om priset hoppat förbi |
| Stop sälj | bid ≤ stoppriset | bid |
| Stop loss, köpposition | bid ≤ stop loss | bid |
| Stop loss, säljposition | ask ≥ stop loss | ask |
| Take profit, köpposition | bid ≥ take profit | bid |
| Take profit, säljposition | ask ≤ take profit | ask |

Vid prisgap fylls limitordrar och take profit till ett bättre pris, och stopordrar och stop loss till ett sämre pris. Det motsvarar en riktig marknad. Någon extra slippage läggs inte till i fas 1.

### Validering av ordrar

- Volymen ska ligga inom instrumentets gränser och vara en multipel av volymsteget.
- Alla priser ska vara större än noll och ligga på prisrutnätet.
- En limitorder ska vänta på ett bättre pris än nu och en stoporder på ett sämre: köplimit under ask, säljlimit över bid, köpstop över ask, säljstop under bid.
- Stop loss ska ligga på förlustsidan och take profit på vinstsidan. För marknadsordrar och ändringar jämförs de med stängningspriset (bid för köp, ask för sälj). För väntande ordrar jämförs de med orderpriset.
- Marknadsordrar får inte ha ett pris. Limit- och stopordrar måste ha ett.
- Ett order-id kan bara användas en gång per konto. Positionen får samma id som ordern.

### Marginal

- Marginal = volym × kontraktsstorlek × kurs från basvalutan till kontovalutan / hävstång.
- Marginalen räknas om med aktuella priser vid varje värdering.
- Positioner i motsatt riktning i samma symbol minskar inte marginalen.
- En order fylls bara om marginalen plus provisionen ryms i den fria marginalen (equity minus använd marginal). Väntande ordrar reserverar ingen marginal. Saknas marginal när de utlöses tas ordern bort.

### Stop out

När marginalnivån (equity / använd marginal × 100) faller under gruppens nivå stängs positionen med störst förlust. Vid lika förlust stängs den äldsta först. Därefter räknas nivån om, och stängningen upprepas tills nivån är tillbaka eller inga positioner finns kvar. Nivån 0 stänger av stop out.

### Golv för equity

- Ett konto kan ha flera golv med var sitt namn, till exempel `daily` och `max-loss`. Motorn känner inte till propfirm-begrepp. Regelmotorn i propfirm-plattformen sätter golven.
- **Fast golv:** en nivå i kontovalutan.
- **Släpande golv:** följer den högsta equity som observerats, minus ett avstånd. Golvet slutar stiga vid en valfri låsnivå och sjunker aldrig. Den högsta equityn startar från equity när golvet sätts.
- **Brott:** equity strikt under golvets nivå. Equity exakt på nivån är tillåten.
- Vid brott sparas bevisen i händelsen. Därefter stängs alla positioner till senaste pris, alla ordrar tas bort och kontot stängs av. Golven kontrolleras i bokstavsordning efter namn, och det första som bryts anges.
- Ett golv som sätts över nuvarande equity bryts direkt.
- Golven kontrolleras vid varje pris och efter varje kommando som ändrar equity, i samma steg som priset behandlas. Ingen fördröjning finns mellan priset och beslutet.

### Valutaomräkning

- Vinst räknas i kursvalutan och räknas om till kontovalutan. Marginal räknas i basvalutan och räknas om på samma sätt.
- Kursen hämtas från ett instrument som har valutaparet, åt något håll. Mittpriset för det råa priset används, utan påslag. Finns paret bara åt motsatt håll används 1 / mittpriset.
- Saknas kurs avvisas ordern med `NoConversionRate`. Ett USD-konto som handlar EURGBP behöver alltså både GBPUSD (för vinsten) och EURUSD (för marginalen).

### Avrundning

Belopp i kontovalutan avrundas till valutans decimaler med `MidpointRounding.AwayFromZero`. Standard är 2 decimaler, och JPY har 0. Avrundningen sker per position och per bokföring: provision, realiserad vinst, flytande vinst och marginal. Equity är saldot plus summan av de avrundade flytande vinsterna. Startsaldot måste redan vara avrundat.

### Provision

Provisionen anges per lot och sida i kontovalutan. Den dras från saldot både när positionen öppnas och när den stängs.

### Skydd mot gamla priser

Marknadsordrar, väntande ordrar, stängningar och ändringar avvisas med `StalePrice` när det senaste priset för symbolen är äldre än den konfigurerade gränsen jämfört med kommandots tidsstämpel. De avvisas med `NoPrice` om inget pris finns. Stängningar som motorn själv gör, vid brott mot golvet eller stängt konto, använder senaste pris även om det är gammalt.

### Tid och ordning

- All indata har en tidsstämpel som tjänsten sätter när indatan tas emot. Motorn läser aldrig systemklockan.
- Indata med en tidsstämpel tidigare än föregående avvisas med `OutOfOrder`.
- Händelserna får samma tidsstämpel som indatan som orsakade dem.

### Ordning inom en prisuppdatering

Konton behandlas i den ordning de skapades. För varje konto med positioner eller ordrar:

1. Stop loss och take profit för positioner i symbolen, i den ordning positionerna öppnades.
2. Väntande ordrar i symbolen, i den ordning de lades. En position som öppnas här kontrolleras först vid nästa pris.
3. Golv för equity.
4. Stop out. Om positioner stängdes kontrolleras golven igen, eftersom provisionen kan sänka equity.

## Utanför fas 1

- Swap och rollover.
- Slippage utöver prisgap, och bredare spread vid nyheter och nattetid.
- Delstängning av positioner, netting, giltighetstid för ordrar (alla gäller tills de tas bort) och minsta avstånd till priset för stop loss.
- Insättningar och uttag.
- Ögonblicksbilder, återstart och lagring. Det kommer i fas 2.
- Kontroll av att växelkurser är färska.
- Index per symbol. Varje pris utvärderar alla konton med positioner eller ordrar, vilket räcker för små och medelstora firmor.

## Tester

Testerna ligger i `trading/tests/Trading.Engine.Tests`.

- **Beteenden:** ett test per regel ovan, med belopp som är uträknade för hand.
- **Determinism:** samma uppspelning körs två gånger och ska ge identiska händelser.
- **Facit:** en uppspelning av 3 000 syntetiska EURUSD-priser jämförs med `Golden/replay-eurusd.jsonl`. Uppspelningen innehåller ordrar, stop loss, take profit, golv, brott mot golvet, stop out och stängning av konto. Varje ändring i motorns utdata syns som en diff i facitfilen.
- **Arkitektur:** `BannedSymbols.txt` stoppar klocka, slump och I/O vid bygget, och ett test kontrollerar att kärnan aldrig använder flyttal.

Facit uppdateras efter en avsiktlig ändring med:

```bash
UPDATE_GOLDEN=1 dotnet test --project trading/tests/Trading.Engine.Tests
```

Granska diffen i facitfilen innan den committas.
