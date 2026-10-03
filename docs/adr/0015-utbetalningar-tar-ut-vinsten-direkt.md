# 0015. Utbetalningar tar ut vinsten direkt

- Status: Föreslagen
- Datum: 2026-10-03

## Sammanhang

En funded trader ska kunna begära ut sin vinst i portalen. Firman kontrollerar, till exempel KYC, godkänner, skickar pengarna själv och markerar utbetalningen som betald. Vi hanterar aldrig pengarna (se produktplanen).

Mellan begäran och betalningen kan det gå flera dagar. Om vinsten ligger kvar på kontot under tiden kan tradern förlora den igen, och firman betalar då ut pengar som inte längre finns på kontot. Uttaget måste också göras på handelsplattformen, som då behöver insättningar och uttag. Ett uttag får inte heller räknas som en förlust, för då skulle golven för daglig och total förlust brytas direkt.

## Beslut

- **Hela vinsten tas ut när tradern begär utbetalningen.** Kontot börjar om från startsaldot, och tradern får vinstandelen av vinsten. Firman behåller resten. Tradern måste ha stängt alla positioner, så att vinsten är bestämd.
- **Handelsplattformen får insättningar och uttag** (`AdjustBalance` i motorn och `POST /api/admin/v1/accounts/{id}/balance-operations` i admin-API:t). Varje operation har ett id som anroparen väljer och som bara kan användas en gång per konto, så ett nytt försök efter ett avbrott dras aldrig två gånger. Ett uttag kan kräva ett minsta saldo efteråt.
- **En insättning eller ett uttag är inget handelsresultat.** Golv som mäts från kontot följer med saldot: ett förankrat golvs startpunkt och ett släpande golvs högsta equity flyttas lika mycket. Fasta nivåer och låsnivåer ligger kvar. Ett uttag som skulle bryta ett golv, gå under minsta saldot eller ta mer än den fria marginalen avvisas.
- **Regelmotorn äger utbetalningen**, som resten av kontots livscykel. Den avgör om en utbetalning kan begäras, räknar ut beloppet och tar utbetalningen från begäran till betald, och varje steg sparas i kontots journal (ADR 0013). Samma beräkning visar traderns nästa utbetalning i portalen.
- **Uttaget går genom utkorgen** med utbetalningens id som operationens id och startsaldot som minsta saldo. Utbetalningen väntar på firman först när uttaget syns i händelseströmmen. Har en position stängts med förlust just innan nekar handelsplattformen uttaget, och tjänsten rapporterar nejet till regelmotorn i samma transaktion som kommandot läggs åt sidan. Utbetalningen blir då `Failed` och kontot är som innan.
- **En utbetalning åt gången.** Nästa kan begäras när den förra är betald eller nekad, och när funded-fasens minsta antal handelsdagar har gått sedan förra utbetalningen.
- **Nekar firman en utbetalning återförs inte vinsten.** Firman nekar när tradern har brutit mot villkoren. Väntar firman på något, till exempel KYC, låter den utbetalningen vänta.

## Konsekvenser

- Firman betalar aldrig ut pengar som tradern har hunnit förlora, och tradern kan handla vidare medan firman kontrollerar.
- Kontots saldo visar alltid det tradern handlar med. Uttaget syns i terminalens historik.
- Firmor som använder handelsplattformen utan propfirm-plattformen kan göra egna insättningar och uttag.
- En insättning på ett konto i en utvärderingsfas räknas in i saldot som vinstmålet mäts på. Propfirm-plattformen gör aldrig det, men en firma som gör det direkt på handelsplattformen påverkar sin trader.
- Delutbetalningar, automatiska utbetalningar och regeln om jämna resultat kan läggas till senare utan att ändra flödet.
