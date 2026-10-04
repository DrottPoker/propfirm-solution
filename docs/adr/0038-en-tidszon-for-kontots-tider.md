# 0038. Ett kontos tider visas i challengens tidszon

- Status: Föreslagen
- Datum: 2026-10-05

## Sammanhang

I genomgången som ny firma visades tider i tre zoner: historiken i webbläsarens zon, terminalens statusrad och graf i UTC, och handelsdagen började vid midnatt i Europe/Stockholm. En trader kunde inte se om en affär räknades till rätt dag, eller när den dagliga gränsen började om.

## Beslut

- **Alla tider för ett konto visas i challengens tidszon,** den som handelsdagarna följer: i terminalens statusrad, graf, ordrar, historik och händelser, och i portalens kontosida, översikt, utbetalningar, graf, affärer och regellogg.
- **Zonen står på sidan,** med stadens namn, till exempel "Stockholm time", i terminalens statusrad och grafens huvud och i kontosidans rubrik.
- **Terminalen får zonen** med kontots uppgifter (ADR 0035). Ett konto utan zon, eller med en som webbläsaren inte känner till, visas i UTC. Utbetalningar har zonen med sig, så att listor över flera konton visar varje utbetalning i dess challenges zon.
- **Candles behåller sina gränser i UTC.** Bara etiketterna visas i kontots zon, så att grafen och affärernas pilar fortsätter att mätas på samma sätt.

## Konsekvenser

- En trader ser samma tid i terminalen och portalen, och dagarna stämmer med när handelsdagen börjar.
- En dags candle i terminalen följer UTC-dygnet, inte handelsdagen. I zoner med halvtimmar börjar en timmes candle på halvtimmen.
- Adminpanelens sidor för hela firman, till exempel ordrar och fakturor, visas fortfarande i webbläsarens zon.
