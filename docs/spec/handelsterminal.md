# Spec: handelsterminalen

- Fas: 3a och 3b, insättningar och uttag i historiken i 5, ny layout efter fas 6, pausade konton i 7, inloggning genom firmans portal och kontoraden som i portalen efter genomgången som ny firma
- Status: Implementerad i `trading/terminal`
- Datum: 2026-10-05

## Syfte

Webbgränssnittet där traders handlar på sitt simulerade konto. Terminalen pratar bara med handelstjänsten (se [specen för handelstjänsten](handelstjanst.md)).

## Inloggning

- **Vårt eget utseende.** Terminalen är vårt varumärke och inte white label (ADR 0009). Den heter Kronant Trader (ADR 0046), och namnet finns på ett ställe, `productName` i `src/lib/config.ts`.
- **Inloggning** på `/login` med server, e-post och lösenord, som i MetaTrader och TradeLocker. Servrarna hämtas från `GET /api/servers` och visas med firmans namn. En server som inte listas, till exempel en firma i sandlådan, hämtas med `GET /api/servers/{id}` när den finns i länken eller användes senast.
- **Inloggning genom firmans portal** (ADR 0027). En firma vars server har `loginUrl` loggar in sina traders i sin portal, eftersom de inte har något lösenord till terminalen. Sidan visar då knappen "Log in through {firma}" till `loginUrl` med kontot som senast var öppet (`?account=`), och länken "I have a password for the terminal" för den som har ett. Portalen öppnar terminalen igen med en engångslänk. När sessionen i terminalen går ut, eller tjänsten svarar 401, skickas tradern direkt tillbaka till portalen, som loggar in igen utan att tradern märker något om den fortfarande är inloggad där. En trader som loggar ut själv hamnar på inloggningen och skickas inte tillbaka.
- **Inloggning med länk** på `/login/link?token=...&account=...`. Firmans portal skapar länken via admin-API:t. Länken fungerar en gång i 2 minuter, tas bort ur adressen när den har använts, och sidan skickar ingen referer.
- **Vald server:** i första hand den i länken från firmans portal (`/login?server=nordic-prop`), sedan den som senast användes på enheten och annars den enda som finns. Servern och kontot sparas på enheten när tradern loggar in eller byter konto.
- **Spärr:** den som inte är inloggad skickas till `/login`.
- **Konto:** terminalen visar det första kontot tradern äger, eller det som anges med `?account=` om tradern äger det. Har tradern flera konton, till exempel ett per fas i en challenge, byter den konto i listan i kontoraden. Kontot visas med namnet firmans portal gav det, till exempel "#1001 Two-step 100K · Phase 1" (ADR 0035), och kontots id på handelsplattformen syns när musen vilar på det. Ett konto utan namn visas med sitt id.
- **Utloggning** finns i menyn bakom traderns initialer längst till höger i kontoraden. Menyn visar också e-postadressen och servern.

## Layout

```
┌────────────────────────────────────────────────────────────────────────┐
│ Firma  Live prices  Konto  Saldo  Equity  Marginal  Mål  Gränser  Ini. │
├────────────────┬───────────────────────────────────┬───────────────────┤
│ Watchlist      │ Symbol Bid 24h Hög Låg Spread     │ Trade EURUSD      │
│ Sök            │ Tidsramar               Helskärm  │ Market Limit Stop │
│ Alla  Grupper  │                                   │ Volym        - +  │
│ Favoriter      │ Graf: candles och tickvolym,      │ SL - +    TP - +  │
│ ★ Symbol  Bid  │ linjer för positioner, SL och TP  │ SÄLJ    |    KÖP  │
│ Ask 24h Kurva  │                                   │ Villkor           │
├────────────────┴───────────────────────────────────┴───────────────────┤
│ Positioner │ Ordrar │ Historik │ Händelser                             │
├────────────────────────────────────────────────────────────────────────┤
│ Server  Tid i kontots zon        Equity  Marginal  Fri marginal  Nivå  │
└────────────────────────────────────────────────────────────────────────┘
```

- **Kontoraden** visar firmans logga, eller namnet när den saknar logga, och under den länken "Back to {firma}" till kontot i firmans portal, eller till portalen när kontot inte har någon adress där. Sedan anslutningen, kontot, saldot, equity, fri marginal och marginalnivån. Anslutningen heter "Live prices" när den är uppe, så att ingen läser den som ett riktigt konto.
- **Vinstmålet** visas när firman har gett kontot ett: saldot som klarar fasen och hur mycket som är kvar, till exempel "5,000.00 to go". Målet nås på saldot när alla positioner är stängda, som i regelmotorn, så ett nått mål med öppna positioner visar "Reached, close positions".
- **Förlustgränserna** heter som i portalen: Daily loss limit och Max loss limit. Andra golv får sitt namn från id:t, till exempel Min equity limit. Varje gräns visas med sin nivå och hur mycket equity kan falla innan den bryts, till exempel "5,800.00 left". Värdet kommer från motorn (`headroom`). När mindre än 25 % av gränsens avstånd är kvar blir värdet gult, och under 10 % rött. Gränser på en fast nivå har inget avstånd och färgas inte förrän equity har nått dem. Ett negativt värde visas aldrig: en gräns som equity har nått visar "Broken".
- **Avslutat konto:** när ett golv bryts stänger motorn kontot. Under kontoraden visas då en röd ruta, "Trading on this account has ended", med en mening om varför, till exempel "The daily loss limit was broken at 17:38:12 Stockholm time: equity 9,482.50 fell below 9,500.00. Every position was closed at the next price.", och knappen "See the account at {firma}" till kontot i portalen. Har firman stängt kontot står det i stället. Kontoraden visar Ended, vinstmålet döljs, gränserna visar bara den som bröts, orderpanelen säger att inga nya ordrar tas emot, och ett tidigare "Buy filled." försvinner.
- **Watchlist** listar symbolerna i bokstavsordning med bid, ask, förändringen över 24 timmar och en liten kurva över samma tid. Kurvan ritas först när tjänsten har minst två timmar av priser för symbolen, eftersom en kurva av några få punkter blir en rak linje som byter färg med varje pris. Bid blir grönt eller rött efter hur det senast rörde sig. Tradern söker på symbol och väljer Alla, en grupp eller Favoriter. Grupperna följer basvalutan: metaller (ISO-koderna XAU, XAG, XPT och XPD) och annars Forex. Favoriterna sparas i webbläsaren på enheten. Terminalen öppnar på EURUSD när gruppen har den, annars på den första symbolen.
- **Grafens huvud** visar symbolen, bid, förändringen över 24 timmar, högsta och lägsta bid under samma tid och spreaden i punkter. Grafen visar candles av bid med tickvolym under, alltså antalet priser i varje candle, och kan visas i helskärm.
- **Affärer i grafen:** en pil till vänster om candlen där positionen öppnades pekar på öppningspriset, och en pil till höger om candlen där den stängdes pekar på stängningspriset. En grå streckad linje, 75 % synlig, går från pil till pil. Pilarna har färgen av affären: grön för köp och röd för sälj, så stängningen av ett köp är röd. Öppna positioner har bara öppningspilen och linjen för öppningspriset. Pilarna byggs av händelserna `PositionOpened` och `PositionClosed` och av de öppna positionerna, och ritas bara på candles som finns i grafen.
- **Dra stop loss och take profit i grafen:** linjerna för en öppen positions stop loss och take profit visar det uppskattade resultatet vid sin nivå, till exempel SL -100.00. Över linjen blir markören en dragpil. Tradern drar linjen till en ny nivå, och när musknappen släpps skickas den till motorn med `PUT /positions/{id}/stops`. Under dragningen följer resultatet med. Linjen stannar minst en punkt från priset positionen stänger till, bid för köp och ask för sälj, eftersom motorn inte tar emot stopp på fel sida. Esc avbryter. Säger motorn nej flyttas linjen tillbaka, och skälet visas i grafen. Grafens skala rymmer alltid stopplinjerna, så att de går att nå. Dragning görs med mus. På pekskärm flyttas grafen i stället.
- **Spöken för ordern som fylls i:** när stop loss, take profit eller priset för en limit- eller stoporder är ifyllt visas de i grafen som streckade linjer med halv synlighet, med till exempel Buy SL -100.00 i etiketten. Grafen ritar etiketter utan genomskinlighet, så deras färger är blandade till hälften med panelens bakgrund. Belopp ger olika priser för köp och sälj, så spöket visar den sida tradern pekar på eller har markerat, SÄLJ eller KÖP. Annars visas den enda sida som skrivna priser passar, och köp om båda passar. Med belopp följer spökena priset. Grafens skala rymmer spökena, och de går inte att dra.
- **Högerklick i grafen** öppnar en meny vid priset under musen, avrundat till en punkt, eller vid linjens nivå om klicket träffar en stopplinje. Menyn har:
  - **Reset chart**, som Reset chart view i TradingView: standardzoom vid den senaste candlen och en prisskala som följer candlarna igen efter att tradern har dragit i den. Alt+R gör samma sak var som helst i terminalen. Den fysiska R-tangenten används, så det fungerar på alla tangentbordslayouter, och AltGr lämnas i fred. Grafens egen förflyttning till den senaste candlen tar 400 ms och går inte att korta, så terminalen glider själv till standardzoomen och standardläget på 150 ms. Med minskade animationer i datorns inställningar hoppar grafen direkt dit.
  - **New order** med Stop loss och Take profit, som fyller i nivån som pris i orderpanelen. Spöket visas då direkt. Träffar högerklicket ett spöke byts valet för det mot Remove stop loss, Remove take profit eller Remove order price, som tömmer fältet i orderpanelen. Ett spöke för stop loss eller take profit tömmer fältet både för pris och belopp.
  - **En rad per öppen position på symbolen** med den stopp som passar på den sidan av priset positionen stänger till: stop loss under priset för köp och över för sälj, och take profit på andra sidan. Texten är Set eller Move beroende på om stoppet redan finns, och det uppskattade resultatet visas. Ett klick skickar `PUT /positions/{id}/stops`.
  - **Remove stop loss** eller **Remove take profit** i stället för Move, när högerklicket träffar positionens egen stopplinje.

  Prisaxeln behåller webbläsarens egen meny. Menyn stängs av ett val, Esc, Tab, ett klick utanför eller scroll i grafen, och den går att styra med piltangenterna. Säger motorn nej visas skälet i grafen.
- **Orderpanelen** har knappar för att stega volym, pris, stop loss och take profit. Volymen stegas med instrumentets steg inom dess gränser, och priser stegas en pip i taget (0,0001 för EURUSD, 0,01 för USDJPY och 0,1 för XAUUSD). Ett tomt pris börjar från bid. Under volymen visas kontraktsvärdet i basvalutan. Stop loss och take profit sätts som pris eller som belopp i kontots valuta (Price eller till exempel USD). Ett belopp räknas om till ett pris från där ordern öppnar: ask för ett köp, bid för en sälj, eller orderpriset för en limit- eller stoporder. Avståndet avrundas nedåt till hela punkter, så att beloppet inte överskrids. Med belopp stegar knapparna med vad en pip är värd vid volymen. Under SÄLJ och KÖP visas det andra sättet för varje sida: priserna när tradern skrev belopp, och de uppskattade beloppen när tradern skrev priser. När ordern har lagts töms pris, stop loss och take profit, medan volymen, ordertypen och valet mellan pris och belopp ligger kvar. En avvisad order tömmer ingenting, så att tradern kan rätta den. I positionstabellen visas det uppskattade resultatet bredvid stop loss och take profit, och Edit kan också ta belopp. Där räknas beloppen från öppningspriset. Panelen visar gruppens villkor för symbolen: hävstång, påslag på spreaden, provision och kontraktsstorlek. Det är en del av öppenheten mot traders.
- **Pausat konto:** kontoraden visar Paused, orderpanelen säger att nya ordrar inte tas emot, och köp och sälj går inte att trycka på. Tradern kan stänga positioner och flytta stop loss och take profit, också genom att dra linjerna i grafen. Händelserna visar när kontot pausades och fick handla igen.
- **Historik** listar stängda positioner och insättningar och uttag, till exempel en utbetalning, med det nyaste först. Skälet till en stängning står i vanliga ord: Manual, Stop loss, Take profit, Stop out, Loss limit eller Account closed. Ett uttag syns som en rad med beloppet i vinstkolumnen. Provisionen är positionens hela, både när den öppnades och när den stängdes. Saknas öppningen bland händelserna som hämtats räknas den som lika stor som stängningens, vilket den är om firman inte ändrade provisionen medan positionen var öppen.
- **Händelser** listar allt som hänt kontot i vanliga meningar, till exempel "Closed Buy 1.00 EURUSD at 1.07500 by the loss limit" och "Trading ended: a loss limit was broken", aldrig motorns kodord. Avvisningar, brott mot golv och stop out markeras i gult. Vid brott mot ett golv visas priserna från beviset.
- **Avvisningar** förklaras med vanliga ord, till exempel "Refused: not enough free margin" eller "Refused: the stop loss is on the wrong side of the price". Ett skäl som terminalen inte känner till visas som motorn skrev det.
- **En tidszon:** alla tider i terminalen, i statusraden, grafen, ordrarna, historiken och händelserna, visas i kontots tidszon, den som handelsdagen följer, så att de stämmer med portalen. Zonens namn står i statusraden och i grafens huvud, till exempel "Stockholm time". Ett konto utan tidszon, eller med en som webbläsaren inte känner till, visas i UTC.
- **Statusraden** visar firmans server, tiden i kontots tidszon med zonens namn, och kontots equity, marginal, fria marginal och marginalnivå.
- **Inte med från designen:** menyer för sidor som inte finns, ritverktyg och indikatorer, uppskattad marginal, kalkylator för positionsstorlek, marknadssentiment, export och ping. Uppskattad marginal och positionsstorlek kräver belopp som bara motorn kan räkna fram, och de andra delarna har ingen funktion bakom sig än.

## Dataflöde

| Data | Källa | Uppdatering |
|---|---|---|
| Instrument och villkor | `GET /instruments` | En gång |
| Värdet av en punkt | `GET /instruments/{symbol}/point-value` | Var 10:e sekund, eftersom det följer växelkursen |
| Candles | `GET /candles/{symbol}` | Vid byte av symbol eller tidsram. Den senaste candlen uppdateras med livepriser. |
| 24 timmar per symbol | `GET /candles/{symbol}?timeframe=M15&count=100` | Var 5:e minut, och visas tillsammans med livepriset |

- **Uppdateringar på klockan:** värdet av en punkt och de senaste 24 timmarna hämtas på hela 10 sekunder och hela 5 minuter. Alla delar av terminalen som visar samma värde delar då på en hämtning, i stället för att var och en hämtar lite förskjutet. React Query pausar uppdateringarna när fönstret inte är aktivt.
| Priser | SignalR `Prices` | Högst var 100:e ms |
| Kontot | SignalR `Account` | Högst var 250:e ms |
| Händelser | De senaste 1 000 från `GET /events` vid start, därefter SignalR `Events` | Direkt |
| Kontots namn, vinstmål, tidszon och adress i portalen, firmans logga | `GET /api/auth/me` (`accountDetails`, `server.logoUrl`) | Vid inloggning |
| Kommandon | `POST /orders`, `DELETE /orders/{id}`, `POST /positions/{id}/close`, `PUT /positions/{id}/stops` | Svaret innehåller händelserna |

- **Gränssnittet räknar aldrig pengar.** Equity, vinst, marginal och avståndet till golven kommer från motorn och visas som de är. Kontraktsvärdet under volymen är bara volymen gånger kontraktsstorleken i basvalutan, inte ett belopp på kontot, och det som är kvar till vinstmålet bara målet minus saldot.
- **Belopp för stop loss och take profit är uppskattningar.** Motorn ger värdet av en punkt per lot i kontots valuta vid aktuell växelkurs, och terminalen räknar punkter gånger volym gånger det värdet. Det faktiska resultatet beror på växelkursen när positionen stänger, provisionen kommer till, och en marknadsorder kan fyllas en bit från priset vid klicket.
- **Förändringen över 24 timmar** räknas från stängningen av den sista candlen på 15 minuter som slutade för 24 timmar sedan. Har tjänsten inte haft priser så länge visas ett streck i stället.
- **Inmatning kontrolleras innan den skickas.** Priser får inte ha fler decimaler än instrumentet, och volymen ska följa instrumentets gränser och steg. Motorn kontrollerar allt igen.
- **Order-id skapas i webbläsaren** (UUID), så att ett anrop som skickas igen inte kan lägga ordern två gånger.
- **Händelser:** terminalen behåller kontots senaste 1 000 händelser. Vid start hämtas de senaste, så historiken, händelserna och pilarna i grafen visar alltid det som hänt sist, också på konton med fler händelser. En stängning vars öppning är äldre än så visas med bara stängningspilen.
- **Återanslutning:** SignalR återansluter automatiskt. Efter en återanslutning hämtas det som missades med `GET /events?after=`, räknat från den senaste händelsen som terminalen vet att den har allt fram till. Svar på kommandon räknas inte, eftersom de kan komma före tidigare händelser i realtid. Har fler än 1 000 händelser missats hämtas de senaste 1 000 i stället för att bläddra igenom resten, eftersom terminalen ändå bara behåller så många. En hämtning som misslyckas görs om varannan sekund. Om tjänsten inte går att nå vid start försöker terminalen igen varannan sekund.

## Typer från API:t

1. Handelstjänsten genererar kontraktet `contracts/trading/trading-service.json` när den byggs.
2. `pnpm generate:api` genererar `src/lib/api/schema.ts` från dokumentet.
3. CI kontrollerar att båda filerna är committade och aktuella. En ändring i API:t som inte når terminalen stoppas alltså i CI.

## Teknik

- Next.js 16, React 19, TypeScript och Tailwind CSS.
- TradingView Lightweight Charts för grafen (se [ADR 0007](../adr/0007-graf-lightweight-charts.md)). TradingViews logga visas i grafen enligt licensen.
- Zustand för livedata och TanStack Query för anrop.
- `openapi-fetch` för typade anrop och `@microsoft/signalr` för realtid.

## Begränsningar

- Panelerna har fast storlek, och layouten är gjord för datorskärm.
- Candles finns bara i tjänstens minne, så förändringen över 24 timmar visas först 24 timmar efter att tjänsten startade.
- Varje rad i symbollistan hämtar sina egna candles för de senaste 24 timmarna. Med många symboler och traders behövs en samlad sammanfattning från tjänsten.
- Terminalen får högst ett pris per symbol var 100:e ms, så tickvolymen i den senaste candlen kan vara lägre än tjänstens tills grafen laddas om.
- Inget byte eller återställning av lösenord i terminalen. Firmornas traders loggar in genom sin portal, som har glömt lösenord.
- Grafen saknar ritverktyg och indikatorer.
- Candles börjar på hela timmar och dygn i UTC, och bara etiketterna visas i kontots tidszon. En dags candle följer alltså UTC-dygnet, inte handelsdagen, och i zoner med halvtimmar börjar en timmes candle på halvtimmen.
- Kontots namn, vinstmål och adress i portalen kommer med inloggningen, så ett konto som öppnas medan terminalen är öppen visas med sitt id tills sidan laddas om.

## Tester

- Enhetstester med Vitest för gränsernas namn och "Broken" i stället för negativa värden, vinstmålet, meningen när kontot har avslutats, avvisningar och händelser i vanliga ord, tider i en tidszon och dess namn, etiketterna i grafen, händelserna när ett konto pausas och återupptas, inmatning och stegknappar, belopp och priser för stop loss och take profit, spökena för ordern som fylls i, uppdateringar på klockan, candles, affärerna i grafen, de senaste 24 timmarna, grupper och spread, golvens namn och färg, favoriter, formatering, händelsetexter, sammanslagning av händelser och priser, hämtning av händelser vid start och efter återanslutning, vilken server som väljs, adressen till firmans portal med kontot, symbolen terminalen öppnar på, och positionens hela provision (`pnpm test`).
- Tester av hela flödet med Playwright (`pnpm e2e`): spärren och inloggningssidan med vald server, att utvecklingsfirmans traders leds till dess portal, fel lösenord, inloggning med länk från portalen, inloggning, stegknapparna, köp, stängning, historik, händelser och utloggning, stop loss och take profit med belopp, att en avvisad order behåller fälten och en lagd order tömmer dem, en stop loss som dras i grafen, menyn vid högerklick med stopp för ordern och borttagning av spöket, flytt och borttagning av en positions stop loss, Reset chart och Esc, kontot med namnet från portalen, vinstmålet, gränserna, länken tillbaka och tidszonen, rutan när en gräns bryts utan negativa värden, sökning och favoriter i symbollistan, byte av konto, och att en trader bara ser sitt eget konto. Testerna startar en egen tjänst på port 5121 och en egen terminal på port 3021 mot databasen `trading_e2e`, som töms före varje körning. De kan alltså köras medan du utvecklar. Postgres från `deploy/docker-compose.yml` måste vara igång.
- Lint, typkontroll och bygge i CI.
