# 0055. Inställningar i terminalen

- Status: Föreslagen
- Datum: 2026-10-07

## Sammanhang

Terminalens val låg utspridda: Sound on fills och Sound on warnings i menyn bakom initialerna, volymen under grafen som en knapp ovanför grafen, och resten gick inte att välja. Tradern kunde inte byta färg på candles, stänga av rutnätet, se ask i grafen eller få en fråga innan en order skickas, vilket TradeLocker, cTrader och MetaTrader alla har. Inställningarna sparas redan på traderns inloggning och följer med till andra enheter ([ADR 0052](0052-regelboken-varningar-och-storlek-fran-risk.md)), så det som saknades var ett ställe att välja dem på och fler val.

## Beslut

- **En ruta, Settings,** öppnas från menyn bakom initialerna. Den har flikarna Chart, Sounds och Trading. Varje ändring gäller direkt och sparas på inloggningen som förut, så det finns ingen Spara-knapp, bara Done. "Reset to defaults" sätter tillbaka allt efter ett andra klick, så att ett klick aldrig tar bort traderns egna färger. Ljudvalen flyttar från menyn till rutan, och menyn har kvar e-postadressen, servern, Settings och Log out. Det ersätter platsen för ljudvalen i ADR 0052.
- **Chart:**
  - Candle colors: fyra färdiga par, Green and red (Kronants, som förut), Teal and red, Blue and orange (lätt att skilja för den som har svårt med rött och grönt) och Light and dark, eller traderns egna två färger. En liten rad candles i rutan visar valet. Färgerna gäller candles, volymen under dem och MACD:s staplar. Köp och sälj, vinst och förlust behåller sina färger, eftersom de betyder något annat än att ett pris steg.
  - Volume under the candles (som förut, på från början), Grid lines (på), Ask price line (av): en streckad linje vid ask, eftersom candles visar bid och ett köp öppnar vid ask, och Trades on the chart (på): pilarna där positionerna öppnades och stängdes.
- **Sounds:** en volym från 0 till 100 % för alla ljud, med en knapp som spelar ett prov, och Sound on fills (av), Sound on closes (ny, av: två mjuka fallande toner när en position stängs, också av stop loss, take profit eller en gräns) och Sound on warnings (på). Volymen räknas i kvadrat, eftersom örat hör styrka så: halvvägs låter ungefär hälften så starkt. Ett ljud spelas en gång när det slås på, så att tradern hör det och webbläsaren tillåter det senare.
- **Trading:** Ask before placing an order (av). Orderpanelen visar då ordern och dess stoppar i stället för köp- och säljknapparna, till exempel "Buy 1.00 EURUSD at market?" och "No stop loss or take profit.", och skickar den på Confirm buy. Frågan kommer före knapparna, så att det andra klicket i ett dubbelklick hamnar på den och inte på Confirm. Esc och Cancel skickar ingenting. En marknadsorder fylls till priset när tradern bekräftar. Ordrar från högerklick i grafen skickas direkt, eftersom tradern där redan valt typ och pris i en meny.
- **Nycklar** på inloggningen: `trading.chartGrid`, `trading.chartAsk`, `trading.chartTrades`, `trading.closeSound`, `trading.confirmOrders` med "on" eller "off", `trading.soundVolume` med ett heltal och `trading.candleUp` och `trading.candleDown` med en färg som `#rrggbb`. Ett värde som inte går att läsa ger standardvalet. Inget ändras i handelstjänsten.

## Konsekvenser

- Nya val läggs i samma ruta och i `src/lib/settings.ts`, och följer med till andra enheter utan något nytt i tjänsten.
- Candles i traderns egna färger kan likna köp och sälj. Det är traderns val, och pilarna för affärer behåller köpets och säljets färger.
- Frågan före en order gäller bara orderpanelen. Vill en trader ha den också för högerklick eller för att stänga positioner byggs det som egna val.
