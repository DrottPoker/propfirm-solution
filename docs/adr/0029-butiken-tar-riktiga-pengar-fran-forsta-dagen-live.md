# 0029. Butiken tar riktiga pengar från första dagen live

- Status: Föreslagen
- Datum: 2026-10-04

## Sammanhang

En firma i sandlådan säljer med testbetalningar eller Stripes testnycklar ([ADR 0019](0019-kop-i-portalen-med-firmans-betalningsleverantor.md)). Testbetalningar slutar fungera när firman går live, och livenycklar kunde inte sparas i sandlådan. En firma som gick live med testbetalningar fick alltså en stängd butik utan att något sa det. Att koppla in Stripe krävde dessutom att firman lade till en webhook i Stripe för hand, med rätt adress och sex händelser, och klistrade in dess signeringshemlighet. Och adminpanelens siffror räknade testköpen och testkontona från sandlådan också efter att firman gått live.

## Beslut

- **Livenycklar kan sparas i sandlådan**, men tar betalt först när firman är live. Testnycklar tar testbetalningar i sandlådan. Butiken tar då riktiga pengar från första dagen.
- **En firma kan inte gå live med en butik som skulle sluta ta betalt.** Go-live nekas med orsaken och en länk till Checkout när butiken tar testbetalningar eller har testnycklar där de slutar live. Det gäller inte firmor som valt att inte sälja i portalen. I utveckling, där testbetalningar får fortsätta live, nekas inget.
- **En firma som är live kan inte välja testbetalningar eller spara testnycklar** där de inte tar betalt live. Skulle butiken ändå inte ta betalt, till exempel efter en ändrad konfiguration, står det först under Needs you i adminpanelen.
- **Webhooken läggs till i firmans Stripe-konto åt firman** när den sparar sin hemliga nyckel. Tjänsten tar bort webhooks som redan går till firmans adress och lägger till en ny med händelserna som orderna behöver, och sparar dess signeringshemlighet. Nekar Stripe, till exempel för en begränsad nyckel eller en adress som inte nås från internet, säger tjänsten varför, och firman lägger till webhooken själv med adressen och händelserna som adminpanelen visar.
- **Ordrar och konton markeras när de görs i sandlådan** (`sandbox`). Adminpanelens siffror räknar dem medan firman är kvar i sandlådan, så att den ser hur siffrorna fungerar, men inte efter att den gått live. Vår adminvy räknar aldrig dem i siffrorna över utbetalningar.

## Konsekvenser

- En firma som vill prova riktiga betalningar innan den går live kan inte det. Stripes testläge räcker för att se flödet.
- Vi lägger till och tar bort webhooks i firmans Stripe-konto med firmans nyckel. Firmor som inte vill det använder en begränsad nyckel och lägger till webhooken själva.
- Lokalt nås tjänsten inte från internet, så Stripe nekar webhooken där. Utvecklare lägger till den själva, till exempel med Stripes CLI.
