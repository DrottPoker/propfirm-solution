# Spec: handelsterminalen

- Fas: 3a och 3b, insättningar och uttag i historiken i 5, ny layout efter fas 6, pausade konton i 7, inloggning genom firmans portal och kontoraden som i portalen efter genomgången som ny firma, layouten för telefon, resultatet efter provision, Kronants utseende, sammanfattningen i orderpanelen, notiserna och volymen per symbol efter genomgången av UI och UX, öppettider, orderverktygen och grafens verktyg efter jämförelsen med konkurrenterna, och terminalen som egen produkt efter genomgången av terminalen (ADR 0058)
- Status: Implementerad i `trading/terminal`
- Datum: 2026-10-09

## Syfte

Webbgränssnittet där traders handlar på sitt simulerade konto. Terminalen pratar bara med handelstjänsten (se [specen för handelstjänsten](handelstjanst.md)). Den är en egen produkt som olika sorters företag använder: propfirmor, mäklare, skolor och handelsbord. Vad som skiljer dem står i serverns profil (ADR 0058).

## Profilen per server

- **Profilen** (`server.profile`, ADR 0058) kommer med servern vid inloggningen och säger:
  - **Sorten:** `Prop`, `Broker`, `Practice` eller `Desk`. Den väljer orden: knappen och panelen med reglerna heter Rules för en propfirma och Limits för de andra, ett eget lås säger "The challenge goes on." eller "The account stays open.", och pengar ut ur kontot heter Payout eller Withdrawal.
  - **Delarna** som visas: `rulebook` (firmans regler och varningarna om dem), `ownLimits` (traderns egna spärrar), `riskSizing` (storlek från risk), `tradeDetails` (Details i historiken) och `breachReports` (rapporten om ett brutet golv). Terminalen visar bara det som är på.
  - **Fråga före order** och **hur en ny order börjar:** den minsta volymen, ett antal lots eller en andel av utrymmet kvar i dag. Traderns eget val i Settings går före firmans.
  - **Lösenord i terminalen:** av för en firma vars traders loggar in genom firmans portal.
  - **Firmans sidor** (hjälp, support, villkor, integritet och glömt lösenord) och en **riskvarning** för inloggningen. Sidorna står också i användarmenyn.
- Utseendet är alltid vårt (ADR 0009). Firmans logga och namn syns i kontoraden och på inloggningen.

## Inloggning

- **Vårt eget utseende.** Terminalen heter Kronant Trader (ADR 0046), och namnet finns på ett ställe, `productName` i `src/lib/config.ts`.
- **Ingen lista över servrar.** Den gemensamma sidan `/login` tar e-post och lösenord, och handelstjänsten hittar firman bland dem som tillåter lösenord. Passar uppgifterna hos flera firmor svarar tjänsten 409 med firmorna, och tradern väljer. Den som letar efter sin firmas inloggning söker på firmans namn, från två tecken, med högst fem träffar bland listade servrar, och kommer till firmans sida.
- **Firmans sida** `/login?server=nordic-edge` visar firmans logga och namn, "Log in to the trading terminal", länken tillbaka till firmans webbplats och länken "Not with {firma}? Log in elsewhere". Under rutan står firmans sidor och riskvarningen från profilen, och "Trading terminal by Kronant Trader". Lösenordsfälten visas bara när profilen tillåter lösenord. Annars leder sidan till firmans portal.
- **Inloggning genom firmans portal** (ADR 0027). En firma vars server har `loginUrl` loggar in sina traders i sin portal. Portalen öppnar terminalen med en engångslänk. När sessionen i terminalen går ut, eller tjänsten svarar 401, skickas tradern direkt tillbaka till portalen. En trader som loggar ut själv hamnar på inloggningen och skickas inte tillbaka.
- **Inloggning med länk** på `/login/link?token=...&account=...`. Länken fungerar en gång i 2 minuter, tas bort ur adressen när den har använts, och sidan skickar ingen referer.
- **Spärr:** den som inte är inloggad skickas till `/login`. Servern och kontot sparas på enheten.
- **Konto:** terminalen visar det första kontot tradern äger, eller det som anges med `?account=`.
- **Inbäddning** stöds inte. Terminalen skickar `Content-Security-Policy: frame-ancestors 'none'` och `X-Frame-Options: DENY`.

## Utseende

- **Kronants utseende** från `@kronant/design` ([ADR 0047](../adr/0047-ett-gemensamt-designsystem.md)): grafit, varm benvit text och mässing som accent. Köp och vinst är gröna, sälj och förlust röda. Mässing används bara för det valda och för huvudknappen.
- **Tema:** mörkt (Kronants), ljust eller som datorn, valt i Settings. Det ljusa har varma pappersfärger, nästan svart text och lite mörkare mässing, grönt och rött. Färgerna står i `src/app/globals.css` (`:root[data-theme="light"]`) och för grafen i `src/lib/colors.ts`. Grafen ritas om i temats färger när temat byts. Ett litet skript i rotlayouten sätter temat från webbläsarens kopia av inställningarna innan sidan ritas, så att den aldrig blinkar mörk.
- **Typsnitt** med `next/font`: Familjen Grotesk för all text, med lika breda siffror överallt så att de står i kolumner, Instrument Serif bara i ordmärket, och JetBrains Mono bara för id och koder, som en positions id och en färgkod.
- **Kant i kant:** delarna ligger mot varandra med en linje emellan, utan svävande kort och lådor i lådor. Menyer, rutor och notiser har en skugga, så att de svävar över sidan.
- **Inga versala etiketter** ovanför rubriker. Hjälptext är högst en rad, och resten står bakom en informationsknapp.
- **Tooltips** är terminalens egna (`Tooltip`): de visas när musen vilar, vid fokus med tangentbordet och vid ett tryck på en pekskärm, och svävar över sidan så att en rad som rullar i sidled inte klipper dem.
- **Rörelse:** knappar tonar vid hovring och trycks ned en pixel vid klick. Med minskad rörelse i datorns inställningar står allt still.
- **Fokus med tangentbordet** syns som en mässingsfärgad ring.
- **Ikonerna** kommer från Phosphor, som i portalen.

## Arbetsytan

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Firma / Back  Konto ▾  Equity  Today  Left today ▬▬▬    More ▾  Rules  Ini.  │
├──────────────────────────────────────────────────────────────────────────────┤
│ (en rad för kontots läge och en för plattformen, när det finns något)         │
├────────────────┬────────────────────────────────────────┬───────────────────┤
│ Lista ▾    ⋯   │ Symbol Bid 24h Hög Låg Spread          │ EURUSD   Villkor  │
│ Sök            │ Tidsramar Typ Indikatorer Rita  Trade ⛶│ Market|Limit|Stop │
│ Symbol Bid Ask │ SELL 0.10 BUY  O H L C                  │ Volym  Lots ▾     │
│ Namn       24h │                                        │ SL     TP   Enhet │
│                │ Graf                                   │ SELL  0.9  BUY    │
│ (villkor under │                                        │ Marginal, 1 pip,  │
│  en kort lista)├────────────────────────────────────────┤ risk              │
│                │ Positions Orders History Events Alerts │                   │
└────────────────┴────────────────────────────────────────┴───────────────────┘
│ Kronant Trader  Server  Live  Tid                                 Layout ▾   │
└──────────────────────────────────────────────────────────────────────────────┘
```

- **Panelerna** dras till sin storlek i linjerna mellan dem, och storlekarna sparas på inloggningen. Positionspanelen är bara så hög som det den visar, upp till den valda höjden, och fälls ned till sina flikar. Grafen behåller alltid en minsta höjd.
- **Layouter** i statusraden: Standard, Chart in focus (bevakningslistan dold och positionerna nedfällda) och Active trading (bredare orderpanel, mer plats för positioner och köp och sälj i grafen). Menyn döljer och visar också bevakningslistan och positionerna.
- **Statusraden** visar bara Kronants märke och Kronant Trader, firmans server och anslutningen, tiden med zonens namn och layouten.

## Kontoraden

- **Firman** med logga eller namn och länken "Back to {firma}" till kontot i firmans portal.
- **Kontot** med namnet firmans portal gav det, till exempel "#1001 Two-step 100K, Phase 1" (ADR 0035), och läget som ord bredvid: Ended i rött, Paused i gult eller Locked today. Har tradern flera konton öppnar en meny med alla konton, deras läge och saldo, med avslutade sist. Kontonas läge och saldo hämtas när menyn öppnas.
- **Tre siffror:** Equity, Today, alltså vad kontot har tjänat eller förlorat sedan handelsdagen började (equity mot saldot vid dagens start, med andelen när den inte är noll), och vad som är kvar till den närmaste gränsen, firmans eller traderns egen, till exempel "Left today 4,845.36", med en mätare som blir gul under 25 % och röd under 10 % när gränsen har ett avstånd. Varje siffra har en förklaring i en tooltip.
- **More** öppnar en ruta med saldot, equity, använd och fri marginal, marginalnivån, vinstmålet med vad som är kvar, varje förlustgräns med nivån och vad som är kvar, och den egna dagliga gränsen, med en förklaring vid varje.
- **Rules** (eller Limits) öppnar regelboken (se Regelboken). Knappen blir gul eller röd när en regel behöver uppmärksamhet.
- **Användarmenyn** bakom initialerna, från namnet firman har berättat eller annars från e-postadressen, visar namnet, e-postadressen och servern, och Back to {firma}, Settings, Keyboard shortcuts, Take the tour, About, firmans sidor från profilen och Log out.

## Meddelanden överst

- **Högst två rader** under kontoraden, kant i kant, en rad var: en för kontot och en för plattformen. Resten av texten står bakom Details, i en ruta som svävar över sidan, så att grafen aldrig trycks ned. Finns flera meddelanden för plattformen visar raden det viktigaste och Details alla.
- **Kontot:** "Trading on this account has ended" med varför, till exempel "The daily loss limit was broken on 3 Oct at 06:50:57 UTC: equity 94,956.04 fell below 95,000.00. Every position was closed at those prices.", knappen Breach report och länken till kontot hos firman. Eller traderns eget lås, till exempel "Your own daily loss limit was reached at 16:32:08 Stockholm time".
- **Plattformen,** i den ordningen:
  - **En bruten anslutning** efter 2 sekunder: "Connection to Demo Firm lost" med "Trying again. Prices and the account are not updating, and new orders wait until it is back." De levande siffrorna (kontots siffror, priserna, grafens huvud och resultaten) tonas ned, och orderknapparna säger att de väntar på anslutningen. När den är tillbaka står "Connected again" i 3 sekunder. En terminal som aldrig kom fram säger "Cannot reach Demo Firm" efter 5 sekunder.
  - **Priser som saknas** (ADR 0053): "No new prices since 08:12 UTC, 4 min ago", med vad det betyder för ordrar bakom Details.
  - **Firmans meddelande** (ADR 0053): rubriken och första stycket, med alla stycken och länken till statussidan bakom Details.

## Bevakningslistan

- **Listor** i menyn vid rubriken: All instruments, en kategori (Forex, Metals, Indices, Commodities och Crypto, de som gruppen har, ADR 0049), Favorites och traderns egna listor. En egen lista skapas med New list och ett namn, döps om och tas bort med en fråga. Instrument läggs i en lista från menyn på raden. Valet av lista sparas på inloggningen.
- **Sökning** på symbol och namn: "gold" hittar XAUUSD och "euro" EURUSD.
- **Fasta kolumner:** symbolen med instrumentets namn under, bid, ask, valfri spread i pips, förändringen över 24 timmar och en liten kurva över samma tid när listan är bred nog. Är listan smal och spreaden vald visas spreaden i stället för förändringen. Kurvan ritas först när tjänsten har minst två timmar av priser för symbolen.
- **Priser** byter bara textfärg en stund, grönt när bid steg och rött när det föll, och går tillbaka. Valet "Colour prices as they move" stänger av det.
- **Sortering** på symbol, förändring och spread genom att trycka på rubriken: först högst, sedan lägst, sedan listans egen ordning. Favoriterna och de egna listorna har traderns ordning, som raderna dras till.
- **Stjärnan** och menyn för listor syns i radens högra kant, över förändringen, när musen är över raden eller vid fokus, så att symbolen och priserna alltid syns och symbolen går att klicka på. En favorit har alltid en liten stjärna efter symbolen.
- **Den valda raden** har en tonad bakgrund och en mässingsfärgad kant.
- **En kort lista,** åtta instrument eller färre, har det valda instrumentets villkor och öppettider under sig.
- Terminalen öppnar på EURUSD när gruppen har den, annars på den första symbolen.

## Grafen

- **Grafens huvud** visar symbolen, bid, förändringen över 24 timmar, högsta och lägsta bid under samma tid och spreaden i pips (points för index, råvaror och krypto). Är grafen smal visas bara symbolen, bid och förändringen i procent.
- **Verktygsraden:** tidsramarna M1 till MN (en lista när raden är smal), typen (candles, staplar, linje eller Heikin Ashi), Indicators, ritverktygen, papperskorgen, prislarm, Volume, Trade och helskärm.
- **Raden uppe till vänster** visar öppning, högsta, lägsta och stängning för candlen under musen, med förändringen, och under den varje indikator med namn, period och värde, med knappar för att dölja och ta bort den.
- **Linjer utanför vyn** trycker inte ihop grafen. Ligger en stop loss eller take profit utanför visas en tagg i kanten med pil, och ett klick på den tar in linjerna tills grafen återställs.
- **Tidsaxeln** klipper aldrig den första etiketten, och grafen bläddrar aldrig förbi den första candlen. Med få candles, som på en månadsgraf, fyller candlarna bredden.
- **Bläddra bakåt** (ADR 0051): nära den första candlen hämtas 500 äldre, tills tjänstens historik tar slut. Livepriser och äldre candles väntar på symbolens historik, så ett pris som kommer före historiken eller från en nyss vald symbol aldrig hamnar på fel candles.
- **Indikatorer** (ADR 0051): SMA, EMA, Bollinger Bands, RSI och MACD, högst 8, sparade på inloggningen. RSI och MACD har egna rutor vars höjd dras.
- **Ritverktyg** (ADR 0051): vågrät linje, trendlinje, rektangel, pil, Fibonacci och anteckning, i en meny med namn och förklaring. Ritningarna sparas per symbol på inloggningen.
- **Prislarm** från knappen med klockan, högerklick eller bevakningslistan, med ljud och notis när priset nås, också från datorn när terminalen ligger i bakgrunden. Larmen syns som prickade linjer och listas under fliken Alerts.
- **TradingViews logga** syns inte i grafen. TradingView nämns med en länk i About, som licensen för Lightweight Charts tillåter.
- **Tickvolymen** är ett lågt band längst ned, och den döljs där historiken saknar tickvolym.
- **Affärer i grafen:** pilar där positionerna öppnades och stängdes, med en streckad linje emellan.
- **Dra stop loss och take profit:** linjerna för en öppen positions stoppar och en väntande orders pris och stoppar dras till en ny nivå. Linjen stannar minst en punkt från priset positionen stänger till. Esc avbryter, och ett nej från motorn flyttar tillbaka linjen med skälet i grafen.
- **Spöken:** ordern som fylls i, eller en position eller order som ändras i tabellerna, visas med streckade linjer i halv styrka, till exempel "Buy SL -100.00".
- **Högerklick** öppnar en meny vid priset: Reset chart (Alt+R), New order med Buy limit eller Buy stop och Sell limit eller Sell stop vid priset med orderpanelens volym, efter var priset ligger mot marknaden, Order panel med Use as stop loss och Use as take profit (eller Remove på ett spöke), Chart med Alert at this price och Horizontal line here, och en rad per öppen position med den stopp som passar på den sidan av priset. Kan ingen order läggas säger menyn varför, till exempel att marknaden är stängd. Menyn placeras inom fönstret när den öppnas och står sedan still: korsar priset nivån växer eller krymper den bara nedtill, och det som inte får plats på en låg skärm rullar.
- **Köp och sälj i grafen** (valet Buy and sell on the chart, knappen Trade): SELL och BUY med priserna och volymen emellan i grafens hörn, också i helskärm. De skickar marknadsordrar utan stoppar med orderpanelens volym. Pilarna upp och ned stegar volymen.

## Orderpanelen

- **Rubriken** är symbolen och instrumentets namn, med Conditions som visar hävstången, påslaget på spreaden i pips, provisionen, kontraktet, vad en pip är och öppettiderna.
- **Market, Limit eller Stop.** Tradern väljer typen. En limit väntar på ett bättre pris än marknadens, under ask för ett köp och över bid för en sälj, och en stop på ett sämre, för att följa en rörelse. Ett pris passar bara den ena sidan: den andra knappen väntar och säger på hover varför, till exempel "A sell limit waits above the market price, now 1.08001.", utan att något sägs under knapparna så länge den första sidan går att skicka. Knapparna säger vad de skickar, till exempel "Buy limit" och "Sell stop". Ett tomt prisfält visar Price, och ett pris valt i grafen gör en marknadsorder till en limit. Högerklicksmenyn i grafen ger i stället den typ som passar vid nivån, med typen utskriven på varje rad.
- **Volymen** börjar på den tradern senast valde för symbolen, och annars på det profilen säger, från början den minsta volymen, så att guld inte börjar på 1 lot. Fältet har enheten bredvid: Lots, eller en risk i kontots valuta, i procent av saldot eller i procent av dagens gräns (när profilen har storlek från risk). Med en risk räknas volymen fram från stop lossen.
- **Stop loss och take profit** skrivs som pris, pips eller belopp, med enheten bredvid rubriken. Pips och belopp räknas från där ordern öppnar.
- **SELL och BUY** med priserna och spreaden i pips emellan. Under dem står vad som saknas eller är fel, till exempel "Enter the price the order waits for."
- **Vad ordern innebär:** marginalen, vad en pip är värd, och med stop loss vad ordern riskerar mot dagens gräns, till exempel "Risks 20.00 USD, 0.4% of today's limit", med en stapel. Utan stop loss: hur många pips dagens gräns räcker vid volymen. En stop loss på fel sida sägs bara när volymen och priset är kända.
- **Varför inga ordrar tas emot,** med rätt skäl: ingen anslutning, väntar på priser, en stängd marknad med när den öppnar, ett för gammalt pris (ADR 0053), traderns eget lås eller dagens affärer (ADR 0054). En halvtimme innan marknaden stänger varnar panelen. Ett avslutat eller pausat konto får en ruta i stället för formuläret.
- **Fråga först:** med valet i Settings, eller profilens standard tills tradern valt, visar panelen ordern och dess stoppar i stället för knapparna och skickar den på Confirm. Samma fråga gäller köp och sälj i grafen och ordrar från högerklick.

## Tabellerna

- **Positioner** med symbol, sida, volym, när den öppnades, öppningspris, pris, stop loss och take profit med resultatet där, provisionen vid öppningen och resultatet, och under dem antalet och summan. Close är en egen knapp, och menyn bredvid har Stop loss and take profit, Break even och Close part, med varför ett val inte går. Stopparna och delen ändras i en ruta vid raden, med stoppar som pris, pips eller belopp och grafens spöken. Valet "Ask before closing a position" gör att Close frågar en gång till. Close all frågar alltid.
- **Ordrar** med ordern, till exempel "Buy limit", volym, pris, stoppar och när den lades, och Edit (en ruta vid raden med pris, stoppar och trailing stop) och Cancel order.
- **Historik** med Today, This week (från måndag) och All, en rubrik per dag, antalet affärer och resultatet efter provision, och Export CSV med raderna som visas. Insättningar och uttag står med ord, till exempel "Payout of 500.00". Details öppnar affärens detaljer när profilen har dem.
- **Händelser** med en rubrik per dag och filter för All, Trades, Account och Warnings. En händelse som upprepas i en följd står en gång med "4 times since 21:15:56".
- **Alerts** listar traderns prislarm.
- **Tider** som inte är i dag har sin dag: "Yesterday 21:17" eller "3 Oct 06:50". Tider i dag står som klockslag.
- **På en telefon** visas positioner och ordrar som kort: symbolen, sidan, volymen och resultatet stort, och Close och menyn på kortet.

## Regelboken

- **Firmans regler först** (ADR 0052): vinstmålet, förlustgränserna med vad som är kvar, handelsdagarna, när steget måste vara klart, när en ny position senast måste öppnas och bästa dagens andel mot konsekvensregeln. Läget står som färgad text: grått, gult (nära), rött (akut) eller grönt (klart). Varje rad har en förklaring bakom en informationsknapp.
- **Utbetalning:** ett finansierat konto har raden Payout, till exempel "80% of the profit" med "You can ask for one now" och länken "Ask for it at {firma}", eller "Not yet". Firmans system berättar andelen och om en utbetalning går att begära (`profitSplitPercent` och `payoutAvailable`).
- **De egna spärrarna** under (ADR 0054), med Set your limits eller Change your limits och Lock the rest of the day.
- **Varningar** (ADR 0052) i en notis och med ljud när en gräns kommer nära eller bryts, en tidsgräns närmar sig, bästa dagen går över konsekvensregeln eller målet nås. Utan regelboken i profilen varnar bara förlustgränserna och de egna spärrarna.

## Egna spärrar

- Bladet Your own limits har tre likadana rader: den dagliga förlustgränsen i kontots valuta eller procent, dagsmålet och antal affärer per dag, med vad var och en betyder i dag. Tomt är av. En strängare gräns gäller direkt och en lösare från nästa handelsdag, och det står bara när en ändring faktiskt är lösare.
- Lock the rest of the day låser nya ordrar till nästa handelsdag, med valet att stänga positionerna eller låta dem vara.

## Telefonen

- Under 1 024 px visas en del i taget, vald i flikarna längst ned (Chart, Trade, Watchlist, Positions).
- Kontoraden har kontot under firman och Equity, Today och vad som är kvar på en fast rad som alltid syns. Resten ligger under More.
- Under grafen står SELL, volymen och BUY, så att en affär inte kräver fliken Trade. En order som ska frågas om visar frågan i raden.
- Positioner och ordrar visas som kort. Notiserna kommer överst.

## Inställningar och hjälp

- **Settings** (ADR 0055) från användarmenyn, med en rad förklaring vid varje val och resten bakom en informationsknapp:
  - Display: Theme (Dark, Light, As the computer), Time zone (kontots, datorns eller UTC) och Colour prices as they move.
  - Chart: Candle colors, Volume under the candles, Grid lines, Ask price line och Trades on the chart.
  - Sounds: volymen, Sound on fills, Sound on closes, Sound on warnings och Notifications from the computer, som frågar webbläsaren om lov och säger till när den nekar.
  - Trading: Ask before placing an order, Ask before closing a position och Buy and sell on the chart.
- **Inställningar sparas** (ADR 0052) i webbläsaren och på inloggningen och följer tradern till andra enheter: favoriterna, de egna listorna, volymen per symbol, valen i Settings, indikatorerna, ritningarna, larmen, layouten, rundturen och valet av storlek.
- **Kortkommandon:** "/" söker i bevakningslistan, 1 till 9 väljer tidsram i ordning, Alt+R återställer grafen, Delete tar bort den valda ritningen, Esc avbryter och "?" visar listan. De gäller inte medan ett fält skrivs i, och inget kortkommando lägger en order.
- **Rundturen** visas första gången på en inloggning: orderpanelen, grafen, kontot och regelboken, ett kort i taget med en ring kring delen, som inte täcker sidan. Den hoppas över med Skip och visas igen med Take the tour.
- **About** visar version, server, anslutning, att Ludware gjort terminalen och TradingViews omnämnande med länk.

## Notiser och ljud

- **Notiser** när en order fylls, en väntande order läggs eller en position stängs, och avvisningar i rött i 8 sekunder, med skälet i klartext, också när tradern skickat för mycket på kort tid ("too many requests at once, wait a moment and try again", ADR 0059). De kommer in nere till vänster över grafens äldsta candles, så att de aldrig täcker orderpanelen, det senaste priset eller positionerna. På en telefon kommer de överst.
- **Datorns egna notiser** för samma saker, larm och varningar när terminalen ligger i bakgrunden, om tradern slagit på dem.
- **Ljud** med Web Audio API i traderns volym.

## Dataflöde

| Data | Källa | Uppdatering |
|---|---|---|
| Servern med profilen, traderns namn, kontona och deras namn, vinstmål, tidszon och adress i portalen | `GET /api/auth/me` | Vid inloggning |
| Instrument, villkor och namn | `GET /instruments` | En gång |
| Värdet av en punkt | `GET /instruments/{symbol}/point-value` | Var 10:e sekund |
| Öppettider | `GET /market-hours` | En sekund efter att nästa marknad öppnar eller stänger, minst varje timme och efter en återanslutning |
| Candles | `GET /candles/{symbol}` | Vid byte av symbol eller tidsram, efter en återanslutning och när tjänsten fyllt ett glapp (ADR 0056). Äldre med `before`. |
| 24 timmar per symbol | `GET /candles/{symbol}?timeframe=M15&count=100` | Var 5:e minut och efter en återanslutning |
| Priser | SignalR `Prices` | Högst var 100:e ms |
| Kontot | SignalR `Account` | Högst var 250:e ms |
| Andra konton i kontomenyn | `GET /api/accounts/{id}` | När menyn öppnas, högst var 30:e sekund |
| Händelser | De senaste 1 000 från `GET /events`, därefter SignalR `Events` | Direkt |
| Kontots regler | `GET /rules`, därefter SignalR `Rules` | Direkt |
| Inställningar | `GET /api/me/settings`, `PUT` och `DELETE /api/me/settings/{key}` | Hämtas före start, och en ändring skickas en sekund senare |
| Kommandon | `POST /orders`, `PUT /orders/{id}`, `DELETE /orders/{id}`, `POST /positions/{id}/close`, `POST /positions/close-all`, `PUT /positions/{id}/stops` | Svaret innehåller händelserna |

- **Gränssnittet räknar aldrig kontots pengar.** Equity, vinst, marginal och avståndet till golven kommer från motorn. Dagens resultat är equity minus saldot vid dagens start, båda från motorn. Marginalen, värdet av en pip, risken och volymen från en risk är uppskattningar inför en order.
- **Order-id skapas i webbläsaren** (UUID), så att ett anrop som skickas igen inte kan lägga ordern två gånger.
- **Återanslutning:** SignalR återansluter direkt och sedan varannan sekund. Efter en återanslutning hämtas graferna, öppettiderna, reglerna och de missade händelserna igen.

## Typer från API:t

1. Handelstjänsten genererar kontraktet `contracts/trading/trading-service.json` när den byggs.
2. `pnpm generate:api` genererar `src/lib/api/schema.ts` från dokumentet.
3. CI kontrollerar att båda filerna är committade och aktuella.

## Teknik

- Next.js 16, React 19, TypeScript och Tailwind CSS.
- TradingView Lightweight Charts för grafen (se [ADR 0007](../adr/0007-graf-lightweight-charts.md)), utan logga i grafen och med omnämnandet i About.
- Zustand för livedata och TanStack Query för anrop.
- `@kronant/design`, `next/font`, `@phosphor-icons/react` och `sonner`.
- `openapi-fetch` för typade anrop och `@microsoft/signalr` för realtid.

## Begränsningar

- Bara engelska, med engelska tal och datum.
- Lägre tidsramar har kortare historik: M1 och M5 når 2 dagar bakåt, M15 och M30 30 dagar, H1 och H4 180 dagar och D1, W1 och MN tre år.
- Varje rad i bevakningslistan hämtar sina egna candles för de senaste 24 timmarna. Med många symboler behövs en samlad sammanfattning från tjänsten.
- Historiken, summorna och exporten räknas från de senaste 1 000 händelserna.
- Ändras samma inställning på två enheter samtidigt vinner den som skickas sist.
- Candles börjar på hela timmar och dygn i UTC, och bara etiketterna visas i den valda tidszonen.
- Kontots namn, vinstmål och adress i portalen kommer med inloggningen, så ett konto som öppnas medan terminalen är öppen visas med sitt id tills sidan laddas om.

## Tester

- Enhetstester med Vitest (`pnpm test`) för bland annat sammanfattningen i orderpanelen, ordertyperna efter priset, pips och points, startvolymen från profilen, stoppar som pris, pips och belopp, storleken från risk, tider med dag och rubriker per dag, historikens perioder, summor och CSV, händelser som upprepas och deras ämnen, bevakningslistans listor, sökning, sortering och ordning, kontorads siffror (dagens resultat och närmaste gräns), initialer från namnet, regelboken med utbetalningar, varningarna, de egna spärrarna, temat och tidszonen, kortkommandona, inloggningen utan server, larmen, ritningarna, indikatorerna och de sparade inställningarna.
- Tester av hela flödet med Playwright (`pnpm e2e`) mot en egen tjänst på port 5121 och en egen terminal på port 3021 mot databasen `trading_e2e`, som töms före varje körning. De täcker inloggningen utan server och firmans egen inloggning, en affär från köp till historik med period och CSV, stoppar som belopp och i pips och dragna på grafen, högerklicksmenyn med nya ordrar, ordrar från grafens knappar som frågar först, ordersammanfattningen och volymen per symbol, kontoraden med mål och gränser och ett avslutat konto, bevakningslistans sökning, favoriter och egna listor, flera konton, inloggningslänken från portalen, delstängning och trailing stop, väntande ordrar, indikatorer, ritningar och kortkommandon, regelboken och dess varningar, egna gränser och låsning av dagen, storlek från risk, inställningarna med tema och fråga före order, inställningar på en annan enhet, en affärs detaljer, rapporten om en bruten gräns, firmans meddelande och en förlorad anslutning. Guiden hoppas över i början av varje test, och ett fel som sidan inte fångar fäller testet.
- Lint, typkontroll och bygge i CI.
