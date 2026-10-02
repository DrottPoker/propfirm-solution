# 0001. Monorepo med hårda gränser mellan produkterna

- Status: Beslutad
- Datum: 2026-10-02

## Sammanhang

Vi bygger två produkter: en handelsplattform och en propfirm-plattform. De ska kunna säljas var för sig eller tillsammans. Kunderna köper en tjänst och ser aldrig koden. Det som avgör om en produkt kan säljas ensam är alltså hur den körs, inte hur koden är organiserad.

Kontraktet mellan produkterna kommer att ändras ofta i början. Med separata repon måste varje ändring publiceras som ett versionerat paket och uppdateras på två ställen.

## Beslut

All kod ligger i ett repo, ordnat efter produkt:

| Mapp | Innehåll |
|---|---|
| `trading/` | Produkt 1: handelsplattformen |
| `prop/` | Produkt 2: propfirm-plattformen |
| `contracts/` | Kontraktet mellan produkterna. Det enda de delar. |
| `shared/` | Generell kod utan affärslogik |

Reglerna:

1. En produkt får bara använda kod från sin egen mapp, `contracts/` och `shared/`.
2. `contracts/` och `shared/` får inte använda kod från någon produkt.
3. Varje produkt har egen databas och egen driftsättning.
4. Varje produkt fungerar ensam. Handelsplattformen har egen inloggning och eget API för konton. Propfirm-plattformen kan testas mot en låtsasversion av handelsplattformen.

Reglerna kontrolleras automatiskt:

- **.NET:** `Directory.Build.targets` stoppar bygget med felet `BOUNDARY001` om ett projekt refererar till ett projekt i en annan produkt.
- **Webb:** ESLint-regeln `no-restricted-imports` stoppar import från den andra produkten. pnpm tillåter bara import av paket som finns som beroende.
- **Databas:** Varje produkt har egen databas och egen roll, och ingen roll kan ansluta till den andras databas.

## Konsekvenser

- Kontraktet och båda sidorna kan ändras i samma commit, och CI kontrollerar att de passar ihop.
- Ett verktyg för allt: ett CI-flöde, en kodstil och en plats för dokumentation.
- Om en produkt behöver brytas ut, till exempel vid försäljning av den ena, kan dess mapp flyttas till ett eget repo med historiken kvar. Det kräver att gränserna hålls rena, vilket kontrollerna säkerställer.
