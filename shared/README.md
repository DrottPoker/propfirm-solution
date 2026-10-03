# Delad kod

Generell kod som båda produkterna kan använda, till exempel typer för pengar, loggning och hälsokontroller.

| Projekt | Innehåll |
|---|---|
| `src/Common.Postgres` | Migreringar av databasen från SQL-filer i produktens assembly, och en spärr som ser till att de körts innan första frågan. |

Regler:

- Ingen affärslogik. Det som rör handel hör hemma i `trading/`, och det som rör challenges i `prop/`.
- Får inte använda kod från någon produkt.
- Lägg bara kod här när båda produkterna faktiskt behöver den.

Se [ADR 0001](../docs/adr/0001-monorepo-med-produktgranser.md).
