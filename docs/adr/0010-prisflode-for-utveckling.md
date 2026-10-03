# 0010. Tiingo som riktigt prisflöde under utvecklingen

- Status: Beslutad
- Datum: 2026-10-03

## Sammanhang

Utvecklingen behöver riktiga priser för valutor och guld, gratis och tillgängliga från Sverige. Leverantören för produktionen är inte vald, eftersom licensvillkoren för att visa priser för kunders traders inte är utredda (se produktplanen).

Gratis alternativ som undersöktes:

- **OANDA v20 (demokonto):** valdes först, men API:t finns inte hos OANDA TMS Brokers, som sedan 2023 har kunderna i EU. Ett demokonto från Sverige blir ett MetaTrader 5-konto utan API.
- **Tiingo:** gratisplan med API-nyckel. Strömmande bästa bud och utbud (top of book) för över 140 valutapar, inklusive guld (`xauusd`), via WebSocket. Inga krav på land.
- **Kraken:** öppna priser utan konto, men USDJPY handlas knappt där (spreaden var över 3 yen vid kontrollen).
- **Saxo (simulering):** riktiga valutapriser, men token från utvecklarportalen gäller bara i 24 timmar.
- **IG:** kräver ett riktigt konto för att få en API-nyckel till demokontot.
- **cTrader Open API:** gratis med demokonto, men applikationen måste godkännas av Spotware först.
- **Twelve Data, Finnhub:** WebSocket saknas på gratisnivån eller ger bara senaste pris, inte bud och utbud.

## Beslut

- Tjänsten har en adapter för Tiingos ström av valutapriser (`PriceFeed:Provider = Tiingo`). Standard är fortfarande det syntetiska flödet, så att projektet fungerar utan konto.
- API-nyckeln sparas med `dotnet user-secrets`, aldrig i filer. Tjänsten vägrar starta om den saknas.
- Vid varje anslutning hämtas först de senaste priserna via REST. Strömmen skickar bara ändringar och är tyst när marknaden är stängd, så utan dem skulle inget ha ett pris på helgen.
- Högst ett pris per symbol och kvart sekund går vidare till motorn, alltid det senaste. En livlig marknad kan alltså inte dränka motorn och journalen.
- Priserna avrundas utåt till instrumentets decimaler (bid nedåt, ask uppåt), så att spreaden aldrig blir mindre än Tiingos.
- Adaptern ansluter igen med växande väntetid, upp till 2 minuter, om strömmen bryts eller är tyst längre än 5 minuter. Tiingo skickar en heartbeat varannan minut, även när marknaden är stängd. En död anslutning upptäcks inom 30 sekunder med WebSocket-ping. Gratisplanen tillåter 50 anrop i timmen, och varje anslutning gör ett.

## Konsekvenser

- Tiingos gratisplan är för eget bruk och tillåter inte att priserna visas för andra. Adaptern är bara för utveckling och tester.
- Tiingo säljer en plan för vidaredistribution. Den bör utredas tillsammans med andra leverantörer inför lansering.
- Utan historik från Tiingo fylls graferna med livepriser, och efter en omstart från journalen.
- När marknaden är stängd kommer inga nya priser, så motorns skydd mot gamla priser avvisar ordrar med `StalePrice`. Det liknar en stängd marknad, men riktiga handelstider finns inte i motorn än. För att handla på helgen används det syntetiska flödet.
