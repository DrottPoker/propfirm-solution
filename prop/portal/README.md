# Portal

Firmans vitmärkta traderportal och adminpanel i propfirm-plattformen (Produkt 2). Se [specen för portalen](../../docs/spec/portal.md) och [ADR 0014](../../docs/adr/0014-vitmarkt-portal-pa-firmans-adress.md).

Kör lokalt med `pnpm dev` på http://localhost:3002. Propfirm-tjänsten måste vara igång, och portalen når den på `PROP_API_URL` (standard http://localhost:5201).

| Kommando | Gör |
|---|---|
| `pnpm test` | Enhetstester med Vitest |
| `pnpm e2e` | Hela kedjan med Playwright, se README i roten |
| `pnpm generate:api` | Typerna i `src/lib/api/schema.ts` från `openapi/prop-api.json` |

Får inte importera kod från `trading/`. Se [ADR 0001](../../docs/adr/0001-monorepo-med-produktgranser.md).
