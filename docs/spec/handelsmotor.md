# Spec: handelsmotorns kärna

- Fas: 1, insättningar och uttag i 5, grupper under drift i 6, pausade konton i 7, ändrade villkor i skapade grupper och omräkning genom USD efter genomgången som ny firma, öppettider och orderverktygen efter jämförelsen med konkurrenterna
- Status: Implementerad i `trading/src/Trading.Engine`
- Datum: 2026-10-06

## Syfte

Kärnan simulerar orderutförande mot riktiga priser. Ingenting skickas ut på marknaden. Den är deterministisk: samma indata ger alltid samma händelser (se [ADR 0005](../adr/0005-deterministisk-handelsmotor.md)). Tjänsten runt kärnan, som byggs i fas 2, sköter prisflöde, lagring, API och tidsstämplar.

## Gränssnitt

- `TradingEngine.Apply(EngineInput)` tar emot en indata och returnerar händelserna den orsakade, i ordning. Ogiltig indata ger händelsen `InputRejected` med en orsak. Motorn kastar aldrig undantag för ogiltig indata.
- `TradingEngine.GetAccount(id)` returnerar kontot värderat till senaste priser. För varje golv anges `Headroom`, alltså equity minus golvets nivå.
- `TradingEngine.GetPrices(groupId)` returnerar senaste priser efter gruppens påslag, och `GetLatestQuotes()` de senaste råa priserna.
- `TradingEngine.GetGroupId(accountId)` returnerar kontots grupp utan att värdera kontot.
- `TradingEngine.GetAccounts(groupIds)` returnerar alla konton i grupperna, värderade till senaste priser och ordnade efter id, till exempel för att se vad en incident gjorde med en firmas konton (ADR 0053).
- `Revaluation` värderar positioner på nytt pris för pris, med samma kod som motorn: `Update(quote)` för varje rått pris och `Equity(balance, positions, currency)` för equity med de priserna, eller null när ett pris eller en växelkurs saknas. Rapporten om ett regelbrott ritar equity med den (ADR 0053).
- `TradingEngine.GetGroup(groupId)` returnerar gruppens villkor, med symbolerna i bokstavsordning.
- `TradingEngine.GetPointValue(accountId, symbol)` returnerar vad en punkt på en lot är värd i kontots valuta: kontraktsstorlek gånger punkt gånger växelkursen från symbolens kursvaluta, samma kurs som vinsten räknas med. Värdet avrundas inte. Null om kontot inte finns, gruppen inte handlar symbolen eller växelkursen saknas.
- `TradingEngine.ExportState()` och `TradingEngine.FromState(configuration, state)` exporterar och återställer hela tillståndet. En återställd motor ger exakt samma händelser som originalet för samma indata.
- Händelser och kommandon som hör till ett konto är märkta med `IAccountEvent` och `IAccountCommand`, så att tjänsten kan fördela dem per konto. Priser och grupper hör inte till något konto.
- Motorn är inte trådsäker. Indata ska tillämpas en i taget och i tur och ordning.

### Indata

| Indata | Beskrivning |
|---|---|
| `Quote` | Rått pris från flödet (bid och ask), före påslag. |
| `CreateGroup` | Skapar en grupp med sina villkor, till exempel för en firma som registrerat sig (ADR 0016). |
| `ChangeGroupSymbols` | Ersätter symbolerna och villkoren i en grupp som skapats med `CreateGroup` (ADR 0027). Valutan och nivån för stop out ändras inte. |
| `CreateAccount` | Skapar ett konto i en grupp med ett startsaldo. |
| `PlaceOrder` | Marknads-, limit- eller stoporder med valfri stop loss, take profit och trailing stop. |
| `ModifyOrder` | Ger en väntande order nytt pris, stop loss, take profit och trailing stop (ADR 0051). |
| `CancelOrder` | Tar bort en väntande order. |
| `ClosePosition` | Stänger en position till aktuellt pris, eller med en volym bara en del av den. |
| `CloseAllPositions` | Stänger alla positioner, eller en symbols, i samma ögonblick. De som inte går att stänga nu ligger kvar. |
| `ModifyPosition` | Sätter eller tar bort stop loss och take profit, och slår på eller av trailing stop. |
| `SetEquityFloor` | Lägger till eller ersätter ett namngivet golv för equity. |
| `RemoveEquityFloor` | Tar bort ett golv. |
| `CloseAccount` | Stänger alla positioner, tar bort alla ordrar och stänger av kontot. |
| `SuspendAccount` | Pausar kontot: tar bort väntande ordrar och tar inte emot nya. Öppna positioner ligger kvar. |
| `ResumeAccount` | Låter ett pausat konto handla igen. |
| `ReopenAccount` | Öppnar ett avstängt konto igen med ett nytt saldo, utan positioner, ordrar eller golv (ADR 0053). |
| `AdjustBalance` | Sätter in ett positivt belopp eller tar ut ett negativt, med ett id för operationen och ett valfritt minsta saldo. |
| `SetTradingDay` | Sätter när kontots handelsdag börjar: en tidszon och en lokal tid (ADR 0054). |
| `SetOwnLimits` | Traderns egna gränser: daglig förlust, dagsmål och antal affärer per dag (ADR 0054). |
| `LockTrading` | Låser nya ordrar till nästa handelsdag, och stänger positionerna eller låter dem ligga (ADR 0054). |

### Händelser

| Händelse | När |
|---|---|
| `GroupCreated` | Gruppen skapades. Innehåller villkoren med symbolerna i bokstavsordning. |
| `GroupSymbolsChanged` | Gruppens symboler och villkor ändrades. Innehåller gruppen som den är nu. |
| `AccountCreated` | Kontot skapades. |
| `OrderPlaced` | En limit- eller stoporder väntar på sitt pris. |
| `OrderModified` | En väntande order fick nytt pris, stop loss, take profit eller trailing stop. |
| `OrderCancelled` | En order togs bort: manuellt, saknad marginal vid utlösning, brott mot golvet, stängt konto, pausat konto, låst dag eller nått tak för affärer. |
| `PositionOpened` | En position öppnades. Innehåller pris, provision och saldo efteråt. |
| `PositionModified` | Stop loss eller take profit ändrades. |
| `PositionClosed` | En position stängdes. Innehåller pris, vinst, provision, orsak och saldo efteråt. |
| `PositionPartiallyClosed` | En del av en position stängdes. Innehåller delens volym, den volym som ligger kvar, pris, vinst, provision för delen och saldo efteråt. |
| `EquityFloorSet`, `EquityFloorRemoved` | Ett golv sattes eller togs bort. |
| `EquityFloorBreached` | Equity föll under ett golv. Innehåller bevisen: golvets nivå, equity, priser och öppna positioner innan de stängdes. |
| `StopOutTriggered` | Marginalnivån föll under gränsen för stop out. |
| `AccountDisabled` | Kontot stängdes av efter brott mot golvet eller på begäran. |
| `AccountSuspended`, `AccountResumed` | Kontot pausades efter att dess väntande ordrar togs bort, eller får handla igen. |
| `AccountReopened` | Ett avstängt konto öppnades igen med saldot. Följs av `EquityFloorRemoved` för varje golv det hade. |
| `BalanceAdjusted` | Pengar sattes in eller togs ut. Innehåller operationens id, beloppet och saldot efteråt. |
| `TradingDaySet` | Kontots handelsdag sattes. Innehåller dagen och när nästa dag börjar. |
| `TradingDayStarted` | En ny handelsdag började på ett konto med egna gränser eller lås. Innehåller startsaldot, gränserna för dagen och när nästa dag börjar. |
| `OwnLimitsSet` | Traderns egna gränser ändrades. Innehåller gränserna som gäller nu och de som väntar till nästa dag. |
| `OwnLimitReached` | Equity nådde den egna förlustgränsen eller det egna dagsmålet. Innehåller vilken, nivån och equity. |
| `TradingLocked` | Nya ordrar låstes till nästa handelsdag. Innehåller orsaken, när låset hävs, gränsens belopp, dagens resultat och hur många positioner låset stängde. |
| `TradingUnlocked` | Låset hävdes, när en ny dag började eller kontot öppnades igen. |
| `InputRejected` | Indata avvisades. Innehåller indatan och orsaken. |

## Beslut

### Hedging

Varje fylld order blir en egen position, och positioner stängs var för sig. Ett konto kan ha både köp- och säljpositioner i samma symbol. Netting kan läggas till som ett eget kontoläge senare.

### Konfiguration

- **Instrument:** symbol, bas- och kursvaluta, kontraktsstorlek (100 000 för forex, 100 för guld), antal decimaler i priset och volymgränser i lots. Index, råvaror och krypto har sig själva som basvaluta, till exempel US100 mot USD, så marginalen räknas med instrumentets eget pris som för guld.
- **Grupp:** kontovaluta, nivå för stop out och villkor per symbol. Villkoren är hävstång, påslag på spread i punkter och provision per lot och sida. En grupp motsvarar en firmas handelsvillkor.
- **Grupper i konfigurationen eller skapade med indata.** Grupper i konfigurationen finns från start. `CreateGroup` skapar fler medan motorn kör, med samma kontroller: kända symboler, varje symbol en gång, hävstång över noll och påslag och provision som inte är negativa. Ett id som redan finns avvisas med `DuplicateId`, och ogiltiga villkor med `InvalidGroup`. Exporterat tillstånd innehåller de skapade grupperna, men inte de konfigurerade. En skapad grupp som senare finns i konfigurationen stoppar återställningen.
- **Ändrade villkor.** `ChangeGroupSymbols` gäller bara skapade grupper. En grupp i konfigurationen avvisas med `GroupNotChangeable`, eftersom konfigurationen bestämmer den. Villkoren kontrolleras som när gruppen skapas (`InvalidGroup`). En symbol som tas bort medan ett konto i gruppen har en position eller en väntande order i den avvisas med `SymbolInUse`. Öppna positioner och väntande ordrar får de nya villkoren direkt: hävstången ändrar marginalen, påslaget priset de värderas till och provisionen det som dras när de stängs. Konton med positioner eller ordrar kontrolleras mot golv och stop out med en gång, som efter ett nytt pris.
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
- **Förankrat golv (`AnchoredFloor`):** ett avstånd under kontot som det är när golvet sätts, antingen saldot eller det högsta av saldo och equity. Nivån ligger sedan fast. Sätts det om vid varje ny handelsdag blir det en gräns för daglig förlust, där nivån tas i samma steg som priserna. Från saldot räknas öppna förluster mot den nya nivån.
- **Brott:** equity strikt under golvets nivå. Equity exakt på nivån är tillåten.
- Vid brott sparas bevisen i händelsen. Därefter stängs alla positioner till senaste pris, alla ordrar tas bort och kontot stängs av. Golven kontrolleras i bokstavsordning efter namn, och det första som bryts anges.
- Ett golv som sätts över nuvarande equity bryts direkt.
- En insättning eller ett uttag är inget handelsresultat. Golv som mäts från kontot följer därför med: ett förankrat golvs startpunkt och ett släpande golvs högsta equity flyttas lika mycket som saldot. Ett fast golv och ett släpande golvs låsnivå ligger kvar.
- Golven kontrolleras vid varje pris och efter varje kommando som ändrar equity, i samma steg som priset behandlas. Ingen fördröjning finns mellan priset och beslutet.

### Valutaomräkning

- Vinst räknas i kursvalutan och räknas om till kontovalutan. Marginal räknas i basvalutan och räknas om på samma sätt.
- Kursen hämtas från ett instrument som har valutaparet, åt något håll. Mittpriset för det råa priset används, utan påslag. Finns paret bara åt motsatt håll används 1 / mittpriset.
- Har inget instrument paret räknas kursen genom USD: kursen till USD gånger kursen från USD, var och en på samma sätt (ADR 0030). Ett konto i EUR som handlar XAUUSD räknar marginalen med XAUUSD och 1 / EURUSD.
- Bara när konfigurationen varken har paret eller en väg genom USD räknas kursen genom en annan valuta, den första i konfigurationens ordning som har båda paren (ADR 0049). Ett USD-konto som handlar indexet DE40, som har EUR som kursvaluta, räknar marginalen med DE40 och EURUSD. Vägen väljs av konfigurationen, inte av vilka priser som finns, så tidigare indata ger samma händelser.
- Saknas kurs avvisas ordern med `NoConversionRate`. Ett USD-konto som handlar EURGBP behöver alltså både GBPUSD (för vinsten) och EURUSD (för marginalen).

### Avrundning

Belopp i kontovalutan avrundas till valutans decimaler med `MidpointRounding.AwayFromZero`. Standard är 2 decimaler, och JPY har 0. Avrundningen sker per position och per bokföring: provision, realiserad vinst, flytande vinst och marginal. Equity är saldot plus summan av de avrundade flytande vinsterna. Startsaldot måste redan vara avrundat.

### Insättningar och uttag

- `AdjustBalance` sätter in ett positivt belopp eller tar ut ett negativt. Beloppet får inte vara noll och ska vara avrundat till kontovalutans decimaler.
- Den som skickar operationen väljer dess id. Ett id kan bara användas en gång per konto, så ett nytt försök efter ett avbrott avvisas med `DuplicateId` i stället för att dras två gånger. Ett id som avvisades av en annan orsak kan användas igen.
- Ett uttag avvisas med `InsufficientFunds` om saldot efteråt blir lägre än `MinBalance` (eller 0), om beloppet är större än den fria marginalen, eller om equity efteråt skulle hamna under något golv.
- Avstängda konton tar inte emot operationer.
- Propfirm-plattformen tar ut en funded traders vinst så här när tradern begär en utbetalning (se [ADR 0015](../adr/0015-utbetalningar-tar-ut-vinsten-direkt.md)).

### Pausade konton

- Ett konto är `Active`, `Suspended` eller `Disabled`. Ett pausat konto kan bli aktivt igen. Ett avstängt konto öppnas bara igen med `ReopenAccount`, när en firma återställer ett steg som en incident avslutade (se nedan).
- Ett pausat konto tar inte emot nya ordrar. `PlaceOrder` avvisas med `AccountSuspended`. Väntande ordrar tas bort när kontot pausas, eftersom de annars skulle öppna positioner.
- Ägaren kan stänga positioner, ta bort ordrar och ändra stop loss och take profit. Golv, stop loss, take profit och stop out gäller som vanligt, och kontot kan stängas och få insättningar och uttag.
- Att pausa ett pausat konto avvisas med `AccountSuspended`, och att återuppta ett aktivt med `AccountNotSuspended`. Ett avstängt konto avvisas med `AccountDisabled`.
- Propfirm-plattformen pausar en firmas konton medan firmans månad är obetald (se [ADR 0020](../adr/0020-forbetalda-platser-for-aktiva-challenges.md)).

### Öppna ett avstängt konto igen

- `ReopenAccount` gör ett avstängt konto aktivt igen med saldot i indatan och tar bort golven det hade, eftersom de hörde till steget som avslutades. Den som öppnar kontot sätter nya golv efteråt. Kontot har inga positioner eller ordrar, eftersom de stängdes eller togs bort när det stängdes av.
- Saldot ska vara över noll och avrundat till kontovalutans decimaler, annars `InvalidAmount`. Ett konto som inte är avstängt avvisas med `AccountNotDisabled`, och ett okänt med `UnknownAccount`.
- Propfirm-plattformen använder det när en firma återställer ett steg som ett brutet golv avslutade under en incident, på samma konto så att historiken hänger ihop (se [ADR 0053](../adr/0053-detaljer-rapporter-och-incidenter.md)).

### Egna spärrar

Se [ADR 0054](../adr/0054-egna-sparrar-for-tradern.md).

- **Handelsdagen:** varje konto har en handelsdag med en IANA-tidszon och en lokal starttid, från början midnatt UTC. `SetTradingDay` byter den, och en okänd tidszon avvisas med `InvalidTradingDay`. Dagen följer klockan också när den byter till eller från sommartid, som öppettiderna. Ett lås flyttas till den nya dagens start.
- **Ny dag:** den första indatan för kontot, eller det första priset, på eller efter nästa dags start börjar dagen. Startsaldot blir saldot då, antalet affärer nollställs, väntande lösare gränser börjar gälla och låset hävs. `TradingDayStarted` kommer bara för konton med gränser eller lås. Ett avstängt konto börjar inga dagar.
- **Gränserna:** `SetOwnLimits` tar en daglig förlustgräns och ett dagsmål i kontovalutan, över noll och avrundade till valutans decimaler, och ett tak för affärer från 1 till 1 000. En tom gräns är avstängd. Annat avvisas med `InvalidLimits`. Varje gräns som är strängare gäller direkt: ett lägre belopp eller tak, eller en gräns där ingen fanns. Varje gräns som är lösare eller stängs av väntar till nästa dag. Att be om gränserna som de är nu tar bort det som väntar.
- **När en gräns nås:** equity på eller under startsaldot minus förlustgränsen, eller på eller över startsaldot plus dagsmålet, ger `OwnLimitReached`. Alla positioner stängs till senaste pris med orsaken `OwnLimit`, väntande ordrar tas bort med `TradingLocked` och kontot låses. En gräns som dagen redan har passerat nås direkt när den sätts. Gränserna kontrolleras efter golven och stop out, så ett pris som bryter ett golv stänger av kontot i stället.
- **Taket för affärer:** varje öppnad position räknas. När taket är nått avvisas `PlaceOrder` med `TradeLimitReached`, och en väntande order som utlöses tas bort med `TradeLimit` i stället för att fyllas.
- **Låset:** `LockTrading` låser kontot till nästa dags start, och stänger med valet varje position som har ett färskt pris, med orsaken `OwnLimit`. Ett låst konto avvisar `PlaceOrder` och ett nytt `LockTrading` med `AccountLocked`. Positioner kan stängas och få nya stoppar. Låset är skilt från kontots status, så ett konto kan vara pausat och låst samtidigt.
- **Insättningar och uttag** flyttar startsaldot lika mycket, så de räknas inte som resultat.
- `ReopenAccount` börjar en ny dag med det nya saldot och häver låset.

### Provision

Provisionen anges per lot och sida i kontovalutan. Den dras från saldot både när positionen öppnas och när den stängs.

### Orderverktyg

Se [ADR 0051](../adr/0051-orderverktyg-och-grafens-verktyg.md).

- **Delstängning:** `ClosePosition` med en volym stänger bara den delen. Delen ska vara en volym som instrumentet tillåter och lämna minst den minsta volymen, annars `InvalidVolume`. Hela volymen är en vanlig stängning. Resten behåller id, stoppar och trailing stop. Vinst och provision räknas för delen.
- **Stäng allt:** `CloseAllPositions` stänger varje position, eller en symbols, till sitt eget pris i den ordning de öppnades. Positioner vars marknad är stängd eller saknar färskt pris ligger kvar. Går ingen att stänga avvisas kommandot med skälet för den första, och utan positioner med `UnknownPosition`. Golven och stop out kontrolleras en gång efteråt.
- **Ändrad order:** `ModifyOrder` kontrolleras som en ny order: pris på rätt sida om marknaden för ordertypen, stoppar på rätt sida om orderpriset, öppen marknad och färskt pris.
- **Trailing stop:** avståndet är det mellan stop lossen och priset positionen stänger till när ordern läggs eller stopparna ändras, eller orderpriset för en limit- eller stoporder. Utan stop loss avvisas valet med `NoStopLoss`. Vid varje pris flyttas stop lossen till det priset minus avståndet för ett köp, plus för en sälj, om det är bättre, före kontrollen av stop loss och take profit. Flytten ger ingen händelse. Ändrade stoppar utan valet stänger av trailing stop.

### Öppettider

- Ett instrument kan ha öppettider (`TradingHours`, se [ADR 0050](../adr/0050-oppettider-per-instrument.md)): marknadens tidszon, öppna perioder varje vecka, till exempel söndag 17:00 till fredag 17:00, och stängda perioder i marknadens lokala tid, till exempel en helgdag. Ett instrument utan öppettider är alltid öppet.
- Perioderna följer marknadens klocka, också när den byter till eller från sommartid. En tid i timmen som klockan hoppar över flyttas till den första tiden som finns, och en tid i timmen som upprepas är den första av de två. Perioder som möts blir en.
- Medan marknaden är stängd avvisas marknadsordrar, väntande ordrar, stängningar och ändringar av stop loss och take profit med `MarketClosed`, före kontrollen av priset. Väntande ordrar kan tas bort.
- Priser som kommer medan marknaden är stängd tillämpas som vanligt: de flyttar equity och kan utlösa stop loss, take profit, väntande ordrar, golv och stop out.
- `TradingHours.IsOpen(tid)`, `NextChange(tid)` och `PeriodsBetween(från, till)` säger om marknaden är öppen, när den nästa gång öppnar eller stänger, och vilka perioder den är öppen. Tjänsten använder dem för att visa öppettiderna.
- Konfigurationen avvisas om tidszonen är okänd, perioder saknas eller överlappar, marknaden aldrig stänger eller en stängd period slutar innan den börjar.

### Skydd mot gamla priser

Marknadsordrar, väntande ordrar, stängningar och ändringar avvisas med `StalePrice` när det senaste priset för symbolen är äldre än den konfigurerade gränsen jämfört med kommandots tidsstämpel. De avvisas med `NoPrice` om inget pris finns. Stängningar som motorn själv gör, vid brott mot golvet eller stängt konto, använder senaste pris även om det är gammalt, också när marknaden är stängd.

### Tid och ordning

- All indata har en tidsstämpel som tjänsten sätter när indatan tas emot. Motorn läser aldrig systemklockan.
- Indata med en tidsstämpel tidigare än föregående avvisas med `OutOfOrder`.
- Händelserna får samma tidsstämpel som indatan som orsakade dem.

### Ordning inom en prisuppdatering

Konton behandlas i den ordning de skapades. Först börjar en ny handelsdag för varje konto vars dag har passerat. Sedan, för varje konto med positioner eller ordrar:

1. Stop loss och take profit för positioner i symbolen, i den ordning positionerna öppnades.
2. Väntande ordrar i symbolen, i den ordning de lades. En position som öppnas här kontrolleras först vid nästa pris.
3. Golv för equity.
4. Stop out. Om positioner stängdes kontrolleras golven igen, eftersom provisionen kan sänka equity.
5. Traderns egna gränser, om kontot inte stängdes av.

## Utanför fas 1

- Swap och rollover.
- Slippage utöver prisgap, och bredare spread vid nyheter och nattetid.
- Netting, giltighetstid för ordrar (alla gäller tills de tas bort), att ändra volymen på en väntande order och minsta avstånd till priset för stop loss.
- Kontroll av att växelkurser är färska.
- Index per symbol. Varje pris utvärderar alla konton med positioner eller ordrar, vilket räcker för små och medelstora firmor.

## Tester

Testerna ligger i `trading/tests/Trading.Engine.Tests`.

- **Beteenden:** ett test per regel ovan, med belopp som är uträknade för hand.
- **Determinism:** samma uppspelning körs två gånger och ska ge identiska händelser.
- **Återställning:** uppspelningen startas om från exporterat tillstånd vid flera punkter och ska ge exakt samma resultat som utan omstart.
- **Facit:** en uppspelning av 3 000 syntetiska EURUSD-priser jämförs med `Golden/replay-eurusd.jsonl`. Uppspelningen innehåller ordrar, stop loss, take profit, golv, en insättning, ett uttag och ett upprepat uttag, brott mot golvet, stop out och stängning av konto. Varje ändring i motorns utdata syns som en diff i facitfilen.
- **Pausade konton (`SuspensionTests`):** väntande ordrar tas bort och positioner ligger kvar, inga nya ordrar, positioner kan stängas och få nya stoppar, stoppar och golv gäller, pengar kan flyttas och kontot stängas, kontot handlar igen efter återupptagandet, upprepade och avstängda konton avvisas, och pausen följer med en återställning.
- **Valutor (`ConversionTests`):** vinst och marginal i en annan valuta än kontots, ett konto i EUR som handlar guld genom USD, ett konto i USD som handlar ett index i EUR genom EUR, och ordrar utan kurs, också genom USD och EUR.
- **Öppettider (`TradingHoursTests` och `MarketClosedTests`):** valutaveckan, CME:s dagliga paus, byte till vintertid i New York och Berlin vid olika datum, timmen som hoppas över och timmen som upprepas, helgdagar som kortar eller delar en period, perioder som möts, när marknaden nästa gång ändras, ogiltiga öppettider, och att stängda marknader avvisar ordrar, stängningar och stoppar med `MarketClosed` före `StalePrice` medan väntande ordrar kan tas bort, priser fortfarande utlöser stop loss, ett stängt konto stängs till senaste pris och instrument utan öppettider alltid är öppna.
- **Orderverktyg (`OrderToolsTests`):** en del stängs och resten ligger kvar, hela volymen är en vanlig stängning, ogiltiga delar och en del som lämnar för lite, delarna summerar till hela positionen, alla positioner eller en symbols stängs på en gång, positioner i en stängd marknad ligger kvar, en ändrad order fylls till sitt nya pris och kontrolleras som en ny, trailing stop för köp och sälj som aldrig går bakåt, som kräver stop loss, som slås på och av, som följer en väntande order från dess pris, och att trailing stop och delar följer med en återställning.
- **Egna spärrar (`OwnLimitsTests`):** den egna förlustgränsen stänger allt och låser till midnatt, nästa dag hävs låset och räknas från saldot den började med, dagsmålet låser medan tradern ligger på plus, firmans golv går före när ett pris bryter båda, en gräns som dagen redan passerat slår till direkt, en strängare gräns gäller direkt och en lösare nästa dag, ogiltiga gränser, taket för affärer som avvisar nya ordrar och tar bort en väntande order som skulle öppna en till, låset med och utan stängning, att ett lås inte stänger till gamla priser, handelsdagen i Stockholm och att en insättning flyttar dagens startsaldo, att ett återöppnat konto inte är låst, att konton utan gränser byter dag utan händelser, och att allt följer med en återställning.
- **Grupper (`GroupTests`):** en skapad grupp handlar med sina egna villkor, id är unika, ogiltiga grupper avvisas, skapade grupper följer med en återställning, ögonblicksbilder från innan grupper kunde skapas går att läsa, en skapad grupp som blivit konfigurerad stoppar återställningen, och ändrade villkor: nya symboler och villkor som gäller öppna positioner direkt, högre hävstång som sänker marginalen, en symbol i bruk som inte kan tas bort, konfigurerade grupper som inte kan ändras, ogiltiga villkor och att ändringen följer med en återställning.
- **Arkitektur:** `BannedSymbols.txt` stoppar klocka, slump och I/O vid bygget, och ett test kontrollerar att kärnan aldrig använder flyttal.

Facit uppdateras efter en avsiktlig ändring med:

```bash
UPDATE_GOLDEN=1 dotnet test --project trading/tests/Trading.Engine.Tests
```

Granska diffen i facitfilen innan den committas.
