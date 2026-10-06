# 0051. Orderverktyg i motorn, längre historik och egna verktyg i grafen

- Status: Beslutad
- Datum: 2026-10-06

## Sammanhang

Jämförelsen med TradeLocker, cTrader och TopstepX ([rapporten](../../reports/TradeLocker%20cTrader%20och%20TopstepX%20mot%20Kronant.md)) visade att Kronant Trader saknade verktyg som alla tre har och som traders använder varje dag: att stänga en del av en position eller allt på en gång, break-even med ett klick, trailing stop och att ändra en väntande order. Grafen laddade 500 staplar utan att gå att bläddra bakåt, hade 30 dagars historik och saknade indikatorer och ritverktyg.

[ADR 0007](0007-graf-lightweight-charts.md) planerade att ritverktyg och indikatorer skulle komma med TradingViews Advanced Charts. Den kräver en ansökan och ett godkännande, och handel direkt i grafen ingår i en annan, betald produkt. Våra linjer för positioner, order, stoppar och spöken är byggda på Lightweight Charts och skulle behöva göras om.

## Beslut

### Orderverktygen ligger i motorn

- **Stänga en del:** `ClosePosition` har en valfri volym. En del ska vara en volym som instrumentet tillåter och lämna minst den minsta volymen öppen, annars `InvalidVolume`. Hela volymen är en vanlig stängning. Delen ger händelsen `PositionPartiallyClosed` med delens volym, den kvarvarande volymen, priset, vinsten och provisionen för delen. Resten ligger kvar med samma id, sina stoppar och sin trailing stop.
- **Stänga allt:** `CloseAllPositions` stänger alla kontots positioner, eller en symbols, i samma ögonblick och i den ordning de öppnades. En position vars marknad är stängd eller saknar färskt pris ligger kvar. Går ingen att stänga avvisas kommandot med skälet för den första, och utan positioner med `UnknownPosition`. Väntande ordrar ligger kvar.
- **Ändra en väntande order:** `ModifyOrder` ger en väntande order nytt pris, stop loss, take profit och trailing stop. Allt kontrolleras som för en ny order, och marknaden ska vara öppen. Händelsen är `OrderModified`.
- **Trailing stop:** `PlaceOrder` och `ModifyPosition` har valet `TrailingStop`, och `ModifyOrder` likaså. Stop lossen följer då priset på det avstånd den sätts på: från priset positionen stänger till för en marknadsorder eller ändrade stoppar, och från orderpriset för en limit- eller stoporder. Utan stop loss avvisas valet med `NoStopLoss`. Motorn flyttar stop lossen vid varje pris, före kontrollen av stop loss och take profit, och aldrig bakåt. Flytten ger ingen händelse, som för ett släpande golv: den följer av priserna, och kontot visar var stop lossen är. Avståndet finns med i positionen, ordern och händelserna `PositionOpened`, `OrderPlaced`, `OrderModified` och `PositionModified`. Ändrade stoppar utan valet stänger av trailing stop, så terminalen skickar alltid med det.
- **Break-even** är ingen egen funktion i motorn. Terminalen flyttar stop lossen till öppningspriset med `ModifyPosition`, när priset har rört sig förbi det.
- Ögonblicksbilder sparar volymen och trailing stop. Äldre ögonblicksbilder saknar avståndet och läses som utan trailing stop.

### Propfirm-plattformen läser delstängningar

- Regelmotorn uppdaterar saldot vid en delstängning och räknar positionen som öppen tills den sista delen stängs.
- Handelshistoriken sparar varje del i en egen tabell, en rad per händelse, så att en händelse som läses igen inte räknas två gånger. En position som stängs i delar är en affär: volymen, vinsten och provisionen för delarna läggs till den sista stängningen, och stängningspriset är delarnas genomsnitt viktat med volymen. Saldoändringen för en del heter Closed, som för en hel stängning.

### Grafen

- **Bläddra bakåt:** när tradern bläddrar till nära den första candlen hämtar terminalen 500 äldre med `GET /candles?before=`, tills historiken tar slut. Vyn står kvar där den var.
- **Längre historik:** graferna når 180 dagar bakåt. De senaste 2 dagarna laddas i minutstaplar, de senaste 30 i staplar på 15 minuter och resten i timstaplar, så H1 och längre når 180 dagar. Historikens början sparas med den (`chart_histories.reach`), och når den inte så långt som konfigurationen säger laddas den igen.
- **Egna indikatorer:** glidande medelvärde (SMA), exponentiellt glidande medelvärde (EMA), Bollinger Bands, RSI och MACD, räknade i webbläsaren från bid-candles i grafen. Medelvärden och Bollinger ritas över candles, och RSI och MACD i egna rutor under. Högst 8 åt gången. De sparas i webbläsaren på enheten och gäller alla symboler.
- **Egna ritverktyg:** vågrät linje, trendlinje och rektangel, ritade med klick. En ritning väljs med ett klick, flyttas i en ände eller som helhet, och tas bort med Delete eller papperskorgen, som tar bort alla på symbolen med ett andra klick. Ritningarna sparas per symbol i webbläsaren, vid tider och priser, så att de står kvar på varje tidsram.
- Vi bygger vidare på Lightweight Charts i stället för att vänta på Advanced Charts.

## Konsekvenser

- Traders har verktygen de jämför med i en demo, och skydden ligger i motorn på servern, så de gäller också när terminalen är stängd.
- Firmornas system som läser händelseströmmen behöver förstå `PositionPartiallyClosed`, annars missar de en saldoändring. Vår propfirm-plattform gör det.
- En trailing stop som rör sig syns inte i händelserna, bara i kontot och i den stängning den till slut utlöser.
- Indikatorer och ritningar följer enheten, inte kontot. Att spara dem på kontot hör till nästa steg i rapportens lista, som [ADR 0052](0052-regelboken-varningar-och-storlek-fran-risk.md) byggde.
- Fler indikatorer, ritverktyg och mallar måste vi bygga själva. Lightweight Charts placerar bara hela staplar, så en ritnings tid mellan två staplar räknas om från stapeln före.
- Ett byte till längre historik laddar om den en gång, med fler anrop till prisleverantören. Med Capital.com tog 180 dagar 277 anrop.
