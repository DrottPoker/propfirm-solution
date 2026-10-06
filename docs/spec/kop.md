# Spec: köp i portalen

- Fas: 8, webhooken i Stripe, livenycklar i sandlådan, notisen om försäljning, pristabellerna, köparens namn och land, lösenordet direkt efter köpet, rabattkoder och nya försök efter genomgången som ny firma, kvittot i mejlet och som PDF efter genomgången av UI och UX
- Status: Implementerad i `prop/src/Prop.Api/Payments` och `prop/portal`
- Datum: 2026-10-06

## Syfte

En trader ska kunna köpa en challenge direkt i firmans portal. Pengarna går till firmans egen betalningsleverantör, och kontot startar när betalningen är bekräftad. Firman väljer leverantör själv. Besluten finns i [ADR 0019](../adr/0019-kop-i-portalen-med-firmans-betalningsleverantor.md), [ADR 0029](../adr/0029-butiken-tar-riktiga-pengar-fran-forsta-dagen-live.md) och [ADR 0034](../adr/0034-losenord-direkt-efter-kopet.md). Portalen beskrivs i [specen för portalen](portal.md) och propfirm-tjänsten i [specen för propfirm-tjänsten](propfirm-tjanst.md).

## Flöde

```
/ på firmans adress -> en besökare skickas till /buy, med Log in uppe till höger
/buy -> pristabeller, kontostorlekarna som kolumner och reglerna som rader (och en rad om att bara firmans team kan köpa, när det gäller) -> tradern väljer storlek,
   anger e-post, namn och land (eller är inloggad), kan skriva en rabattkod och godkänner firmans villkor
-> POST /api/portal/orders -> ordern sparas med priset just nu och koden, status Pending
-> tradern skickas till leverantörens betalsida (Stripe, firmans egen sida eller testsidan)
-> leverantören bekräftar betalningen (Stripes webhook, firmans API eller testsidan)
-> i en transaktion: ordern blir Paid, kontot startas, webhooken order.paid och mejlet "New sale" till firmans administratörer köas
-> tradern kommer tillbaka till /orders/{id}?token= och ser att betalningen är klar
-> en ny köpare väljer lösenord direkt där och kommer till sitt konto
-> mejlet efter köpet har en länk som bekräftar e-posten, eller låter en köpare som gick därifrån välja lösenordet,
   och kvittot, reglerna i korthet och hur tradern kommer igång (en köpare som redan har lösenord får en länk till kontot)
-> kvittot finns också som PDF: GET /api/portal/orders/{id}/receipt.pdf?token=
```

## Order

| Status | Betyder |
|---|---|
| `Pending` | Väntar på betalning. |
| `Paid` | Betald. Kontot är startat, eller `problem` säger varför inte, till exempel att ingen plats var ledig. |
| `Expired` | Ingen betalning inom `Payments:OrderLifetime` (standard 1 timme), eller så sa leverantören att betalningen inte blir av. En betalning som kommer senare räknas ändå. |

- Ordern har ett nummer per firma som börjar på 1001, köparens e-post, namn och land, challengen, priset och valutan, leverantören, leverantörens id för betalningen och kontot den startade.
- Namnet (högst 100 tecken) och landet (två bokstäver) krävs. En inloggad trader som angett dem förut behöver inte igen. Tradern behåller namnet och landet från första ordern som angav dem, och firman ser dem på traderkortet och bland ordrarna.
- `refundedAt` och `disputedAt` sätts när pengarna har gått tillbaka eller köparen har bestridit betalningen. Kontot rörs inte, utan firman avgör om det ska avbrytas.
- Kontot startas med challengen som den är när betalningen kommer.
- **En order som väntar på betalning håller en plats** av firmans platser, så att köparen alltid har en plats när betalningen kommer (se [specen för platser och betalning](platser-och-betalning.md)). En order som går ut lämnar tillbaka platsen. Kommer betalningen ändå när ingen plats är ledig, eller när firmans månad är obetald, blir ordern `Paid` utan konto och med en förklaring i `problem`. Firman startar då kontot själv när det går.
- Samma betalning flera gånger ger aldrig mer än ett konto, eftersom ordern låses när den markeras som betald.
- En order som görs medan firman är i sandlådan markeras som det (`sandbox`), liksom kontot den startar. Adminpanelens siffror räknar dem bara medan firman är kvar i sandlådan.
- Allt som händer med en order sparas i `order_events` med vem som sa det (`buyer`, `stripe`, `firm-api` eller `admin`) och leverantörens meddelande som bevis.

## Priser

Priset ligger utanför challengen, som bara innehåller regler. Ett pris har belopp, valuta och om challengen säljs i portalen. Beloppet är 1 till 100 000 i hela cent. Valutan är en av AED, AUD, CAD, CHF, CZK, DKK, EUR, GBP, HKD, NOK, NZD, PLN, SEK, SGD och USD, och kan vara en annan än kontots. En order behåller priset den skapades med.

## Rabattkoder

Se [ADR 0036](../adr/0036-rabattkoder-i-butiken.md) för besluten.

- **En kod** har bokstäver, siffror, - och _, högst 32 tecken, och känns igen utan hänsyn till stora och små bokstäver. Den tar av en procent, 1 till 99 med högst två decimaler, eller ett belopp i hela cent i en av prisvalutorna, och då bara av priser i den valutan. Den gäller alla challenges eller de som firman valt, valfritt högst ett antal gånger och till en sista dag. Firman kan stänga av och sätta på den, och ta bort den så länge ingen order har använt den.
- **Priset med koden** räknas av propfirm-tjänsten. En procent avrundas till hela cent, och priset blir aldrig under 1 i sin valuta, så att leverantören kan ta betalt. Butiken visar priset med koden innan köparen betalar, och knappen betala visar beloppet med den.
- **Hur ofta en kod används** räknas på betalda ordrar och ordrar som väntar på betalning. När ordern sparas låses koden och räknas igen, så att två köpare aldrig tar den sista gången samtidigt. En order som går ut lämnar tillbaka sin gång.
- **Ordern** sparar koden som köparen skrev den och priset före den (`listAmount`). `amount` är vad köparen betalar, och det är det beloppet Stripe får, med koden i produktens namn.
- **Koder för nya försök** (`forRetries`) gäller bara en köpare vars tidigare challenge hos firman underkändes, med samma e-post. Ett underkänt konto vars challenge fortfarande säljs visar "Try again" med priset och den kod för nya försök som ger mest rabatt på challengen, och knappen öppnar butiken med challengen vald och koden ifylld (`/buy?challenge=&code=`).

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
- **Webhooken läggs till åt firman.** Med bara den hemliga nyckeln listar tjänsten webhooks i firmans Stripe-konto, tar bort de som redan går till firmans adress och lägger till en ny med händelserna ovan (`POST /v1/webhook_endpoints` med `enabled_events[]`). Dess signeringshemlighet sparas med nyckeln. Säger Stripe nej, till exempel för en begränsad nyckel eller en adress som inte nås från internet (som lokalt), svarar tjänsten 422 med Stripes orsak, och firman lägger till webhooken själv och klistrar in signeringshemligheten.
- I sandlådan tar testnycklar (`sk_test_` eller `rk_test_`) testbetalningar. Livenycklar kan sparas redan i sandlådan men tar betalt först när firman är live, så att butiken tar riktiga pengar från första dagen. En firma som är live kan inte spara testnycklar, utom där testbetalningar är tillåtna för firmor som är live.

### Firmans egen sida

Tradern skickas till firmans adress med `order={id}&return={orderns sida}`. Firmans sida läser ordern med `GET /api/firm/v1/orders/{id}`, tar betalt och anropar `POST /api/firm/v1/orders/{id}/mark-paid` med en valfri `{ "reference" }`, till exempel leverantörens id för betalningen. Sedan skickar den tradern till `return`. En återbetalning markeras med `POST /api/firm/v1/orders/{id}/mark-refunded`.

## Butiken

- Överst firmans vinstandel som rubrik och vägen i tre steg: klara utvärderingen, bli funded, få betalt.
- **Firmans utbetalningar**, när firman har valt att visa dem under Checkout: vad den betalat ut till traders de senaste 30 dagarna, per valuta i hela belopp som aldrig avrundas uppåt, hur många utbetalningar, och hur många dagar från begäran till betalning i snitt, till exempel "$12,840 paid out to traders in the last 30 days, in 12 payouts" och "Paid 1.5 days after the request on average". Valet är avstängt tills firman sätter på det, eftersom det gör firmans egna siffror publika. Siffrorna räknas som på firmans översikt, så testutbetalningar räknas bara medan firman är i sandlådan, och raden visas inte alls förrän firman har betalat en utbetalning de senaste 30 dagarna. Under Checkout ser firman raden som köparna ser, eller skulle se.
- Challenges med samma regler, utom kontostorleken, och samma valuta på priset är ett program med ett kort: storlekarna som val, priset stort, faserna som en bild, reglerna i korthet och knappen "Start for $349".
- Bakom "Compare every rule" delar de en tabell: storlekarna som kolumner, minst först, och priset, målet per fas, Daily loss limit, Max loss limit, minsta antal handelsdagar, tidsgränsen, vinstdelningen, konsistensregeln när funded har en, och inaktiviteten som rader, i procent och belopp. Tabellen med den billigaste challengen kommer först. På en telefon rullar tabellen i sidled, med radernas namn kvar.
- Vanliga frågor, besvarade från firmans egna regler.
- Den som går till portalens förstasida utan att vara inloggad skickas till butiken när den säljer, annars till inloggningen.

## Lösenordet efter köpet

- En trader som är inloggad i portalen köper med sin egen e-postadress, och kontot syns direkt bland traderns konton.
- Firmans administratörer får mejlet "New sale" med köparen, med namnet först när det finns ("Ann Buyer (ann@example.com)"), challengen, beloppet och ordernumret, om firman inte stängt av det under Notifications (ADR 0025). Det köas i samma transaktion som betalningen.
- En ny köpare väljer lösenord direkt på orderns sida och kommer till sitt konto. Det går bara när ordern startade traderns enda konto och tradern inte har något lösenord, så att någon som skriver en annans e-post aldrig kommer åt konton från förut (`canChoosePassword`).
- Har tradern inget lösenord mejlar plattformen dessutom en inbjudan med kvittot när ordern är betald, i firmans namn och utseende (ADR 0033). Länken gäller en gång i 7 dagar. Har köparen redan valt lösenord bekräftar länken e-posten, annars väljer köparen lösenordet med den. Innan vi har godkänt firman köper bara firmans administratörer (ADR 0043), och orderns sida säger bara att länken mejlats när den har det.
- **Bara teamet köper innan firman får sälja.** Innan vi har godkänt firman, och med firmans egen betalsida innan firman är live, nekas en order från någon annan än firmans administratörer med 403, och `GET /shop` har `teamOnly`, så att butiken säger det. Riktiga pengar tas i portalen först när firman är live (ADR 0029 och 0043).
- E-posten är bekräftad när tradern har öppnat en länk från ett mejl till den: en inbjudan, en länk för nytt lösenord eller en bekräftelse. Utbetalningar kräver det. Portalen visar en ruta med "Send the link again" tills dess.
- Kunde mejlet inte skickas kan köparen be om det igen från orderns sida, tidigast en minut efter förra gången. Firman kan också skapa en inbjudan på kontots sida i adminpanelen, som går till en trader utan lösenord eller utan bekräftad e-post.

## Kvittot

- **Köparen får kvittot i mejlet efter köpet och som PDF** från orderns sida, båda i firmans namn och med samma rader: ordern skriven som "Order 1002" (utan #), dagen den betalades (i UTC), challengen med priset, rabatten med koden när en kod användes, summan som betalades och hur den betalades (testbetalning, Stripe eller firmans egen betalsida), och leverantörens referens när den finns. Challengen är den som kontot startade med, så att en senare ändring av firman inte ändrar kvittot.
- **Ingen momsrad.** Ordrar har ingen moms (se Begränsningar), så kvittot har ingen rad för moms.
- **Mejlet har också reglerna i korthet** från challengen som kontot startade med: kontostorleken, vinstmålet i varje fas, Daily loss limit och Max loss limit (per fas när de skiljer sig, och "trailing" när gränsen följer högsta equity) och vinstdelningen, och "How to get started" i tre steg: välj lösenord eller logga in, öppna terminalen från kontot i portalen, och lägg första affären. Alla regler står på kontots sida i portalen.
- **Vilket mejl.** En köpare utan lösenord får kvittot i inbjudan, som skickas direkt när ordern är betald och kan skickas igen från orderns sida. En köpare som redan har lösenord, till exempel en inloggad trader, får kvittot i ett eget mejl med en länk till det nya kontot. Det mejlet köas i samma transaktion som betalningen (ADR 0025). Bara en order som startade ett konto ger ett mejl. Innan vi har godkänt firman går mejlen bara till dess administratörer (ADR 0043).
- **PDF:en** görs som fakturorna, på en A4-sida med standardtypsnitten (se [specen för platser och betalning](platser-och-betalning.md)). Den har firmans namn, supportadress och portalens adress, köparens namn, e-post och land, och raderna. För en order som pengarna gått tillbaka för står det när.

## API

### Portalen

Vägarna börjar med `/api/portal` och finns på firmans adress. Köparen behöver inte vara inloggad. Orderns vägar kräver token från köparens länk, och en okänd order eller fel token svarar 404.

| Metod och väg | Beskrivning |
|---|---|
| `GET /shop` | `open`, `full` (stängd för att inga platser är lediga), `test` (testbetalningar), firmans `termsUrl`, challengerna till salu med pris och valuta, billigast först, och `payouts` när firman visar sina utbetalningar och har betalat någon de senaste 30 dagarna: `count`, `totals` per valuta och `averageDaysToPay`. |
| `POST /shop/discount` | `{ "code", "challengeId", "email" }`. Priset med koden: `listAmount`, `discount`, `amount`, `currency` och `forRetries`. En inloggad traders egen e-post avgör om en kod för nya försök gäller, annars den som skickas med, och utan e-post kontrolleras det först när ordern görs. 422 med orsaken: ingen sådan kod, koden har slutat, är förbrukad, gäller inte challengen, gäller bara en annan valuta, skulle ge ett pris under 1, eller är för nya försök. Samma gräns för försök som inloggningen, så att koder inte kan gissas snabbt. |
| `POST /orders` | `{ "challengeId", "email", "acceptTerms", "name", "country", "discountCode" }`. Svarar 201 med `orderId`, `number` och `checkoutUrl`. En inloggad trader köper med sin egen e-post, och med sitt sparade namn och land om de inte anges. 409 när butiken är stängd, ingen plats är ledig eller koden just förbrukades, 404 för en challenge som inte säljs, 422 för fel e-post, namn eller land, villkor som inte är godkända eller en kod som inte gäller, 503 när leverantören inte svarar. Samma gräns för försök som inloggningen. |
| `GET /orders/{id}?token=` | Ordern för köparen: status, challengens namn, belopp, rabattkoden och priset före den, betalsidan medan den väntar, kontot, om tradern redan har lösenord, när inbjudan skickades och om köparen kan välja lösenord här (`canChoosePassword`). |
| `GET /orders/{id}/receipt.pdf?token=` | Kvittot som PDF i firmans namn, med samma rader som i mejlet (se Kvittot). 409 innan ordern är betald. |
| `POST /orders/{id}/password` | `{ "token", "password" }`. Köparen väljer lösenord och loggas in. 409 när ordern inte startade traderns enda konto eller tradern redan har lösenord, 422 för ett för kort lösenord. Samma gräns för försök som inloggningen. |
| `POST /invites/confirm` | `{ "token" }`. En trader med lösenord bekräftar e-posten med inbjudan och loggas in. 422 för en trader utan lösenord, som väljer det med `POST /invites/accept`. |
| `POST /me/confirm-email` | Mejlar den inloggade tradern en ny länk som bekräftar e-posten. 202, 409 när den redan är bekräftad. |
| `POST /orders/{id}/invite` | `{ "token" }`. Skickar inbjudan igen. 202, 409 när tradern redan har lösenord, ordern inte har startat något eller firman inte är godkänd och köparen inte är en administratör, 429 inom en minut från förra, 503 när mejlet inte gick iväg. |
| `POST /orders/{id}/test-payment` | `{ "token" }`. Betalar en testorder. 409 för andra ordrar, en order som gått ut och när firman inte får ta testbetalningar. |

I adminpanelen:

| Metod och väg | Beskrivning |
|---|---|
| `GET /admin/orders?status=&limit=` | Firmans nyaste ordrar, högst 500, valfritt med en status. |
| `GET /admin/orders/{id}` | Ordern med allt som hänt den. |
| `POST /admin/orders/{id}/mark-paid` | Markerar en order från firmans egen sida som betald, med `{ "reference" }`. Kontot startar. 409 för Stripe- och testordrar. |
| `POST /admin/orders/{id}/mark-refunded` | Markerar en betald order som återbetald. 409 för Stripe-ordrar, som Stripe rapporterar själv. |
| `GET /admin/prices`, `PUT /admin/challenges/{id}/price` | Priserna, och ett pris med `{ "amount", "currency", "forSale" }`. 422 för ett ogiltigt pris, 404 för en okänd challenge. |
| `GET /admin/discounts` | Firmans rabattkoder, den nyaste först, med `uses`, betalda ordrar och ordrar som väntar på betalning. |
| `POST /admin/discounts` | `{ "code", "percentOff", "amountOff", "currency", "challengeIds", "maxUses", "expiresAt", "forRetries" }`. Svarar 201 med koden. 422 för en ogiltig kod, både eller ingen av procent och belopp, en procent utanför 1 till 99, ett belopp utan valuta, okända challenges, högst antal utanför 1 till 1 000 000 eller en sista dag som har varit. 409 när firman redan har en kod med samma bokstäver. |
| `PUT /admin/discounts/{id}/active`, `DELETE /admin/discounts/{id}` | `{ "active" }` stänger av eller sätter på koden. Borttagning svarar 409 när en order har använt koden, som då ligger kvar med ordern. |
| `PUT /admin/firm/shop-payouts` | `{ "show" }`. Om butiken visar vad firman betalat ut de senaste 30 dagarna. Svarar med firmans inställningar, där `shopShowsPayouts` säger hur det står. Avstängt från början. |
| `PUT /admin/firm/payments` | `{ "provider", "stripeSecretKey", "stripeWebhookSecret", "checkoutUrl", "termsUrl" }`. Tomma Stripe-nycklar behåller de sparade. Med bara `stripeSecretKey` läggs webhooken till i firmans Stripe-konto. 422 för testbetalningar eller testnycklar hos en firma som är live, Stripe utan nycklar, en webhook som Stripe inte lägger till, egen sida utan adress och adresser som inte är https. |

Villkoren (`termsUrl`) är firmans villkor för traders, samma adress som i ansökan till vår granskning (se [specen för granskning och avstängning](granskning.md)).

`GET /admin/firm` har `payments`: vald leverantör, om den fungerar nu, om testbetalningar är tillåtna, om Stripe-nycklar finns och i vilket läge, adressen och händelserna för Stripes webhook, firmans betalsida och villkor. Nycklarna visas aldrig.

`GET /admin/billing` har `shopProblem`: varför butiken inte tar betalt när firman är live. I sandlådan hindrar det firman från att gå live (till exempel testbetalningar där de slutar när firman är live), och för en firma som är live står det överst under Needs you. Se [specen för platser och betalning](platser-och-betalning.md).

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

- Ingen moms på traderns köp, så kvittot har ingen momsrad, och inga prenumerationer. Rabattkoder skapas bara i adminpanelen, inte med firmans API, och en kod kan inte ge en challenge gratis.
- Kvittot har firmans namn men inte dess bolagsuppgifter, som organisationsnummer och adress. Stripe kan dessutom skicka sina egna kvitton, om firman har slagit på det i Stripe.
- En order som betalades men inte kunde starta något konto ger inget mejl. Kvittot finns ändå som PDF.
- En delvis återbetalning i Stripe markerar inte ordern.
- Bara Stripe har en färdig adapter. Andra leverantörer kopplas in med firmans egen sida tills de får egna adaptrar.
- Inbjudan efter köpet skickas från plattformens e-postadress med firmans namn, inte från firmans domän. Svaren går till firmans supportadress.

## Tester

- `prop/tests/Prop.Api.Tests/SlotTests`: en order som håller den sista platsen åt sin köpare, butiken som stänger, en order som går ut och lämnar tillbaka platsen och en betalning som kommer när ingen plats är ledig.
- `prop/tests/Prop.Api.Tests/OrderFlowTests`: butiken som öppnar när firman tar betalt och har ett pris, testbetalning med inbjudan som fungerar, en inloggad trader som köper med sin egen e-post, Stripe-sessionen med firmans nyckel och idempotensnyckel, Stripes webhook som startar kontot exakt en gång, fel signatur och fel firma, ett belopp som inte stämmer, återbetalning och bestridande med webhooks till firman, en betalning efter att sessionen gått ut, Stripe som nekar, ogiltiga nycklar, livenycklar som sparas i sandlådan men tar betalt först live, webhooken som läggs till i Stripe med bara den hemliga nyckeln och ersätter en tidigare, en webhook som Stripe nekar, nycklar som sparas men aldrig visas, firmans egen sida med firmans API och priset som ordern behåller, att bara egna sidans ordrar markeras för hand, priser som kontrolleras, full sandlåda, firmans villkor, inbjudan som skickas igen, en testorder som gått ut, testbetalningar bara i sandlådan utanför utveckling och att ordrar är firmans egna. Stripe är en låtsad version (`FakeStripe`) som tar emot sessionerna och gör signerade händelser.
- `prop/tests/Prop.Api.Tests/OrderFlowTests` också: lösenordet på orderns sida och e-posten som bekräftas efteråt, att utbetalningar kräver det, att orderns sida aldrig sätter lösenord för en trader med andra konton, namnet och landet som krävs och syns för firman, mejlen till traders i firmans utseende med svar till dess supportadress, kvittot i mejlet efter köpet med rabatten, reglerna och stegen, kvittot som PDF (200, 404 för fel token och okänd order, 409 innan ordern är betald), och kvittot med en länk till kontot för en inloggad trader.
- `prop/tests/Prop.Api.Tests/ReceiptTests`: kvittots rader med och utan rabatt, utan moms och med "Order 1002", reglerna i korthet också per fas och för direkt funded, stegen för en köpare med och utan lösenord, och PDF:en med samma rader och för en återbetald order.
- `prop/portal/src/lib/orders.test.ts`: vilka ordrar firman kan markera, orderns anteckning och vad köparens ordersida säger, också när köparen väljer lösenord där.
- `prop/tests/Prop.Api.Tests/DiscountTests`: en kod som butiken tar av priset och som ordern behåller, att den räknas som använd, inte kan tas bort och kan stängas av, att en kod inte används oftare än tillåtet eller på fel challenge, nya koder som nekas, en kod med samma bokstäver, ett belopp i fel valuta, en okänd kod, koder för nya försök som bara gäller den som underkändes, "Try again" med den bästa koden och inget nytt försök på ett konto som handlas.
- `prop/tests/Prop.Api.Tests/ShopPayoutsTests`: att butiken inte visar några utbetalningar förrän firman sätter på det, att den sedan visar de betalda de senaste 30 dagarna med antal, summa och snittid men inget före den första eller efter 30 dagar, och att bara firmans administratörer ändrar valet.
- `prop/portal/src/lib/shop.test.ts`: pristabellerna med storlekarna som kolumner, konsistensregeln, egna tabeller för andra regler och storlekarnas namn, korten och frågorna, och raderna om firmans utbetalningar i hela belopp som aldrig avrundas uppåt.
- `prop/portal/src/lib/discounts.test.ts`: vad en kod tar av, om den går att använda och sista dagens slut.
- `prop/tests/Prop.Api.Tests/BillingFlowTests`: mejlet om försäljningen och att testköp i sandlådan bara räknas tills firman går live.
- `prop/portal/e2e/shop.spec.ts`: en besökare köper ur pristabellen med e-post, namn och land, väljer lösenord direkt efter testbetalningen, ser rutan om att bekräfta e-posten, och firman ser den betalda ordern, kontot och namnet; en besökare på förstasidan hamnar i butiken; en inloggad trader köper och kommer direkt till sitt nya konto; och firman sätter på att visa sina utbetalningar under Checkout, och butiken visar dem precis när firman har betalat någon.
