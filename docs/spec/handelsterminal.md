# Spec: handelsterminalen

- Fas: 3a och 3b
- Status: Implementerad i `trading/terminal`
- Datum: 2026-10-02

## Syfte

Webbgränssnittet där traders handlar på sitt simulerade konto. Terminalen pratar bara med handelstjänsten (se [specen för handelstjänsten](handelstjanst.md)).

## Inloggning och firmans utseende

- **Firmans utseende** hämtas på servern från `GET /api/branding` med adressen terminalen öppnades på. Namn och färger sätts innan sidan visas, så att standardutseendet aldrig blinkar förbi. Färgerna blir CSS-variabler och kontrolleras en gång till i terminalen. Utan svar från tjänsten visas standardutseendet.
- **Inloggning** på `/login` med e-post och lösenord. Sidan visar firmans namn och logga.
- **Spärr:** den som inte är inloggad skickas till `/login`.
- **Konto:** terminalen visar det första kontot tradern äger, eller det som anges med `?account=` om tradern äger det.
- **Utloggning** finns i kontoraden, bredvid traderns e-postadress.

## Layout

```
┌──────────────────────────────────────────────────────────────────┐
│ Firma  Status  Konto  Saldo  Equity  Marginal  Nivå   e-post Ut  │
│ Golv: nivå och hur långt equity kan falla innan det bryts        │
├───────────┬─────────────────────────────────────┬────────────────┤
│ Symboler  │ Graf: candles, tidsramar, linjer    │ Ordertyp       │
│ bid / ask │ för öppna positioner, SL och TP     │ Volym, pris    │
│           │                                     │ SL, TP         │
│           │                                     │ SÄLJ / KÖP     │
│           │                                     │ Villkor        │
├───────────┴─────────────────────────────────────┴────────────────┤
│ Positioner │ Ordrar │ Historik │ Händelser                       │
└──────────────────────────────────────────────────────────────────┘
```

- **Kontoraden** visar golven och hur mycket equity kan falla innan varje golv bryts. Värdet kommer från motorn (`headroom`).
- **Orderpanelen** visar gruppens villkor för symbolen: hävstång, påslag på spreaden, provision och kontraktsstorlek. Det är en del av öppenheten mot traders.
- **Händelser** listar allt som hänt kontot. Avvisningar, brott mot golv och stop out markeras i gult. Vid brott mot ett golv visas priserna från beviset.

## Dataflöde

| Data | Källa | Uppdatering |
|---|---|---|
| Instrument och villkor | `GET /instruments` | En gång |
| Candles | `GET /candles/{symbol}` | Vid byte av symbol eller tidsram. Den senaste candlen uppdateras med livepriser. |
| Priser | SignalR `Prices` | Högst var 100:e ms |
| Kontot | SignalR `Account` | Högst var 250:e ms |
| Händelser | `GET /events` vid start, därefter SignalR `Events` | Direkt |
| Kommandon | `POST /orders`, `DELETE /orders/{id}`, `POST /positions/{id}/close`, `PUT /positions/{id}/stops` | Svaret innehåller händelserna |

- **Gränssnittet räknar aldrig pengar.** Equity, vinst, marginal och avståndet till golven kommer från motorn och visas som de är.
- **Inmatning kontrolleras innan den skickas.** Priser får inte ha fler decimaler än instrumentet, och volymen ska följa instrumentets gränser och steg. Motorn kontrollerar allt igen.
- **Order-id skapas i webbläsaren** (UUID), så att ett anrop som skickas igen inte kan lägga ordern två gånger.
- **Återanslutning:** SignalR återansluter automatiskt. Efter en återanslutning hämtas de händelser som missades via `GET /events?after=`. Om tjänsten inte går att nå vid start försöker terminalen igen varannan sekund.

## Typer från API:t

1. Handelstjänsten genererar `trading/terminal/openapi/trading-service.json` när den byggs.
2. `pnpm generate:api` genererar `src/lib/api/schema.ts` från dokumentet.
3. CI kontrollerar att båda filerna är committade och aktuella. En ändring i API:t som inte når terminalen stoppas alltså i CI.

## Teknik

- Next.js 16, React 19, TypeScript och Tailwind CSS.
- TradingView Lightweight Charts för grafen (se [ADR 0007](../adr/0007-graf-lightweight-charts.md)). TradingViews logga visas i grafen enligt licensen.
- Zustand för livedata och TanStack Query för anrop.
- `openapi-fetch` för typade anrop och `@microsoft/signalr` för realtid.

## Begränsningar

- Panelerna har fast storlek, och layouten är gjord för datorskärm.
- Inget byte eller återställning av lösenord.
- Grafen saknar ritverktyg och indikatorer.
- Tider i grafen visas i UTC.

## Tester

- Enhetstester med Vitest för inmatning, candles, händelsetexter, sammanslagning av händelser och firmans färger (`pnpm test`).
- Tester av hela flödet med Playwright (`pnpm e2e`): spärren och firmans inloggningssida, fel lösenord, inloggning, köp, stängning, historik, händelser och utloggning, och att en trader bara ser sitt eget konto. Testerna startar en egen tjänst på port 5121 och en egen terminal på port 3021 mot databasen `trading_e2e`, som töms före varje körning. De kan alltså köras medan du utvecklar. Postgres från `deploy/docker-compose.yml` måste vara igång.
- Lint, typkontroll och bygge i CI.
