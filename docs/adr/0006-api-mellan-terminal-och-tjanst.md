# 0006. REST och SignalR mellan terminalen och handelstjänsten

- Status: Föreslagen
- Datum: 2026-10-02

## Sammanhang

Handelsterminalen i webbläsaren behöver lägga ordrar och få priser, kontovärde och händelser i realtid. Både terminalen och tjänsten hör till handelsplattformen (Produkt 1), så det här är inte kontraktet mellan produkterna (se ADR 0004).

## Beslut

- **Kommandon och läsningar** går via REST med JSON: lägg order, ta bort order, stäng position, ändra stop loss och take profit, hämta konto, priser, instrument, candles och händelser.
- **Kontraktet genereras.** Tjänsten skriver OpenAPI-dokumentet till `trading/terminal/openapi/` när den byggs, och terminalens TypeScript-typer genereras från det. Båda committas, och CI kontrollerar att de är aktuella.
- **Realtid** går via SignalR (WebSocket med automatisk återanslutning). Servern skickar priser, kontot och händelser. Inga kommandon går via SignalR.
- **Gränssnittet räknar aldrig pengar.** Equity, vinst och marginal kommer alltid från motorn. Terminalen formaterar bara siffror med instrumentets antal decimaler, och priser som skickas in får inte ha fler decimaler än instrumentet. Det tradern skrev är alltså exakt det som skickas.
- **Order-id skapas av klienten.** Ett anrop som skickas igen efter ett nätverksfel ger `409 DuplicateId` i stället för en andra order.
- **Avvisningar** blir problem-svar (RFC 9457) med fältet `reason`: 404 för okänt konto, order, position eller golv, 409 för id som redan använts och 422 för övriga.
- **JSON:** camelCase och enums som text. Händelser, kommandon och golvregler har fältet `kind` med typens namn. Belopp skickas som JSON-tal, och tal skrivna som text godtas inte.
- **Takt:** händelser skickas direkt. Priser skickas högst var 100:e ms och kontot högst var 250:e ms, och bara när de ändrats.
- **Administration** (skapa trader och konto, sätta golv, stänga konto) ligger under `/api/admin` och kräver firmans API-nyckel.
- **Inloggning** med cookie för traders, och ägarskap för konton, beskrivs i ADR 0009.

## Konsekvenser

- Motorns typer används direkt i API:t. Det är enkelt så länge terminal och motor hör till samma produkt, men en ändring i motorns typer ändrar API:t.
- JSON-tal räcker för visning eftersom gränssnittet inte räknar. Om ett publikt API för algohandel byggs kan belopp behöva skickas som text.
- Tjänsten kör bara i miljön Development tills HTTPS, hemligheter och ett prisflöde med licens finns.
