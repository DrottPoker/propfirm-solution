# Personalpanelen

Vår personals panel över Kronant Trader (Produkt 1): det som behöver oss, alla servrar, prisflödet, instrumenten och öppettiderna, vad traders håller och motorn. Här gör personalen servrar åt firmor som använder plattformen på egen hand, ändrar deras listning, stoppar admin-nycklar som kan ha läckt och ber prisflödet om historik igen. Se [specen för personalpanelen](../../docs/spec/personalpanelen.md) och [ADR 0057](../../docs/adr/0057-personalpanel-for-handelsplattformen.md).

Kör lokalt med `pnpm dev` på http://localhost:3003 och logga in med `ops@test.com` och lösenordet `ops`. Handelstjänsten måste vara igång. Adressen till den kan ändras med `NEXT_PUBLIC_TRADING_API_URL`, och miljöns namn överst på sidorna med `NEXT_PUBLIC_ENVIRONMENT_NAME`.

| Kommando | Vad det gör |
|---|---|
| `pnpm generate:api` | Genererar `src/lib/api/schema.ts` från kontraktet `contracts/trading/trading-service.json` |
| `pnpm test` | Enhetstester med Vitest |
| `pnpm e2e` | Playwright mot en egen handelstjänst och databas. Bygg först tjänsten med `dotnet build trading/src/Trading.Service -c Release`. |
| `pnpm lint`, `pnpm typecheck`, `pnpm build` | Kontroller som också körs i CI |

Får inte importera kod från `prop/`. Se [ADR 0001](../../docs/adr/0001-monorepo-med-produktgranser.md).
