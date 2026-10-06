# 0049. Capital.com som prisflöde under utvecklingen, med index, råvaror och krypto

- Status: Beslutad
- Datum: 2026-10-06

## Sammanhang

Traders hos propfirmor handlar mycket index, framför allt Nasdaq-100, men plattformen hade bara 10 valutapar, guld och silver. Tiingo, som används under utvecklingen ([ADR 0010](0010-prisflode-for-utveckling.md)), har inga index, råvaror eller krypto. Andra gratisalternativ undersöktes:

- **Twelve Data gratis:** inga index, och strömmen finns bara som test med få symboler.
- **Tiingo, Alpaca och Finnhub:** som närmast fonden QQQ, och bara under USA-börsens öppettider. Index hos propfirmor handlas nästan dygnet runt.
- **Dukascopy:** har index, men API:t finns bara för Java och demokontot gäller i 14 dagar.
- **OANDA och IG:** fungerar inte från Sverige, se ADR 0010.

Capital.com har ett gratis demokonto med API: REST och en ström med bid och ask för upp till 40 instrument, historik i staplar, och valutor, metaller, index, råvaror och krypto. API-nyckeln skapas i plattformen och kräver tvåstegsinloggning.

Motorn räknade bara om valutor direkt eller genom USD. Ett index med EUR som kursvaluta, som DE40, kunde därför inte handlas från ett konto i USD.

## Beslut

- Tjänsten har en adapter för Capital.com (`PriceFeed:Provider = CapitalCom`). Nyckeln, kontots e-post och nyckelns lösenord sparas med `dotnet user-secrets`, aldrig i filer, och tjänsten vägrar starta om de saknas.
- Adaptern öppnar en session, hämtar de senaste priserna och prenumererar på alla instrument i en ström. Strömmen pingas var fjärde minut, eftersom en session slutar efter 10 minuter utan användning. Den ansluter igen med växande väntetid om den bryts, nekas eller är tyst i 6 minuter. Priserna avrundas utåt till instrumentets decimaler, som hos Tiingo.
- Historiken hämtas som Capital.com:s staplar av bid, högst 950 per anrop eftersom Capital.com nekar perioder med 1 000 staplar eller fler. Anropen startar minst 150 ms isär för att hålla sig under gränsen på 10 anrop per sekund. Staplar som inte rör sig hoppas över, som för Tiingo ([ADR 0048](0048-grafernas-historik-per-prisflode.md)).
- Plattformen får 11 nya instrument, 23 totalt: indexen US100, US500, US30, DE40, UK100 och JP225, råvarorna USOIL, UKOIL och NATGAS, och kryptovalutorna BTCUSD och ETHUSD. Capital.com:s namn skiljer sig för några av dem: GOLD, SILVER, J225, OIL_CRUDE, OIL_BRENT och NATURALGAS.
- Index och råvaror har sig själva som basvaluta, till exempel US100 mot USD, så marginalen räknas med instrumentets eget pris som för guld. Motorn behövde inget nytt slags instrument.
- Varje instrument har en kategori i konfigurationen: Forex, Metals, Indices, Commodities eller Crypto. Den följer med i API:t, och terminalen grupperar bevakningslistan efter den. Saknas en kategori startar inte tjänsten.
- Motorn räknar om genom en annan valuta än USD när konfigurationen varken har paret eller en väg genom USD. Den första valutan i konfigurationens ordning som har båda paren används, till exempel EUR för DE40 på ett konto i USD. Vägen väljs av konfigurationen, inte av vilka priser som finns. Alla par som gick att räkna om förut räknas om exakt som förut, så tidigare indata ger samma händelser.
- Tiingo får bara valutor och metaller. Med Tiingo har index, råvaror och krypto inga priser.

## Konsekvenser

- Under utvecklingen finns riktiga priser för alla instrument. Capital.com:s priser är mäklarens egna CFD-priser med deras spread och får bara användas för vår egen utveckling, inte visas för firmor i produktion.
- I produktion kostar index extra. Nasdaq-100 ägs av Nasdaq och ingår inte i en licens för valutor ([rapporten om licens för prisdata](../../reports/Licens%20för%20prisdata%20i%20produktion.md)).
- Motorn har inga handelstider. Index och råvaror har dagliga pauser, och då avvisas ordrar som för gamla, precis som valutor på helgen.
- Kontraktsstorlek, decimaler, hävstång och påslag för de nya instrumenten är exempelvärden. Firmor som redan skapats har kvar sina grupper utan de nya instrumenten, medan nya firmor får alla.
- Capital.com strömmar högst 40 instrument per anslutning. Fler instrument kräver flera anslutningar.
- Ett byte till Capital.com hämtar historik för alla 23 instrument, ungefär 190 anrop, och kan ta omkring en minut innan graferna är fyllda.
