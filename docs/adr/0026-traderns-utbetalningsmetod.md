# 0026. Traderns utbetalningsmetod sparas krypterad och följer med utbetalningen

- Status: Föreslagen
- Datum: 2026-10-04

## Sammanhang

En funded trader kunde begära en utbetalning i portalen, men firman fick aldrig veta vart pengarna skulle. Den måste fråga tradern på annat sätt, vilket fördröjer utbetalningen och är en risk för bedrägeri när uppgifterna kommer i ett vanligt mejl.

## Beslut

- **Tradern anger hur den vill få betalt under Payouts i portalen**: en banköverföring (kontoinnehavare, IBAN eller kontonummer, och valfritt BIC, SWIFT eller routingnummer och bank), krypto (valuta, nätverk och plånbokens adress) eller annat med en egen beskrivning, till exempel en PayPal-adress. Fälten för andra slag tas bort, och varje fält får vara högst 200 tecken.
- **Metoden sparas krypterad** i `trader_payout_methods` med `SecretProtector` och ett syfte per trader, som firmornas hemligheter ([ADR 0017](0017-firmor-registrerar-sig-sjalva.md)). Den är personlig och leder till pengar.
- **En utbetalning i portalen kräver en metod.** Utan en svarar begäran 409 med orsaken, och portalen visar en länk till Payouts i stället för att knappen fungerar.
- **Utbetalningen får en kopia av metoden när den begärs**, krypterad i `payouts.payout_details`. En ändring efteråt flyttar inte en utbetalning som redan är på väg, så att ingen kan byta mottagare efter att firman kontrollerat den.
- **Firman ser metoden vid varje utbetalning** (`payTo`) i adminpanelen och i firmans API, med knappar som kopierar kontonumret eller adressen, och i dialogen där utbetalningen markeras som betald.
- **Utbetalningar som firmans eget system begär åt tradern** får ingen metod. Firman har då uppgifterna själv.

## Konsekvenser

- Vi kontrollerar inte att kontot eller adressen finns eller tillhör tradern. Firman gör sina egna kontroller, som KYC, innan den godkänner.
- Uppgifterna lämnar aldrig tjänsten okrypterade utom till tradern själv och firmans administratörer. Vår personal ser dem inte.
- En ny nyckel i `Secrets:Key` gör de sparade metoderna oläsbara, som andra hemligheter. Byte av nyckel behöver en omkryptering innan produktion.
