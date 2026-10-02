# Spec: handelsterminalen

- Fas: 3a
- Status: Implementerad i `trading/terminal`
- Datum: 2026-10-02

## Syfte

Webbgränssnittet där traders handlar på sitt simulerade konto. Terminalen pratar bara med handelstjänsten (se [specen för handelstjänsten](handelstjanst.md)).

## Layout

```
┌──────────────────────────────────────────────────────────────────┐
│ Status  Konto  Saldo  Equity  Marginal  Fri marginal  Nivå       │
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

## Konto

Det finns ingen inloggning än. Kontot väljs i adressen, till exempel `http://localhost:3001/?account=demo`. Standard är `demo`.

## Begränsningar i fas 3a

- Ingen inloggning och ingen white label.
- Panelerna har fast storlek, och layouten är gjord för datorskärm.
- Grafen saknar ritverktyg och indikatorer.
- Tider i grafen visas i UTC.
- Inga tester av hela flödet i webbläsaren. Det kommer med Playwright.

## Tester

- Enhetstester med Vitest för inmatning, candles, händelsetexter och sammanslagning av händelser (`pnpm test`).
- Lint, typkontroll och bygge i CI.
