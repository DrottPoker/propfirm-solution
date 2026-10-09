# Genomgång av terminalen: friktion, osäkerhet och utseende

Datum: 2026-10-09. Testat lokalt på `main` (`ef78719` plus de ocommittade ändringarna för glappen i graferna och personalpanelen), med Capital.com-flödet. Ingen kod är ändrad.

Fokus: var den som handlar tvekar, gör fel eller inte litar på det som visas, hur terminalen ser ut, vad som känns rörigt eller AI-genererat, och vad som behövs för att Kronant Trader ska kunna säljas som en egen produkt till olika sorters företag, inte bara till propfirmor.

## Så testade jag

1. Loggade in direkt i terminalen hos Demo Firm som `test@test.com` (kontot har brutit den dagliga gränsen) och `demo@example.com` (aktivt konto), och genom Nordic Edges portal som sara (finansierat 10K-konto med regler och Stockholmstid).
2. Köpte EURUSD med stop loss och take profit i USD, ändrade positionen, öppnade Close part, stängde, lade en limitorder, försökte med ett limitpris på fel sida och med för stor volym, och tog bort ordern. Använde högerklick, ritverktyg, indikatorer, regelboken, egna spärrar (sparade inget), låsdialogen (avbröt), Settings, detaljer för en affär och rapporten för ett brutet golv.
3. Stängde handelstjänsten en halv minut för att se vad tradern ser när anslutningen bryts.
4. Skärmstorlekar: 1920x1080, 1440x900, 1366x768, 1280x800, 1280x720, 1024x768 och en telefon (390x844).

Testdata kvar: demokontot har en stängd EURUSD-affär (-2,00 USD), en borttagen limitorder och några avvisade order bland händelserna, och volymen för EURUSD är nu 0,10. Indikatorerna och ritningen som testades är borttagna igen.

## Helhetsintryck

Bra och värt att behålla:

- Regelboken, förlustgränserna i kontoraden, storlek från risk, egna spärrar och rapporten när ett golv bryts. Ingen konkurrent visar så mycket, och det känns genomtänkt.
- Texterna är ärliga och konkreta: "Bought 0.10 EURUSD at 1.12260", "Refused: the price is on the wrong side of the market for this order type", och exakt varför ett konto avslutades.
- Spökena i grafen för ordern som fylls i, med beloppet på linjen ("Buy SL -50.00"), är tydliga.
- Notisen efter en stängning, med resultatet efter provision.
- Låsdialogen och bladet för egna spärrar förklarar vad som händer innan man bestämmer sig.

Det som skapar friktion och osäkerhet:

- **Platsen.** På en vanlig laptop får inte terminalen plats, så grafen blir ett smalt band och köpknapparna hamnar under kanten.
- **Siffror man inte kan lita på direkt.** Tider utan datum, en bruten anslutning som knappt syns och en graf som trycks ihop av stop loss-linjerna.
- **Orderpanelen** ber om många val och säger inte exakt vad knappen gör.
- **Farliga standardval.** 1 lot i alla instrument och på alla kontostorlekar, och ett klick lägger ordern direkt.
- **Förklaringar** finns bara i webbläsarens egen tooltip, som kommer efter en sekund och aldrig på en pekskärm.

Varför det känns rörigt eller AI-genererat:

- **Svävande kort.** Tre rundade kort med mellanrum och skugga, och lådor i lådorna (sammanfattningen och villkoren i orderpanelen, rutor i bladen). Proffsterminaler som MT5, cTrader och TradingView ligger kant i kant med tunna linjer, vilket både ser lugnare ut och ger mer plats.
- **Små väljare överallt.** Fyra segmentväljare i en smal orderpanel och chips med samma utseende i menyer.
- **Mallmönster.** Små versala etiketter ovanför en serif-rubrik ("TRADE DETAILS" över "Buy 0.10 EURUSD", "YOUR LIMITS" över "Stricter than the firm's", "BREACH REPORT") är ett känt mönster från genererade gränssnitt.
- **Monospace överallt.** Kontoraden, tabellerna och orderpanelen står i JetBrains Mono, så det ser ut som ett utvecklarverktyg.
- **Hjälptext under allt.** Varje inställning och varje blad har en eller två grå förklarande meningar.
- **Blinkande celler.** Bevakningslistan färgar hela celler gröna och röda, ofta fem eller sex samtidigt.
- **Staplade rutor.** Rutor i olika färger överst (avslutat konto, firmans meddelande, priser saknas, låst dag) läggs på varandra.

## Det viktigaste först

| # | Problem | Förslag |
|---|---|---|
| 1 | **Terminalen får inte plats på vanliga skärmar.** Panelerna har fast storlek (bevakningslistan 19rem, orderpanelen 18rem, nedre panelen 13rem). På 1366x768 och 1280x720 blir grafen 170-250 px hög, bevakningslistan visar två eller tre rader, och Sell, Buy, risken och villkoren hamnar under kanten utan att man ser att panelen går att rulla. På 1440x900 syns raden "Risks 50.00 USD, 1.0% of today's room" bara till hälften. En laptop med 1920x1080 och Windows standardskalning på 150 % har i praktiken 1280x650 i webbläsaren. Nedre panelen tar sin fulla höjd också när den bara säger "No open positions." | Paneler som går att dra i storlek och som minns storleken. Nedre panelen fälls ihop till flikraden när den är tom och öppnas när en order läggs. En kompaktare orderpanel där köp, sälj och risken alltid syns (T2). Rutorna överst samlas (se 87). |
| 2 | **Grafen trycks ihop av linjerna.** Skalan måste rymma varje stop loss, take profit och orderpris (`KeepInView`). Med stop loss 50 pips och take profit 100 pips på M1 blev candlarna ett platt streck mitt i grafen, och samma sak händer med en limitorder långt bort. | Skalan följer candlarna. En linje utanför bilden visas som en etikett i kanten med pil och belopp, till exempel "TP +100.00 ↑", som i TradingView. Ett klick på etiketten zoomar ut till linjen. |
| 3 | **Standardvolymen är 1 lot överallt.** På Nordic Edges 10K-konto är 1 lot EURUSD värt 10 USD per pip, så 50 pips tar hela dagens utrymme på 500 USD. Guld börjar på 1 lot, alltså 100 uns, cirka 420 000 USD och 14 000 USD i marginal. Ett klick på BUY lägger ordern direkt, eftersom "Ask before placing an order" är av från början. | Första volymen kommer från firmans inställning eller från en risk, till exempel 0,5 % av dagens utrymme, annars instrumentets minsta. Firman väljer om bekräftelsen är på från början. Visa vad ordern betyder även utan stop loss: "1 pip = 10 USD. Dagens utrymme räcker 50 pips." |
| 4 | **Knapparna säger inte vad som händer.** För limit och stop visar SELL och BUY marknadspriset, inte priset ordern läggs på. Ett limitpris över marknaden visas som "Buy limit" i grafen och skickas, och först motorn säger nej. | Knapparna beskriver ordern: "Buy limit 0.20 at 1.12100". Antingen en ordertyp Pending där terminalen väljer limit eller stop efter var priset ligger, som i cTrader, eller en varning innan klicket: "En buy limit ska ligga under 1.12253. Menade du buy stop?" |
| 5 | **Fyra väljare och två olika USD.** "Size in: Lots, USD, % balance, % room" och "Stop loss and take profit in: Price, USD" står i samma smala panel. USD i den första betyder risk, i den andra avstånd i pengar. "% room" förstår ingen utan att hålla musen över ordet. | Ett val för storlek, Lots eller Risk, och vid Risk en liten lista för enheten (USD, % av saldot, % av dagens gräns). Stop loss och take profit med enheten i själva fältet. Ord som traders känner igen: "% of today's limit" i stället för "% room". |
| 6 | **Tider utan datum.** Historiken, ordrarna, händelserna och rutan för ett avslutat konto ("broken at 06:50:57 UTC", som hände 3 oktober) visar bara klockslaget. En affär från i går 21:17 står under en från i dag 06:37 utan att man ser att dagen har bytts. | "Today 06:37", "Yesterday 21:17" och "3 Oct 06:50", och en rubrik per dag i historiken och händelserna. |
| 7 | **En bruten anslutning syns knappt.** När tjänsten var nere stod bara ett litet orange "Reconnecting" i statusraden. Saldot, equity, priserna och resultatet stod kvar som om de levde, och orderpanelen sa "No new EURUSD price for 27 s", alltså fel orsak. | En tydlig rad överst: "Anslutningen till Demo Firm bröts. Försöker igen. Priser och konto uppdateras inte." Tona ned alla levande siffror, stäng köp och sälj med rätt skäl, och visa kort "Ansluten igen" när den är tillbaka. |
| 8 | **Kontoraden har ingen ordning.** Sju till nio värden i samma storlek. Det tradern behöver mest, hur mycket som är kvar i dag ("4,847.81 left"), står minst och grått. Dagens resultat och öppet resultat finns inte. "Margin level -" tar lika stor plats som equity. Förklaringarna finns bara i webbläsarens tooltip. Equity, marginal och fri marginal står en gång till i statusraden. | Tre saker först: equity, dagens resultat och utrymmet till närmaste gräns med en mätare. Resten mindre eller bakom en knapp. Egna tooltips som fungerar med tangentbord och finger. Statusraden visar bara server, anslutning och tid (T3). |
| 9 | **Bevakningslistan blinkar som en julgran.** Bid och ask får hela cellen färgad vid varje pris. Kolumnerna byter bredd när man söker, kurvan går utanför panelens kant, flikarna radbryts så att Crypto och Favorites hamnar ensamma på en egen rad, och sökningen hittar bara symbolen: "gold", "dax" och "euro" ger inget. | Bara textfärgen ändras, eller en liten pil, med ett val att stänga av det. Fasta kolumner. Instrumentets namn i liten text under symbolen ("Gold", "Germany 40") och sök på namnet. Flikarna som en lista eller en rad som rullar i sidled. |
| 10 | **Grafen saknar grunderna.** Inga OHLC-värden när man pekar på en candle, ingen förklaring av vilken linje som är vilken indikator, och RSI-rutan blev cirka 40 px hög utan skala eller namn, under en skarp vit linje. | En rad uppe till vänster med öppning, högsta, lägsta, stängning och förändring för candlen under musen. Indikatorerna listade under den med namn, värde, dölj och ta bort. Rutor för RSI och MACD minst 80-100 px och möjliga att dra i storlek. |
| 11 | **Utseendet är plottrigt.** Se listan ovan: svävande kort, lådor i lådor, många små väljare, versala etiketter, monospace och hjälptext överallt. | Ett lugnare uttryck, kant i kant, med färre lådor och en sorts siffror (T1, T4, T5). |
| 12 | **Terminalen är byggd för en propfirma.** Rules, Daily loss limit, Profit target, Breach report, % room, Your own limits, Back to firm och challenge syns för alla. Inloggningen visar alla kunders namn för vem som helst. För en mäklare med riktiga pengar, en tradingskola eller ett internt handelsbord blir orden fel och viktiga delar saknas. | En kundprofil per server som slår på och av delar, väljer ord och standardval, och en egen inloggningsadress per firma. Se "Terminalen som egen produkt". |

## Buggar och fel

| # | Var | Bugg | Förslag |
|---|---|---|---|
| 13 | Indikatorer | Menyn säger "kept on this device for every symbol", men indikatorerna sparas på inloggningen och följer med till andra enheter (ADR 0052). | "Saved on your login, for every symbol." |
| 14 | Kontoväljaren | Saras konto heter `#1002 Quick 10K · Funded`, med mittpunkten som inte längre ska användas (ADR 0047), eftersom namnet sparades före den regeln. Hennes äldre konto står bara som id:t `nordic-edge-1002-1`. | Beskriv alla konton igen med de nya namnen, och visa ett läsbart namn ("Account 1002, Phase 1") när namnet saknas. |
| 15 | Grafen | Första etiketten på tidsaxeln klipps i vänsterkanten ("6:45" i stället för 06:45, "30" i stället för 04:30). | Låt den första etiketten börja innanför kanten, eller dölj den när den inte får plats. |
| 16 | Grafen | Verktygsraden klipps vid 1280 px ("Bid, UTC" blir "Bi") och vid 1024 px försvinner ritverktygen utanför kanten. | Låt mindre viktiga delar gå in i en meny när raden blir smal. |
| 17 | Grafen | Etiketter krockar på prisaxeln: hårkorsets pris ovanpå axelns ("1.12407" över "1.12400"), senaste priset ovanpå nästa, och etiketterna för SL och TP ovanpå axelns priser. | Dölj axelns etikett när en annan etikett täcker den. |
| 18 | Grafen | D1 öppnar med den vänstra tredjedelen tom, eftersom historiken börjar i april, och volymen visar höga staplar bara för de sista dagarna, eftersom äldre historik saknar tickvolym. | Anpassa vyn efter datan när tidsramen byts. Dölj volymen där den saknas. |
| 19 | Bevakningslistan | Kurvan för 24 timmar går utanför panelens högra kant och klipps. | Ge kolumnen fast bredd innanför panelen. |
| 20 | Bevakningslistan | Kolumnerna byter bredd och rubrikerna flyttar sig när sökningen ger en annan lista. | Fasta kolumnbredder. |
| 21 | Bevakningslistan | Flikarna radbryts med Crypto och Favorites centrerade ensamma på en andra rad, och på telefonen står Favorites ensam. | Se 9. |
| 22 | Orderpanelen | Byte till USD under Size in visar direkt det orange felet "Enter the risk as an amount with at most 2 decimals.", innan något är skrivet. | Visa felet först när något har skrivits eller när man försöker lägga ordern. |
| 23 | Orderpanelen | Volymen 500 (största är 100) ger en röd text under fältet, men BUY ser aktiv ut och ger en röd notis när man trycker. | Stäng knappen och skriv skälet på den eller under den. |
| 24 | Settings | Rutan hoppar upp och ned när man byter flik, eftersom den står mitt på skärmen och flikarna har olika höjd. | Fast höjd, eller förankra rutan högre upp. |
| 25 | Notiser | En ny notis läggs ovanpå den förra, så att den äldres text sticker fram under ("type"), och notiserna täcker ordertypen överst i orderpanelen. | Stapla notiserna med mellanrum, och placera dem där de inte täcker orderpanelens kontroller, till exempel nere till höger ovanför statusraden. |
| 26 | Regelboken | På det avslutade kontot visar regelboken "1,925.00 left" för Max loss limit, medan kontoraden döljer det. | Samma regel på båda ställena: efter avslutet visas bara gränsen som bröts. |
| 27 | Händelser | "Daily loss limit set at 9,500.00" står tre gånger vid olika tider, och "Trading days start at 00:00 Stockholm time" står bland affärerna, fast inget har ändrats för tradern. | Visa en sådan händelse bara när värdet faktiskt ändras. |
| 28 | Historik | En utbetalning står som "Withdrawal" med ett rött "-34.00" i kolumnen Result, som om det vore en förlust. | En egen rad, "Payout 34.00 USD", i neutral färg och utanför resultatkolumnen. |
| 29 | Statusraden | "Margin: 0.00" står utan valuta, medan equity har USD, och "Margin level: -" säger ingenting. | Valuta på alla belopp, eller på inget. Se 8 om att ta bort dubbletterna. |
| 30 | Inloggning | Texten "Choose your firm's server" i listan är vit och fet, som ett valt värde. | Grå platshållare. |

## Inloggning

| # | Problem | Förslag |
|---|---|---|
| 31 | Serverlistan visar alla kunders namn för vem som helst (Demo Firm, Nordic Edge Capital, testfirm). En trader vet sällan vad en server är och ser konkurrenterna till sin firma. För oss avslöjar listan hela kundlistan. | En egen adress per firma, till exempel `nordicedge.kronanttrader.com`, med firmans logga och utan lista. På den gemensamma sidan: "Skriv din e-post" och hitta servern från den, eller firmans namn. |
| 32 | Hos en firma med portal finns både "Log in through Demo Firm" och "I have a password for the terminal". Den andra vägen är oklar: vem har ett lösenord? | Visa länken bara när firman tillåter lösenord i terminalen. |
| 33 | Ingen länk för glömt lösenord, ingen hjälp, inga villkor, ingen integritetspolicy och ingen riskvarning. Firmor med egen portal klarar sig, men en mäklare måste ha dem. | Länkarna under rutan, styrda av firmans profil. |
| 34 | Sidan är mörk och tom med en liten ruta, och serverlistan är webbläsarens egen, med ett annat utseende än resten. | Firmans logga och namn ovanför rutan när servern är känd, en egen lista i samma stil som fälten, och ett val av språk när fler språk finns. |

## Kontoraden

| # | Problem | Förslag |
|---|---|---|
| 35 | Lägen som Ended, Paused och "Locked until 00:00" står i 11 px under kontots namn och är lätta att missa. | Läget som ord bredvid namnet, och en enda ruta under kontoraden som säger vad det betyder. |
| 36 | Kontoväljaren är webbläsarens egen lista med bara namnen. Med flera faser eller challenges syns inte vilka som är aktiva, klarade eller avslutade. | En meny med namn, läge och saldo för varje konto, där avslutade konton ligger sist. |
| 37 | Initialerna kommer från e-posten ("TE", "DE", "SA"), också när namnet är känt. | Namnet när det finns. |
| 38 | Infoikonerna bredvid etiketterna förklarar via `title`, som inte fungerar på pekskärm eller med tangentbord. | Se 8. |

## Bevakningslistan

| # | Problem | Förslag |
|---|---|---|
| 39 | En stjärna på varje rad ger brus. | Stjärnan visas när musen är över raden och alltid på favoriterna. |
| 40 | Ingen spread, ingen sortering, ingen egen ordning och inga egna listor. | Spread som valbar kolumn, sortering per kolumn, dra för att ordna, och egna listor ("Mina", "Index"). |
| 41 | Den valda raden syns bara som en tunn mässingsfärgad kant. | En tydligare bakgrund på hela raden. |
| 42 | Hos Nordic Edge, med fyra instrument, är tre fjärdedelar av panelen tom. | Låt listan bli kortare och ge platsen åt något annat, till exempel instrumentets villkor och öppettider. |

## Grafen

| # | Problem | Förslag |
|---|---|---|
| 43 | Bara candles, och inget längre än D1. | Linje, staplar och Heikin Ashi, och W1 och MN. |
| 44 | Ritverktygen är tre ikoner utan text, med förklaring bara i webbläsarens tooltip. Det finns ingen text, pil eller Fibonacci. | Egna tooltips med namn och kortkommando, och fler verktyg på sikt. |
| 45 | TradingViews logga står stor nere till vänster över candlarna. | Licensen för Lightweight Charts tillåter att logan döljs om TradingView nämns med en länk på en sida som användarna når (kontrollera villkoren i NOTICE). Lägg det i en Om-ruta och dölj logan i grafen. |
| 46 | Högerklick ger bara Reset chart och "New order: Stop loss, Take profit", som fyller i fälten. Det vanligaste valet i andra plattformar saknas: "Buy limit här" och "Sell stop här". | Lägg till order på priset under musen, larm på priset och en vågrät linje. |
| 47 | I helskärm syns bara grafen, så det går inte att handla därifrån. | Små köp- och säljknappar i grafens hörn, som valbar snabbhandel (som i MT5 och cTrader), också i vanligt läge. |
| 48 | Inga prislarm för tradern. | Larm på pris med ljud och notis, som går att sätta från grafen och bevakningslistan. |
| 49 | "Bid, UTC" i verktygsraden är kort och tekniskt. | "Bid prices, UTC time", eller bara zonen, med förklaringen i tooltip. |

## Orderpanelen

| # | Problem | Förslag |
|---|---|---|
| 50 | Rubriken "Trade EURUSD" upprepar grafens huvud. | Instrumentets namn ("Euro / US Dollar") och en informationsknapp för villkoren. |
| 51 | Villkoren (hävstång, påslag, provision, kontrakt, öppettider) ligger längst ned, under kanten på de flesta skärmar. | Bakom informationsknappen, eller som en kort rad som alltid syns. |
| 52 | Spreaden syns inte mellan SELL och BUY. | Spreaden mellan knapparna, som i MT5 och cTrader. |
| 53 | Enheterna blandas: "Spread 9 points" i grafens huvud, "Spread markup 2 points", "Pip value", "Trailing SL (10.0 pips)" och "markup: 1 point" i detaljerna. EURUSD:s spread säger traders 0,9 pips. | En enhet per sorts instrument, pips för valutor och punkter där en punkt är definierad, med priset som skillnad i tooltip. |
| 54 | På ett avslutat, pausat eller låst konto står hela formuläret kvar med svagt tonade färgade knappar och en röd ruta i mitten. | Byt formuläret mot ett tydligt läge, till exempel "Trading on this account has ended" och länken till firman, utan fält. |
| 55 | Rutan för Trailing stop är webbläsarens egen kryssruta, när Settings använder reglage. | Samma kontroll överallt. |
| 56 | Fältet för limitpris visar bid i grått som platshållare, vilket ser ut som ett ifyllt värde, och efter en lagd order står det grå priset kvar. | Tomt fält med "Price" och en knapp "Use market price". |

## Positioner, ordrar, historik och händelser

| # | Problem | Förslag |
|---|---|---|
| 57 | Positionerna saknar öppningstid, provision och swap, och det sammanlagda öppna resultatet syns inte. | Kolumnerna, och en summarad: "Open P/L -1.40 USD". |
| 58 | Varje position har fyra knappar (Edit, Break even, Close part, Close), och Close står bredvid Close part och stänger med ett klick. | Close som en egen tydlig knapp, resten i en meny eller som ikoner med tooltip. Ett val i Settings för att bekräfta stängningar. |
| 59 | Edit och Close part byter raden mot fält som inte står under rubrikerna (SL-fältet under Side, TP under Open price), så det är svårt att läsa vad som ändras. | En liten ruta vid raden, eller ett blad från höger, med SL och TP och spöket i grafen. |
| 60 | "Cancel" tar bort en väntande order, och samma ord avbryter en ändring. | "Cancel order", eller en papperskorg med tooltip. |
| 61 | Historiken går inte att filtrera, summera eller exportera. | Today, This week och All, en summarad med resultat och antal affärer, och export till CSV. |
| 62 | Händelserna blandar affärer, avslag och systemhändelser, och samma avslag står fyra gånger i rad ("Closing all positions refused: the price is too old"). | Slå ihop upprepningar ("4 gånger"), och filter för affärer, konto och varningar. |
| 63 | Detaljerna för en affär börjar med positionens hela id ("82609d04-888d-44fa-...") och slutar med journalens löpnummer. Det är brus för tradern. | Lägg id och löpnummer under "For support", med en knapp som kopierar dem. |

## Regelboken, egna spärrar och rapporter

| # | Problem | Förslag |
|---|---|---|
| 64 | Regelboken visar "Your own limits" före firmans regler, fast firmans regler avgör om kontot klarar sig. | Firmans regler först, de egna under, eller två flikar. |
| 65 | Varje regel har en grå, gul, röd eller grön prick, ett mönster som annars är bortplockat (ADR 0047). | Läget som färgad text eller en liten mätare. |
| 66 | Bladet för egna spärrar har tre fält med olika utseende: förlustgränsen har en enhetsväljare, dagsmålet "Off when empty" bredvid och antal affärer ett litet fält. Den orange rutan "Stricter now, looser tomorrow" ser ut som en varning innan något är ändrat. | Samma utseende för alla tre fälten. Visa rutan först när en ändring faktiskt är lösare. |
| 67 | Ett finansierat konto visar ingenting om utbetalningar: när nästa går att begära eller vilken andel tradern får. | En rad i regelboken, "Payout available from 12 Oct", med länk till portalen, när kundprofilen har utbetalningar. |

## Notiser, meddelanden och anslutning

| # | Problem | Förslag |
|---|---|---|
| 68 | Firmans meddelande står på en rad med etiketten ("Slow prices"), rubriken och "Demo Firm: texten", som läses som ett chattmeddelande. När man trycker på det växer raden och knuffar ned hela terminalen. | Rubrik och en rad text. "Read more" öppnar en ruta i stället för att trycka ned sidan. |
| 69 | Rutorna överst staplas: avslutat konto, firmans meddelande, priser som saknas och den låsta dagen tar 40-80 px var. | En plats för kontots läge och en för plattformens meddelanden, högst en rad var, med detaljer bakom ett klick. |
| 70 | Varningarna om reglerna kommer bara medan terminalen är öppen. | Pushnotiser när det installerbara läget finns (punkt 7 i konkurrentrapporten). |

## Settings

| # | Problem | Förslag |
|---|---|---|
| 71 | Fliken Trading har en enda inställning. | Första volym per instrument eller som risk, bekräfta stängningar, snabbhandel i grafen, visa spread, tidszon (i dag alltid kontots), språk och tema. |
| 72 | Varje inställning har en eller två meningar förklaring. | En kort rad, och resten bakom en informationsknapp. |

## Telefonen

| # | Problem | Förslag |
|---|---|---|
| 73 | Positionerna är datorns tabell ihoptryckt: resultatet och Close ligger utanför skärmen. | Ett kort per position med symbol, sida, volym och resultat stort, och Close direkt på kortet. |
| 74 | Fliken Chart har inget sätt att handla. Man måste byta till Trade och tillbaka. | En rad med SELL och BUY under grafen. |
| 75 | Kontots siffror rullar i sidled, så den dagliga gränsen ligger utanför skärmen tills man rullar. | Equity och utrymmet kvar i dag alltid synliga, resten efter ett tryck. |

## Kortkommandon och hjälp

| # | Problem | Förslag |
|---|---|---|
| 76 | Det enda kortkommandot är Alt+R. | "/" för att söka symbol, 1-7 för tidsramarna, Esc för att avbryta, och "?" som visar alla. Inget kortkommando lägger en order utan bekräftelse. |
| 77 | Ingen hjälp första gången, fast mycket är nytt även för vana traders: spökena, regelboken, % room och egna spärrar. | En kort rundtur i tre eller fyra steg första gången, som går att hoppa över, och en hjälpmeny med firmans hjälplänk och kortkommandona. |
| 78 | Ingen Om-ruta med version, Kronants länk, plattformens status och TradingViews omnämnande. | En Om-ruta i användarmenyn. |

## Terminalen som egen produkt

I dag utgår terminalen från att kunden är en propfirma med egen portal. Olika kunder behöver olika saker:

| Kund | Behöver | Det som är fel i dag |
|---|---|---|
| Propfirma | Regler, gränser, mål, utbetalningar, firmans meddelanden | Mest på plats. Utbetalningarna syns inte (67). |
| Mäklare med riktiga pengar | Insättning och uttag, marginalnivå med margin call och stop out, swap, kontoutdrag, riskvarning enligt EU:s regler, glömt lösenord och tvåstegsinloggning | Orden Rules, challenge, % room och Back to firm passar inte. Swap, kontoutdrag och riskvarning saknas. |
| Tradingskola eller kurs | Övningskonto som går att nollställa, enkla ord och förklaringar, kanske en topplista | Svåra ord ("points", "markup", "Margin level"), ingen nollställning och ingen rundtur. |
| Tävling | Topplista, tid kvar och regler | Inget av det. |
| Internt handelsbord eller fond | Flera konton samtidigt, exponering per symbol, inga challenge-ord | Kontoväljaren är en enkel lista och inget summeras över konton. |

| # | Problem | Förslag |
|---|---|---|
| 79 | Delarna är samma för alla. Regelboken, egna spärrar, % room, rapporten för ett brutet golv och "Back to firm" syns oavsett kund, och % room visas också när kontot inte har några förlustgränser. | En kundprofil per server: vilka delar som syns, vilka ord som används ("Rules" eller "Risk limits", "Challenge" eller "Account") och standardval (bekräfta order, första volym, ljud, tidszon, språk). Terminalen visar bara det profilen slår på. |
| 80 | Varumärket är bestämt som vårt (ADR 0009), med firmans logga i kontoraden. Mäklare kräver ofta sitt eget utseende. | Bestäm nivåerna: vårt utseende som i dag, samvarumärke med firmans logga och accentfärg och Kronant i statusraden, eller helt eget utseende som en dyrare nivå. |
| 81 | Bara engelska (`lang="en"`). | Språk per användare med firmans som standard. Svenska och firmornas marknader först. |
| 82 | Siffror och datum är alltid amerikanska ("99,845.81", "Fri 9 Oct"). | Följ språket och landet: "99 845,81" på svenska. |
| 83 | Bara mörkt tema. Många mäklares kunder och kontor vill ha ljust. | Ett ljust tema. Färgerna är redan samlade i `@kronant/design`, så det är främst arbete med grafen och statusfärgerna. |
| 84 | Ingen plats för firmans hjälp, support, villkor och riskvarning i terminalen. | Länkarna i användarmenyn och på inloggningen, från kundprofilen. |
| 85 | Firmor kan vilja bädda in terminalen i sin egen webbplats. | Avgör om det ska stödjas. I så fall behövs regler för vilka adresser som får visa den, och en layout som klarar en smalare ruta. |

## Designförslag

### T1. Kant i kant

Panelerna ligger utan mellanrum, åtskilda av 1 px linjer, på en och samma yta. Skuggor bara på menyer, dialoger och blad. Inga lådor i lådor: sammanfattningen och villkoren i orderpanelen blir vanliga rader. Det ger cirka 30 px mer i höjd och bredd och tar bort det mesta av det röriga.

### T2. En ny orderpanel

```
EURUSD  Euro / US Dollar                       (i)
[ Market | Pending ]
Size         [ 0.10 ]  [ Lots v ]
Stop loss    [ 50   ]  [ USD v ]   Take profit [ 100 ] [ USD v ]
[ ] Trailing stop
[   SELL 1.12231   ]   0.9   [   BUY 1.12240   ]
Risk 50 USD, 1.0 % of today's limit   [=====-----]
Margin 112 USD
```

Allt ryms på 768 px höjd. En limit eller stop väljs av terminalen efter priset, och knapparna säger "Buy limit 0.20 at 1.12100".

### T3. Kontoraden med ordning

```
NORDIC EDGE  | #1002 Quick 10K, Funded v | Equity 10,000.00 | Today +0.00 | Left today 500.00 [==========] | More v | Rules | SA
```

Mätaren för utrymmet byter färg som gränserna gör i dag. Balance, fri marginal, marginalnivå och den totala gränsen ligger under More. Statusraden visar bara server, anslutning och tid.

### T4. Siffror och text

- En sorts siffror: tabellsiffror i textens typsnitt för belopp och tabeller. Monospace bara i prisaxeln och prisfälten, om den ska finnas kvar alls.
- Inga versala etiketter ovanför rubrikerna, och serif bara i ordmärket.
- Hjälptext högst en kort rad. Resten bakom en informationsknapp eller i hjälpen.

### T5. Färg och rörelse

- Prisändringar som textfärg, inte färgade celler.
- SELL och BUY lite mindre mättade, så att de inte drar blicken från grafen när man inte handlar.
- Mässing bara för det valda och för huvudknappen.
- Samma betydelse för rött och grönt överallt. En utbetalning är inte en förlust (28).

### T6. Layouter

Färdiga layouter, Standard, Diagram i fokus och Snabbhandel (större knappar och köp och sälj i grafen), som sparas på inloggningen. Senare flera grafer samtidigt.

### T7. Telefonen

Kort för positionerna (73), köp och sälj under grafen (74) och en fast rad med equity och utrymmet kvar i dag (75).

## Beslut som behövs

1. Vilka kunder ska terminalen säljas till först: propfirmor, mäklare eller skolor? Det styr kundprofilen (79) och vilka delar som byggs först.
2. Varumärket: vårt, samvarumärke eller helt eget utseende för kunden (80).
3. Vilka språk först (81). Samma fråga som punkt 7 i konkurrentrapporten.
4. Ska ett klick på BUY fortsätta att lägga ordern direkt, eller ska firman välja (3)?

## Förslag på ordning

1. Buggarna 13-30. Små och snabba.
2. Plats och graf: 1, 2, 10 och T1.
3. Orderpanelen och säkra standardval: 3, 4, 5 och T2.
4. Tid och anslutning: 6 och 7.
5. Kontoraden och bevakningslistan: 8, 9 och T3.
6. Telefonen: 73-75 och T7.
7. Produkten, efter besluten ovan: 12 och 79-85.
