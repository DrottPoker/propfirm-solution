# 0054. Egna spärrar för tradern

- Status: Beslutad
- Datum: 2026-10-07

## Sammanhang

Punkt 6 i jämförelsen med TradeLocker, cTrader och TopstepX ([rapporten](../../reports/TradeLocker%20cTrader%20och%20TopstepX%20mot%20Kronant.md)). TopstepX låter tradern sätta strängare gränser för sig själv än firman gör, och traders på andra plattformar ber om samma sak. Det som skiljer en spärr från en föresats är att den inte går att ångra mitt i en dålig dag. Firmans gränser avslutar challengen när de bryts. En egen gräns ska i stället stoppa dagen innan firmans gräns nås, utan att något regelbrott uppstår.

Designen godkändes som en duk med fem vyer: regelboken med egna spärrar, panelen för att ändra dem, dialogen för att låsa resten av dagen, terminalen när en spärr har slagit till och firmans vy i adminpanelen.

## Beslut

### Spärrarna

- Tradern kan sätta en egen daglig förlustgräns, ett eget dagsmål för vinst och ett tak för antal affärer per dag. Förlustgränsen anges i kontovalutan eller i procent av dagens startsaldo, och terminalen räknar om procenten till ett belopp. En tom gräns är avstängd.
- Gränserna räknas från saldot när handelsdagen började. Equity på eller under startsaldot minus förlustgränsen, eller på eller över startsaldot plus dagsmålet, stänger varje position till sitt pris och låser nya order till nästa handelsdag. Taket för affärer räknar öppnade positioner. När det är nått avvisas nya order med `TradeLimitReached`, och en väntande order som utlöses tas bort i stället för att öppna en position. Inget stängs.
- Tradern kan också låsa resten av dagen själv, och välja om positionerna ska stängas eller ligga kvar. Ett låst konto avvisar nya order med `AccountLocked`, men tradern kan stänga positioner och ändra deras stoppar, som på ett pausat konto. Väntande order tas bort när kontot låses.
- Låset går inte att häva före nästa handelsdag, varken av tradern eller firman. Det enda som häver det tidigare är att firman öppnar ett avstängt konto igen ([ADR 0053](0053-detaljer-rapporter-och-incidenter.md)), som börjar en ny dag.
- En strängare gräns gäller direkt. En lösare gräns, eller en som stängs av, gäller från nästa handelsdag. Annars kunde tradern lätta på gränsen mitt i en dålig stund. En gräns som dagen redan har passerat slår till direkt.
- Spärrarna är inte firmans regler. En spärr som slår till avslutar inte challengen, ger inget regelbrott och syns inte i regelmotorn. Firmans golv kontrolleras först, så ett pris som bryter både firmans gräns och en egen följer firmans regel.

### Motorn håller spärrarna

- Spärrarna ligger i motorn, inte i terminalen, så de gäller också när terminalen är stängd eller tradern handlar på ett annat sätt. Nya indata: `SetOwnLimits`, `LockTrading` och `SetTradingDay`. Nya händelser: `OwnLimitsSet`, `OwnLimitReached`, `TradingLocked`, `TradingUnlocked`, `TradingDaySet` och `TradingDayStarted`. Ny orsak för stängning: `OwnLimit`.
- Ett konto har en handelsdag med tidszon och starttid. Den första indatan på eller efter nästa dags start börjar dagen: startsaldot blir saldot då, antalet affärer nollställs, lösare gränser börjar gälla och låset hävs. Saldot ändras bara av indata, så startsaldot blir exakt. Insättningar och uttag flyttar startsaldot lika mycket, så de räknas inte som resultat.
- Låset hålls skilt från kontots status, så att firmans paus och traderns lås kan gälla samtidigt.
- Ett lås som tradern väljer stänger bara positioner med färska priser, som när tradern stänger dem själv. En spärr som slår till stänger som firmans golv, till priset som nådde gränsen.
- `TradingLocked` säger vilken gräns som nåddes och dess belopp, dagens resultat vid låset och hur många positioner låset stängde.

### Handelsdagen följer firmans

- Firmans system talar om när handelsdagen börjar med `PUT /api/admin/v1/accounts/{accountId}/trading-day`. Utan den börjar dagen vid midnatt UTC. Ändras dagen medan kontot är låst flyttas låsets slut till den nya dagens start.
- Propfirm-tjänsten skickar challengens handelsdag när den beskriver kontot för terminalen, samma dag som firmans dagliga förlustgräns följer. Alla öppna konton beskrivs en gång till när tjänsten uppdateras, så att de får sin handelsdag.

### Terminalen

- Regelboken har en del Your own limits ovanför firmans regler, med var varje gräns står i dag och knapparna för att ändra dem och låsa resten av dagen. En lösare gräns som väntar visas med när den börjar gälla.
- Panelen för att ändra gränserna säger vad varje gräns betyder i equity i dag, att den egna förlustgränsen ska vara mindre än firmans och vad som gäller direkt och från nästa dag.
- Dialogen för att låsa säger när låset hävs och hur länge det är dit, och att det inte går att ångra. Valet Keep trading har fokus.
- Medan kontot är låst visar terminalen en ruta under kontoraden med vad som hände och att challengen fortsätter, kontoraden visar Locked until och orderpanelen tar inte emot order. Kontoraden visar också den egna förlustgränsen bredvid firmans.
- Varningarna i hörnet säger till när den egna förlustgränsen närmar sig, när en egen gräns låser dagen och när dagens affärer är slut, med ljud om tradern har det på ([ADR 0052](0052-regelboken-varningar-och-storlek-fran-risk.md)).
- Tiderna för spärrarna visas i handelsdagens tidszon, så att "until 00:00 Stockholm time" är när dagen verkligen börjar.

### Firmans vy

- Firmans adminpanel visar traderns egna gränser på kontots flik Trading: de som gäller i dag, de som väntar till nästa dag, låset och dagarna som låstes de senaste 30 dagarna med dagens resultat. Firman kan se dem men inte ändra dem.
- Propfirm-tjänsten sparar låsen från händelseströmmen som en del av handelshistoriken.

## Konsekvenser

- Tradern kan binda sig till en plan som gäller också när terminalen är stängd, och som ingen kan häva mitt i en dålig dag.
- En spärr ger aldrig ett regelbrott. En trader som sätter sin förlustgräns större än firmans når firmans gräns först, och terminalen avvisar det därför.
- Tradern kan inte heller häva ett lås som var ett misstag. Dialogen säger det innan, och valet Keep trading har fokus.
- Ett konto som inte har fått någon handelsdag räknar dagen från midnatt UTC, vilket kan skilja sig från firmans dag. Propfirm-tjänsten skickar alltid dagen.
- Spärrarna gäller per konto. En trader med flera konton sätter dem på varje.
- TopstepX har fler slag av spärrar, som gränser per symbol och en gräns som följer det högsta saldot. De kan läggas till på samma sätt senare.
