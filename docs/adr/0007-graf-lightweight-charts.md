# 0007. TradingView Lightweight Charts för grafen

- Status: Beslutad
- Datum: 2026-10-02
- Kompletterad av [0051](0051-orderverktyg-och-grafens-verktyg.md): egna indikatorer och ritverktyg på Lightweight Charts i stället för att vänta på Advanced Charts.

## Sammanhang

Traders jämför med plattformar som TradeLocker, som har TradingViews fullständiga grafer med ritverktyg och indikatorer. Grafen är det trader tittar mest på.

TradingView erbjuder:

- **Lightweight Charts:** öppen källkod och gratis. Kräver att TradingView anges som källa. Har candles, linjer och prisnivåer, men inga ritverktyg och få indikatorer.
- **Advanced Charts:** gratis efter ansökan. Har ritverktyg och många indikatorer. Villkoren behöver kontrolleras.

## Beslut

- Version 1 använder Lightweight Charts. TradingViews logga visas i grafen.
- En ansökan om Advanced Charts görs parallellt.
- Grafen ligger i en egen komponent (`PriceChart`) som bara tar emot candles, priser och positioner. Den kan bytas mot Advanced Charts utan att resten av terminalen ändras.

## Konsekvenser

- Ingen licenskostnad och inget beroende av ett godkännande för att komma igång.
- Ritverktyg och indikatorer saknas tills Advanced Charts finns på plats. Det kan vara en nackdel i jämförelse med konkurrenterna.
