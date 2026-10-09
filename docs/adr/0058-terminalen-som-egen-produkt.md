# 0058. Terminalen som egen produkt: en profil per server och en lugnare terminal

- Status: Föreslagen
- Datum: 2026-10-09

## Sammanhang

Genomgången av terminalen (`reports/Genomgång av terminalen.md`) gick igenom Kronant Trader som trader och hittade 85 punkter där det kunde bli friktion eller osäkerhet. Två saker gick igen. Terminalen var byggd för en propfirma: regelboken, egna spärrar, "% room", rapporten om ett brutet golv och orden "challenge" och "Rules" syntes för alla, och inloggningen visade alla kunders namn för vem som helst. För en mäklare med riktiga pengar, en skola eller ett handelsbord blev orden fel och viktiga delar saknades. Och utseendet var plottrigt: svävande kort, lådor i lådor, många små väljare, versala etiketter, monospace och hjälptext överallt, priser som blinkade som en julgran och tider utan datum.

Ägaren beslutade 2026-10-09 att kunderna inte får egna utseenden på terminalen (ADR 0009 står kvar), att terminalen bara finns på engelska tills vidare, och att allt i genomgången ska rättas i den ordning som rapporten föreslår.

## Beslut

### En profil per server

- **Profilen** (`TerminalProfile`) säger hur en servers terminal fungerar: vilken sorts verksamhet det är (`Prop`, `Broker`, `Practice` eller `Desk`), vilka delar som visas (`rulebook`, `ownLimits`, `riskSizing`, `tradeDetails`, `breachReports`), om en order frågar innan den skickas, hur en ny order börjar (`Smallest`, `Lots` med ett antal lots, eller `RiskOfRoom` med en andel av utrymmet kvar i dag), om traders får logga in i terminalen med lösenord, firmans egna sidor (hjälp, support, villkor, integritet och glömt lösenord) och en riskvarning på inloggningen. Varje sort har sina standardval: en mäklares kunder får en fråga före varje order och ingen regelbok, en propfirmas traders handlar med ett klick. Utseendet är alltid vårt.
- **Var den sätts:** firman sätter den med `GET` och `PUT /api/admin/v1/terminal-profile`, och en firma i konfigurationen med `Tenants:N:Terminal`. Profilen sparas i `tenants.terminal_profile` och kommer till terminalen med servern (`ServerInfo.profile`) vid inloggningen.
- **Terminalen visar bara det profilen slår på:** regelboken och varningarna om firmans regler, de egna spärrarna, storlek från risk, knappen Details i historiken och knappen Breach report. Orden följer sorten: "Rules" eller "Limits", "The challenge goes on." eller "The account stays open." och "Payout" eller "Withdrawal".
- **Kronant Prop** sätter profilen för varje firma som registrerat sig (`TerminalSync`): en propfirma med alla delar, utan lösenord i terminalen eftersom traders loggar in genom portalen, med supportsidan i portalen och firmans villkor från butiken. Firmans egna val, om ordrar frågar först och hur en order börjar, väljer firman på sidan Terminal i adminpanelen och lämnas orörda. Det som senast skickades sparas i `firms.trading_terminal`, så bara ändringar skickas. Firmor i konfigurationen får sin profil från handelsplattformens konfiguration, som deras server.
- **Traderns namn:** firman berättar namnet med `PUT /api/admin/v1/users/{userId}/name`, och terminalen visar initialerna och namnet i användarmenyn. Kronant Prop skickar namnet från portalen och nya namn när tradern eller firman ändrar dem (`traders.trading_name`).

### Inloggning

- Ingen lista över servrar. Den gemensamma sidan tar e-post och lösenord och hittar firman bland dem som tillåter lösenord. Passar de hos flera firmor väljer tradern firma. Den som letar efter sin firmas inloggning söker på namnet, med minst två tecken och högst fem träffar bland listade servrar.
- En firmas egen sida (`/login?server=`) visar firmans logga och namn, länken tillbaka till firmans webbplats, firmans sidor och riskvarningen från profilen. Lösenordsfälten visas bara när profilen tillåter lösenord.

### Ordern

- Market, Limit eller Stop, som i TradeLocker, cTrader och TopstepX. Tradern väljer typen själv, så den som letar efter en limit hittar ordet, och typen byts aldrig tyst om priset passerar nivån medan ordern fylls i. Ett pris på fel sida av marknaden för typen gör att den sidans knapp väntar och säger varför, och knapparna säger vad de skickar, till exempel "Buy limit". Det ersätter det första valet i denna ADR, Market eller Pending där priset avgjorde om ordern blev en limit eller en stop, som var otydligt för den som tänker i ordertyper.
- En ny order börjar på den volym tradern senast valde för symbolen, och annars på det profilen säger, från början den minsta volymen. Storleken är ett fält med enheten bredvid: lots, eller en risk i kontots valuta, i procent av saldot eller i procent av dagens gräns. Stop loss och take profit skrivs som pris, pips eller belopp.
- Spreaden står mellan SELL och BUY. Avstånd heter pips för valutor och metaller och points för resten, både i spreaden, påslaget och stopparna. Villkoren ligger bakom Conditions. Under knapparna står vad som saknas, och sammanfattningen visar marginalen, vad en pip är värd och vad ordern riskerar mot dagens gräns, eller hur många pips gränsen räcker utan stop loss.
- På ett avslutat, pausat eller låst konto byts formuläret mot en tydlig ruta.
- **Ordrar från grafen:** högerklick ger Buy limit, Buy stop, Sell limit eller Sell stop vid priset, med orderpanelens volym. Knappar för köp och sälj i grafens hörn är ett val i Settings och knappen Trade, och fungerar också i helskärm. Alla ordrar, från panelen, knapparna eller högerklick, frågar först när tradern eller profilen valt det. Det ersätter beslutet i ADR 0055 att ordrar från högerklick skickas direkt.

### Grafen och arbetsytan

- Panelerna går kant i kant och dras till sin storlek, som sparas, med färdiga layouter: Standard, Chart in focus och Active trading, som också slår på köp och sälj i grafen. Positionspanelen fälls ned till sina flikar.
- Linjer utanför vyn trycker inte ihop grafen. De visas som taggar i kanten, och ett klick på en tagg tar in dem.
- En rad uppe till vänster med öppning, högsta, lägsta och stängning för candlen under musen, och indikatorerna under med namn och värde, som går att dölja och ta bort.
- Candles, staplar, linje och Heikin Ashi, och veckor och månader från tre års dagshistorik. Pil, Fibonacci och anteckning bland ritverktygen, med egna tooltips. Prislarm med ljud och notis.
- TradingViews logga syns inte i grafen. TradingView nämns med en länk i About, som licensen för Lightweight Charts tillåter.
- Livepriser och äldre candles väntar på symbolens historik, så ett pris som kommer före historiken aldrig hamnar på fel candles.

### Tider och anslutning

- Tider som inte är i dag har sin dag: "Yesterday 21:17" och "3 Oct 06:50". Historiken och händelserna har en rubrik per dag. Tiderna visas i kontots tidszon, eller i datorns eller UTC om tradern valt det.
- En bruten anslutning sägs efter 2 sekunder på en rad överst, "Connection to Demo Firm lost", de levande siffrorna tonas ned och orderknapparna säger att de väntar på anslutningen. "Connected again" står i 3 sekunder. En terminal som aldrig kom fram säger "Cannot reach" efter 5 sekunder.

### Tabellerna

- Positionerna har öppningstid, provision och resultat, och en summa. Close är en egen knapp, och resten (stoppar, break even och att stänga en del) ligger i en meny bredvid. Ändringar görs i en ruta vid raden, och grafen visar de nya nivåerna svagt. Ett val i Settings gör att Close frågar en gång till.
- Ordrar tas bort med "Cancel order".
- Historiken visar Today, This week eller All, en summa med antal affärer och resultat efter provision, och går att spara som CSV.
- Händelser som upprepas i en följd står en gång med hur många gånger. Filter för affärer, kontot och varningar.
- Affärens id och journalens löpnummer står under "For support", med en knapp som kopierar dem.

### Meddelanden

- Högst två rader överst, en för kontot (avslutat eller låst) och en för plattformen (anslutningen, priser som saknas och firmans meddelande), en rad var med resten bakom Details. Inget trycker ned grafen.
- Datorns egna notiser för fyllningar, stängningar, larm och varningar medan terminalen ligger i bakgrunden, som ett val i Settings som frågar webbläsaren om lov.

### Kontoraden och bevakningslistan

- Kontoraden visar tre saker: equity, dagens resultat (equity mot saldot när handelsdagen började) och vad som är kvar till närmaste gräns, firmans eller tradern egen, med en mätare som blir gul och röd. Resten ligger under More med en rad förklaring var. Kontot har sitt läge som ord bredvid namnet, och menyn med alla konton visar läge och saldo, med avslutade sist. Statusraden visar bara server, anslutning och tid. Förklaringar visas i egna tooltips som fungerar med mus, tangentbord och finger.
- Bevakningslistan har fasta kolumner, instrumentets namn under symbolen, sökning på namn och symbol, listor (alla, en kategori, favoriter och egna listor som tradern döper och ordnar genom att dra), sortering på symbol, förändring och spread, spread som valbar kolumn och kurvan bara när listan är bred nog. Priser ändrar bara textfärg en stund, som ett val. Stjärnan och listmenyn syns när musen är över raden, och alltid en stjärna på favoriterna. Under en kort lista står det valda instrumentets villkor.

### Regelboken

- Firmans regler först och de egna spärrarna under. Läget står som färgad text, inga prickar.
- Ett finansierat konto har en rad Payout med traderns andel och om en utbetalning går att begära nu, med en länk till firman. Firmans system berättar det i reglerna (`profitSplitPercent` och `payoutAvailable`), och Kronant Prop skickar dem.
- De egna spärrarna har tre likadana rader, och rutan om lösare gränser i morgon visas bara när en ändring faktiskt är lösare.

### Telefonen

- Equity, dagens resultat och det som är kvar står alltid synliga, och kontot under firman. Köp och sälj står under grafen. Positioner och ordrar visas som kort med resultatet stort och Close på kortet.

### Inställningar, hjälp och utseende

- Settings har flikarna Display (tema, tidszon och prisfärger), Chart, Sounds (med datorns notiser) och Trading (fråga före order, fråga före stängning och köp och sälj i grafen). Varje val har en rad förklaring och resten bakom en informationsknapp.
- Ett ljust tema, ett mörkt (Kronants) och ett som följer datorn. Grafen ritas om i temats färger, och temat sätts innan sidan ritas, så att den aldrig blinkar mörk.
- Kortkommandon: "/" söker i bevakningslistan, 1 till 9 väljer tidsram, Alt+R återställer grafen, Delete tar bort en ritning, Esc avbryter och "?" visar listan. Inget kortkommando lägger en order.
- En kort rundtur första gången, en gång per inloggning, som går att hoppa över och visa igen från användarmenyn. About visar version, server, anslutning, vem som gjort terminalen och TradingViews omnämnande.
- Siffror står i textens typsnitt med lika breda siffror. Monospace bara för id och koder, inga versala etiketter ovanför rubriker och serif bara i ordmärket.

### Det som inte görs

- **Eget utseende per kund** görs inte (ägarens beslut, ADR 0009). Firmans logga och namn syns i kontoraden och på inloggningen.
- **Språk och format:** bara engelska och engelska tal och datum tills vidare.
- **Inbäddning** på en annan webbplats stöds inte. Terminalen skickar `Content-Security-Policy: frame-ancestors 'none'` och `X-Frame-Options: DENY`, så att ingen sida kan lägga sig över dess knappar.
- **Första kunden:** profilen har fyra sorter med egna standardval i stället för att bygga för en kundtyp först.

## Konsekvenser

- Nya skillnader mellan kunder läggs i profilen, inte som undantag i koden. En ny del i terminalen som inte alla behöver får en flagga under `modules`.
- Handelstjänsten fick migreringen `0013_terminal_profiles.sql` (`tenants.terminal_profile`, `users.name`), admin-API:t för profilen och namnet, fälten för utbetalningar i kontots regler och sökningen på servrar. Kontraktet ändrades och terminalens och personalpanelens typer genereras om.
- Kronant Prop fick migreringarna `0033_trading_account_labels.sql` (varje handelskonto får sitt namn en gång, också tidigare faser) och `0034_terminal_profiles.sql` (`firms.trading_terminal`, `traders.trading_name`), `TerminalSync` och sidan Terminal i adminpanelen.
- Ett konto som inte är det som visas hämtas först när kontomenyn öppnas, så dess läge och saldo kan vara några sekunder gamla.
- Historiken och summorna räknas från de senaste 1 000 händelserna, som förut.
- Rundturen och temat följer inloggningen, så en trader som delar dator med en annan får sina egna.
