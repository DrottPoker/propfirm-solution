# Handelsterminal

Webbgränssnittet för traders i handelsplattformen (Produkt 1): kontorad med golv, symbollista, graf, orderpanel, positioner, ordrar, historik och händelser. Se [specen för handelsterminalen](../../docs/spec/handelsterminal.md).

Kör lokalt med `pnpm dev` på http://localhost:3001. Handelstjänsten måste vara igång. Adressen till den kan ändras med `NEXT_PUBLIC_TRADING_API_URL`.

| Kommando | Vad det gör |
|---|---|
| `pnpm generate:api` | Genererar `src/lib/api/schema.ts` från kontraktet `contracts/trading/trading-service.json` |
| `pnpm test` | Enhetstester med Vitest |
| `pnpm lint`, `pnpm typecheck`, `pnpm build` | Kontroller som också körs i CI |

Får inte importera kod från `prop/`. Se [ADR 0001](../../docs/adr/0001-monorepo-med-produktgranser.md).
