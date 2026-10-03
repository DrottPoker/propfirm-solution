# Spec: köp i portalen

- Fas: 8
- Status: Implementerad i `prop/src/Prop.Api/Payments` och `prop/portal`
- Datum: 2026-10-03

## Syfte

En trader ska kunna köpa en challenge direkt i firmans portal. Pengarna går till firmans egen betalningsleverantör, och kontot startar när betalningen är bekräftad. Firman väljer leverantör själv. Besluten finns i [ADR 0019](../adr/0019-kop-i-portalen-med-firmans-betalningsleverantor.md). Portalen beskrivs i [specen för portalen](portal.md) och propfirm-tjänsten i [specen för propfirm-tjänsten](propfirm-tjanst.md).

## Flöde

```
/buy på firmans adress -> tradern väljer challenge, anger e-post (eller är inloggad) och godkänner firmans villkor
-> POST /api/portal/orders -> ordern sparas med priset just nu, status Pending
-> tradern skickas till leverantörens betalsida (Stripe, firmans egen sida eller testsidan)
-> leverantören bekräftar betalningen (Stripes webhook, firmans API eller testsidan)
-> i en transaktion: ordern blir Paid, kontot startas och webhooken order.paid köas
-> tradern kommer tillbaka till /orders/{id}?token= och ser att betalningen är klar
-> en trader utan lösenord får en inbjudan till portalen per e-post
```

## Order

| Status | Betyder |
|---|---|
| `Pending` | Väntar på betalning. |
| `Paid` | Betald. Kontot är startat, eller `problem` säger varför inte, till exempel att ingen plats var ledig. |
| `Expired` | Ingen betalning inom `Payments:OrderLifetime` (standard 1 timme), eller så sa leverantören att betalningen inte blir av. En betalning som kommer senare räknas ändå. |

- Ordern har ett nummer per firma som börjar på 1001, köparens e-post, challengen, priset och valutan, leverantören, leverantörens id för betalningen och kontot den startade.
- `refundedAt` och `disputedAt` sätts när pengarna har gått tillbaka eller köparen har bestridit betalningen. Kontot rörs inte, utan firman avgör om det ska avbrytas.
- Kontot startas med challengen som den är när betalningen kommer.
- **En order som väntar på betalning håller en plats** av firmans platser, så att köparen alltid har en plats när betalningen kommer (se [specen för platser och betalning](platser-och-betalning.md)). En order som går ut lämnar tillbaka platsen. Kommer betalningen ändå när ingen plats är ledig, eller när firmans månad är obetald, blir ordern `Paid` utan konto och med en förklaring i `problem`. Firman startar då kontot själv när det går.
- Samma betalning flera gånger ger aldrig mer än ett konto, eftersom ordern låses när den markeras som betald.
- Allt som händer med en order sparas i `order_events` med vem som sa det (`buyer`, `stripe`, `firm-api` eller `admin`) och leverantörens meddelande som bevis.

## Priser

Priset ligger utanför challengen, som bara innehåller regler. Ett pris har belopp, valuta och om challengen säljs i portalen. Beloppet är 1 till 100 000 i hela cent. Valutan är en av AED, AUD, CAD, CHF, CZK, DKK, EUR, GBP, HKD, NOK, NZD, PLN, SEK, SGD och USD, och kan vara en annan än kontots. En order behåller priset den skapades med.

## Leverantörer

| Leverantör | Betalsida | Vem säger att ordern är betald |
|---|---|---|
| `Test` | `/checkout/test?order=&token=` i portalen, där tradern trycker på Pay utan att några pengar dras | Köparen på testsidan. Bara för firmor i sandlådan, och i utveckling också för firmor som är live (`Payments:TestPaymentsForLiveFirms`). |
| `Stripe` | En Stripe Checkout Session som skapas med firmans egen hemliga nyckel. Pengarna går till firmans Stripe-konto. | Stripes webhook till `/api/payments/v1/stripe/{firma}`, signerad med firmans signeringshemlighet. |
| `External` | Firmans egen sida, med `order` och `return` i adressen | Firmans system med `POST /api/firm/v1/orders/{id}/mark-paid`, eller en administratör i adminpanelen. |

En firma utan leverantör säljer inget i portalen. Butiken är öppen när leverantören fungerar, minst en challenge har ett pris och säljs, och firman har en ledig plats och en betald månad. Är alla platser tagna säger butiken att inga nya challenges kan köpas just nu.

### Stripe

- Sessionen skapas med `mode=payment`, en rad med challengens namn och priset i cent, köparens e-post, `client_reference_id` och `metadata[order_id]` med orderns id, `payment_intent_data[metadata][order_id]`, `success_url` till orderns sida, `cancel_url` till `/buy` och `expires_at` när ordern går ut. `Idempotency-Key` är `order-{id}`, så ett nytt försök skapar ingen ny session.
- Svarar Stripe inte, eller nekar, skapas ingen order och köparen får 503.
- Webhooken kräver headern `Stripe-Signature` (`t=...,v1=...`) med HMAC-SHA256 av `{t}.{kropp}` och firmans signeringshemlighet. Signaturen får vara högst 5 minuter gammal. Annars svarar den 400.
- Händelser som används:

| Händelse | Blir |
|---|---|
| `checkout.session.completed`, `checkout.session.async_payment_succeeded` med `payment_status` `paid` | Ordern blir betald. Sessionen måste vara orderns, och beloppet och valutan måste stämma med ordern. Annars sparas händelsen som `payment_mismatch` och inget startas. |
| `checkout.session.expired`, `checkout.session.async_payment_failed` | En order som inte är betald går ut. |
| `charge.refunded` med `refunded: true` | Ordern med samma PaymentIntent markeras som återbetald. |
| `charge.dispute.created` | Ordern med samma PaymentIntent markeras som bestridd. |

  Andra händelser svaras med 200 och används inte, så att Stripe inte skickar dem igen.
- I sandlådan tas bara Stripes testnycklar emot (`sk_test_` eller `rk_test_`). Livenycklar fungerar när firman är live.

### Firmans egen sida

Tradern skickas till firmans adress med `order={id}&return={orderns sida}`. Firmans sida läser ordern med `GET /api/firm/v1/orders/{id}`, tar betalt och anropar `POST /api/firm/v1/orders/{id}/mark-paid` med en valfri `{ "reference" }`, till exempel leverantörens id för betalningen. Sedan skickar den tradern till `return`. En återbetalning markeras med `POST /api/firm/v1/orders/{id}/mark-refunded`.

## Inbjudan efter köpet

- En trader som är inloggad i portalen köper med sin egen e-postadress, och kontot syns direkt bland traderns konton.
- Har tradern inget lösenord mejlar plattformen en inbjudan när ordern är betald. Mejlet har firmans namn som avsändare och gäller en gång i 7 dagar. Inbjudan till e-postadressen visar att köparen äger den, så ingen kan köpa sig in på någon annans konton.
- Kunde mejlet inte skickas kan köparen be om det igen från orderns sida, tidigast en minut efter förra gången. Firman kan också skapa en inbjudan på kontots sida i adminpanelen.

## API

### Portalen

Vägarna börjar med `/api/portal` och finns på firmans adress. Köparen behöver inte vara inloggad. Orderns vägar kräver token från köparens länk, och en okänd order eller fel token svarar 404.

| Metod och väg | Beskrivning |
|---|---|
| `GET /shop` | `open`, `full` (stängd för att inga platser är lediga), `test` (testbetalningar), firmans `termsUrl` och challengerna till salu med pris och valuta, billigast först. |
| `POST /orders` | `{ "challengeId", "email", "acceptTerms" }`. Svarar 201 med `orderId`, `number` och `checkoutUrl`. En inloggad trader köper med sin egen e-post. 409 när butiken är stängd eller ingen plats är ledig, 404 för en challenge som inte säljs, 422 för fel e-post eller villkor som inte är godkända, 503 när leverantören inte svarar. Samma gräns för försök som inloggningen. |
| `GET /orders/{id}?token=` | Ordern för köparen: status, challengens namn, belopp, betalsidan medan den väntar, kontot, om tradern redan har lösenord och när inbjudan skickades. |
| `POST /orders/{id}/invite` | `{ "token" }`. Skickar inbjudan igen. 202, 409 när tradern redan har lösenord eller ordern inte har startat något, 429 inom en minut från förra, 503 när mejlet inte gick iväg. |
| `POST /orders/{id}/test-payment` | `{ "token" }`. Betalar en testorder. 409 för andra ordrar, en order som gått ut och när firman inte får ta testbetalningar. |

I adminpanelen:

| Metod och väg | Beskrivning |
|---|---|
| `GET /admin/orders?status=&limit=` | Firmans nyaste ordrar, högst 500, valfritt med en status. |
| `GET /admin/orders/{id}` | Ordern med allt som hänt den. |
| `POST /admin/orders/{id}/mark-paid` | Markerar en order från firmans egen sida som betald, med `{ "reference" }`. Kontot startar. 409 för Stripe- och testordrar. |
| `POST /admin/orders/{id}/mark-refunded` | Markerar en betald order som återbetald. 409 för Stripe-ordrar, som Stripe rapporterar själv. |
| `GET /admin/prices`, `PUT /admin/challenges/{id}/price` | Priserna, och ett pris med `{ "amount", "currency", "forSale" }`. 422 för ett ogiltigt pris, 404 för en okänd challenge. |
| `PUT /admin/firm/payments` | `{ "provider", "stripeSecretKey", "stripeWebhookSecret", "checkoutUrl", "termsUrl" }`. Tomma Stripe-nycklar behåller de sparade. 422 för testbetalningar hos en firma som är live, Stripe utan nycklar, livenycklar i sandlådan, egen sida utan adress och adresser som inte är https. |

`GET /admin/firm` har `payments`: vald leverantör, om den fungerar nu, om testbetalningar är tillåtna, om Stripe-nycklar finns och i vilket läge, adressen för Stripes webhook, firmans betalsida och villkor. Nycklarna visas aldrig.

### Firmans API

| Metod och väg | Beskrivning |
|---|---|
| `GET /prices`, `PUT /challenges/{id}/price` | Som i adminpanelen. |
| `GET /orders?status=&limit=`, `GET /orders/{id}` | Som i adminpanelen. |
| `POST /orders/{id}/mark-paid`, `POST /orders/{id}/mark-refunded` | Som i adminpanelen. Samma `mark-paid` igen svarar 200 med samma order. |

### Betalningsleverantörer

`POST /api/payments/v1/stripe/{firma}` tar emot Stripes webhooks. Den finns inte i OpenAPI-dokumentet, eftersom bara Stripe anropar den, och måste nås från internet (ADR 0018).

## Webhooks till firman

| Händelse | När |
|---|---|
| `order.paid` | En order är betald. `account` är kontot den startade, och `data.order` är ordern. |
| `order.refunded` | Pengarna har gått tillbaka till köparen. |
| `order.disputed` | Köparen har bestridit betalningen. |

## Konfiguration

| Inställning | Innehåll |
|---|---|
| `Platform:ApiUrl` | Där internet når propfirm-tjänsten, som slutar med `/`. Adminpanelen visar firmans adress för Stripes webhook från den. Lokalt http://localhost:5201/. |
| `Payments:OrderLifetime` | Hur länge köparen har på sig att betala, 30 minuter till 23 timmar som Stripe tillåter. Standard 1 timme. |
| `Payments:StripeApiUrl` | Stripes API, standard https://api.stripe.com/. |
| `Payments:TestPaymentsForLiveFirms` | Om firmor som är live också får ta testbetalningar. Bara för utveckling. |
| `Firms:N:Payments` | En konfigurerad firmas `Provider` (`Test`, `Stripe` eller `External`), `StripeSecretKey`, `StripeWebhookSecret`, `CheckoutUrl` och `TermsUrl`. |
| `Firms:N:SeedChallenges:M:Price` | Priset i portalen i challengens valuta. En challenge utan pris säljs inte. |

I utveckling säljer `demo-firm` `two-step-100k` för 499 USD och `quick-test-100k` för 9 USD med testbetalningar, på http://localhost:3002/buy.

## Begränsningar

- Inga rabattkoder, ingen moms och inga prenumerationer. Kvitton skickas av Stripe, och firmans egen sida får sköta dem själv.
- En delvis återbetalning i Stripe markerar inte ordern.
- Bara Stripe har en färdig adapter. Andra leverantörer kopplas in med firmans egen sida tills de får egna adaptrar.
- Inbjudan efter köpet skickas från plattformens e-postadress med firmans namn, inte från firmans domän.

## Tester

- `prop/tests/Prop.Api.Tests/SlotTests`: en order som håller den sista platsen åt sin köpare, butiken som stänger, en order som går ut och lämnar tillbaka platsen och en betalning som kommer när ingen plats är ledig.
- `prop/tests/Prop.Api.Tests/OrderFlowTests`: butiken som öppnar när firman tar betalt och har ett pris, testbetalning med inbjudan som fungerar, en inloggad trader som köper med sin egen e-post, Stripe-sessionen med firmans nyckel och idempotensnyckel, Stripes webhook som startar kontot exakt en gång, fel signatur och fel firma, ett belopp som inte stämmer, återbetalning och bestridande med webhooks till firman, en betalning efter att sessionen gått ut, Stripe som nekar, bara testnycklar i sandlådan, nycklar som sparas men aldrig visas, firmans egen sida med firmans API och priset som ordern behåller, att bara egna sidans ordrar markeras för hand, priser som kontrolleras, full sandlåda, firmans villkor, inbjudan som skickas igen, en testorder som gått ut, testbetalningar bara i sandlådan utanför utveckling och att ordrar är firmans egna. Stripe är en låtsad version (`FakeStripe`) som tar emot sessionerna och gör signerade händelser.
- `prop/portal/src/lib/orders.test.ts`: vilka ordrar firman kan markera, orderns anteckning och vad köparens ordersida säger.
- `prop/portal/e2e/shop.spec.ts`: en besökare köper med testbetalning och firman ser den betalda ordern och kontot, och en inloggad trader köper och kommer direkt till sitt nya konto.
