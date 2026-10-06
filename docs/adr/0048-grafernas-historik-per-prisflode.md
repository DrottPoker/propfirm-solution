# 0048. Graferna visar bara det aktuella prisflödet, med 30 dagars historik från flödet

- Status: Beslutad
- Datum: 2026-10-06

## Sammanhang

Under utvecklingen används Tiingos riktiga priser på vardagar och det syntetiska flödet på helgen, när valutamarknaden är stängd ([ADR 0010](0010-prisflode-for-utveckling.md)). Graferna byggdes om från journalens priser för det senaste dygnet vid varje start ([ADR 0008](0008-journal-av-indata.md)), men journalen visste inte vilket flöde ett pris kom från. Efter en helg med påhittade priser visade graferna därför ett dygn av påhittade priser före de riktiga, med ett hopp mellan dem, till exempel EURUSD från 1,08 till 1,12. Efter ett byte saknade graferna dessutom historik, eftersom Tiingo-adaptern inte hämtade någon.

Priserna i journalen kan inte tas bort. De är indata till motorn, och kontona byggs om från dem.

Tiingo har staplar på en minut minst en månad bakåt, även på gratisplanen. Staplarna bygger på mittpriset och saknar tickvolym. Tiingo tar bara hela dagar, svarar med högst 10 000 staplar per anrop och tillåter 50 anrop i timmen. Medan marknaden är stängd fyller Tiingo i staplar som upprepar det senaste priset.

Att bygga graferna från journalen räcker inte för 30 dagar. Med Tiingo blir det cirka 1,6 miljoner priser per dygn, alltså runt 35 miljoner rader vid varje start.

Terminalen hämtade graferna bara när de visades första gången, så en omstart av tjänsten syntes inte förrän sidan laddades om. Den slutade också försöka återansluta efter ungefär 40 sekunder, eftersom SignalR som standard ger upp efter fyra försök.

## Beslut

- **Varje pris sparas med sitt flöde,** i kolumnen `feed` i `engine_inputs`. Namnet är detsamma som i `PriceFeed:Provider`, till exempel `Synthetic` eller `Tiingo`. Priser som sparades före ändringen har inget flöde, eftersom det inte går att veta.
- **Graferna visar bara det aktuella flödets priser.** Ett annat flödes priser kommer aldrig med, så helgens påhittade priser försvinner när tjänsten startas med Tiingo igen.
- **Staplarna sparas i tabellen `chart_bars` per flöde.** Inspelaren sparar varje färdig minutstapel några sekunder efter att minuten slutat, och de sista när tjänsten stängs. Vid start fyller de sparade staplarna graferna, och bara priserna efter den sista sparade stapeln spelas upp från journalen. Längre tidsramar byggs av minutstaplarna. Staplarna är härledda data: journalen har kvar varje pris.
- **Vid ett byte laddas flödets historik för 30 dagar.** Ett byte märks på att det senaste sparade priset kommer från ett annat flöde, eller att det inte finns något. Historiken ersätter flödets tidigare staplar, och `chart_histories` noterar att den är laddad. Går det inte att ladda den startar tjänsten ändå, och nästa start försöker igen.
- **Lägre tidsramar får kortare historik.** De senaste 2 dagarna och i dag kommer i minutstaplar, som ger M1 och M5. Resten kommer i staplar på 15 minuter, som ger M15 och längre. Grafen visar 500 staplar, alltså ungefär 8 timmar på M1 och knappt 2 dygn på M5, så längre historik där skulle inte synas. Med Tiingo blir det ungefär 11 anrop per byte i stället för 50 eller fler. Längderna ställs in med `Charts:History` och `Charts:MinuteHistory`.
- **Tiingos historik görs om till bid.** Mittpriset sänks med halva den aktuella spreaden och avrundas nedåt, som livepriserna. Staplar som inte rör sig från den förra hoppas över, eftersom livepriserna inte har några staplar när marknaden är stängd.
- **Det syntetiska flödet hittar på sin historik.** Den går bakåt i tiden från det senaste sparade priset, så historiken slutar där livepriserna fortsätter och graferna inte hoppar.
- **`GET /candles` väntar tills graferna är fyllda** efter en start, så att ingen får en graf som saknar sin senaste del.
- **Terminalen hämtar graferna och de senaste 24 timmarna igen** efter en återanslutning eller ett misslyckat försök att ansluta, eftersom tjänsten kan ha startats om. Den återansluter direkt och sedan varannan sekund, så länge det behövs.

## Konsekvenser

- Efter ett byte har graferna 30 dagars historik direkt, och förändringen över 24 timmar syns på en gång.
- Staplar från Tiingos historik har ingen tickvolym, så volymbandet under grafen är tomt för dem. Spreaden vid bytet används för hela historiken, så bid i historiken kan skilja en bråkdel av spreaden från de riktiga buden då.
- Det syntetiska flödets historik är påhittad. Efter en helg med det syntetiska flödet ersätts den av Tiingos riktiga historik när Tiingo startas igen.
- Startar tjänsten med samma flöde läses de sparade staplarna, några hundra tusen rader, i stället för ett dygn av priser. Starten går alltså fortare än förut.
- Tabellen `chart_bars` håller 30 dagar per flöde, ungefär en halv miljon rader. Äldre staplar tas bort vid varje start.
- Ursprunget till varje pris finns kvar i journalen. Det kan behövas för att svara på vilket pris från vilken leverantör som till exempel utlöste en stop loss.
- Byts Tiingo mot en annan leverantör inför lansering behöver den nya adaptern bara ge sin historik i samma form.
