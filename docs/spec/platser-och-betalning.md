# Spec: platser och betalning

- Fas: 7, handpenningen och godkännandet innan live i 9a, kontrollen av butiken före live, moms, fakturor, sidan Go live och mejlen med kvitto efter genomgången som ny firma
- Status: Implementerad i `prop/src/Prop.Api/Billing`, `prop/src/Prop.Rules`, handelsplattformen och `prop/portal`
- Datum: 2026-10-03, paketet 2026-10-04

## Syfte

Firman betalar oss i förskott för platser, alltså hur många challenges den kan ha aktiva samtidigt. Varje månad betalar den ett paket med ett antal platser, och platser utöver paketet kostar ett pris per plats. Den betalar en handpenning när den skickar sin ansökan till vår granskning, och när vi har godkänt den går den live genom att betala startavgiften minus handpenningen och första månaden (se [specen för granskning och avstängning](granskning.md)). Sedan betalar den varje månad innan den börjar. En månad som inte är betald när den börjar pausar firmans challenges tills den är betald. Vi levererar aldrig något som inte är betalt och har aldrig en skuld att driva in. Besluten finns i [ADR 0020](../adr/0020-forbetalda-platser-for-aktiva-challenges.md). Inaktivitetsregeln och tidsgränsen per fas, som frigör platser, beskrivs i [specen för regelmotorn](regelmotor.md). Moms, fakturor och nummerserierna beskrivs i [ADR 0032](../adr/0032-moms-och-fakturor.md).

## Flöde

```
sandlådan -> /admin/go-live: 1 bolagets uppgifter -> 2 debitering Deposit på en betalsida -> 3 vår granskning -> godkänd
-> 4 antal platser (minst paketets) och automatisk utökning, priset just nu med moms, rutan "innan du betalar"
-> butiken tar betalt också live, eller firman säljer inte i portalen
-> POST /admin/billing/activate -> debitering Activation och en betalsida (Stripe Checkout eller testsidan)
-> leverantören bekräftar -> i en transaktion: debiteringen betald med nästa fakturanummer, månaden betald, firman live,
   testkontona från sandlådan avslutas och deras traders mejlas, obetalda testordrar går ut,
   och administratörerna mejlas kvittot och vad som händer nu
5 dagar före varje månad -> debitering Renewal -> kortet dras utan firman
   nej -> nytt försök varje dygn, mejl till administratörerna
månaden börjar obetald -> firman pausas: inga nya challenges, butiken stänger, challengerna pausas
   -> firman betalar (nytt försök, annat kort eller betalsida) -> challengerna återupptas
fler platser -> debitering Slots -> kortet dras direkt
sista lediga platsen tas och automatisk utökning är på -> debitering Slots med fler platser
```

## Platser

| Firma | Platser |
|---|---|
| I sandlådan | `Sandbox:MaxOpenAccounts` (standard 10), utan betalning. |
| Live och betalar | De platser firman har betalat för den här månaden. Är månaden obetald kan inget startas. |
| Konfigurerad | `Firms:N:Slots` utan betalning, eller ingen gräns om den saknas. |

- **Tagna platser** är challenges som inte har tagit slut (`Used`) och ordrar i portalen som väntar på betalning och inte har gått ut (`Reserved`). En betald order tar sin egen reserverade plats när kontot startas.
- **Ingen ledig plats:** firmans API och adminpanelen svarar 409 när en challenge ska startas, butiken stänger och en ny order nekas. En betalning som ändå kommer, till exempel för en order som gått ut, blir betald utan konto, med orsaken i `problem`.
- **Varning:** när `Billing:WarningPercent` (standard 80) av en betalande firmas platser är tagna mejlas administratörerna en gång, och igen först när användningen har sjunkit under gränsen. Adminpanelen visar varningen.

## Priser

Priserna är inställningar under `Billing`. Värdena nedan är vårt förslag från 2026-10-04 och bekräftas när offerterna för prisdata och intervjuerna med firmor finns.

| Del | Förslag |
|---|---|
| Startavgift, en gång | 700 USD |
| Handpenning för granskningen, dras av från startavgiften | 200 USD |
| Paket med 25 platser | 500 USD per månad |
| Plats 26 till 100 | 5 USD per månad |
| Plats 101 och uppåt | 4 USD per månad |

- Paketet ingår alltid, och dess platser är de färsta en firma kan ha.
- Varje plats utöver paketet kostar priset för sitt eget steg. 120 platser kostar alltså 500 + 75 x 5 + 20 x 4 = 955 USD i månaden.
- En del av en månad betalas för dagarna som är kvar, räknat med dagen för betalningen, avrundat till hela cent. Den 5 oktober är 27 av 31 dagar kvar.
- Månaderna är kalendermånader i UTC.
- En firma har minst paketets platser och högst `MaxSlots` (10 000). Har paketet fler platser än firman valt, till exempel efter att priserna ändrats, debiteras och får firman paketets platser.
- Paketet och platserna utöver det är egna rader i varje debitering, till exempel `Package with 25 slots, November 2026` och `5 extra slots, November 2026`.
- Priserna är utan moms. Momsen läggs till som en egen summa på debiteringen, se nedan.

## Moms

Vi är ett svenskt bolag (`Billing:Seller:Country`), och momsen på en debitering följer firmans ansökan när debiteringen görs:

| Firma | Moms | `vatTreatment` |
|---|---|---|
| I Sverige, eller i ett annat EU-land utan momsnummer, eller utan land | `Billing:VatPercent` (25 %) läggs till | `Charged` |
| I ett annat EU-land med momsnummer | Ingen, köparen redovisar momsen (omvänd skattskyldighet) | `ReverseCharge` |
| Utanför EU | Ingen | `OutsideEu` |
| Debiteringar från innan momsen sparades | Ingen sparad | `NotRecorded` |

- Raderna är utan moms och summerar till `netAmount`. `vatAmount` är momsen avrundad till hela cent, och `amount` är vad som betalas, `netAmount` plus `vatAmount`.
- Debiteringen sparar momsen och vem den gäller (bolagets namn, registreringsnummer, adress, land och momsnummer), så att fakturan säger samma sak även om ansökan ändras senare.
- Hos Stripe är momsen en egen rad (`VAT 25%`), så att raderna summerar till beloppet.
- Handpenningen som dras av från startavgiften är handpenningen utan moms.
- Våra siffror i adminvyn räknar intäkter utan moms.

## Nummer och fakturor

- Varje firma har sin egen nummerserie för debiteringar, från 1001 (`billing_counters`). Numren från innan serien per firma behålls.
- En debitering som betalas får nästa nummer i firmans serie av fakturor, från 1, i samma transaktion som betalningen, så att serien saknar luckor. Fakturanumret skrivs `{FIRMA}-{nummer}`, till exempel `ACME-0001`.
- Varje betald debitering har en faktura som PDF på `GET /billing/charges/{id}/invoice`: vi som säljare (`Billing:Seller`), firman som köpare, fakturanummer och datum, raderna, summan utan moms, momsen, totalen och att den är betald med kort. Vid omvänd skattskyldighet och utanför EU står varför ingen moms tas ut.
- PDF:en är en A4-sida med standardtypsnittet Helvetica, så inget typsnitt bäddas in. Text utanför Latin-1 visas som ?.

## Debiteringar

| Sort | När | Vad |
|---|---|---|
| `Deposit` | Firman skickar sin ansökan första gången | Handpenningen för vår granskning. Betalas på en betalsida, som sparar kortet. Betalas inte tillbaka, eftersom granskningen görs för hand också när vi inte kan godkänna firman. |
| `Activation` | Firman går live, när vi har godkänt den | Startavgiften minus handpenningen som betalats, och paketet och platserna utöver det för resten av månaden. Från den dag nästa månad debiteras också nästa månad. Betalas på en betalsida, som sparar kortet. |
| `Renewal` | `ChargeDaysBeforeMonth` (5) dagar innan månaden börjar | Månadens paket och platser: firmans valda antal, eller så många som är tagna om de är fler, och aldrig färre än paketets. Dras från det sparade kortet. |
| `Slots` | Firman köper fler, eller automatisk utökning | Skillnaden i pris för resten av månaden, och för nästa månad om den redan är betald med färre. Dras från kortet direkt. |

| Status | Betyder |
|---|---|
| `Pending` | Inte betald än. Kortet dras vid nästa försök, eller firman betalar på en betalsida. |
| `Paid` | Betald, och det den betalar för gäller. |
| `Failed` | En månad som nekades `MaxAttempts` (5) gånger. Kortet dras inte igen av sig självt, men firman kan betala den. |
| `Void` | Ska inte betalas: en go-live eller handpenning som startades om eller inte betalades i tid, platser som nekades, eller en månad som gjordes om med fler platser. |

- **En nekad månad** försöks igen efter `RetryInterval` (1 dygn). Administratörerna mejlas varje gång, med när den försöks igen och när månaden börjar.
- **Nekade platser** köps inte. Administratören får Stripes orsak direkt. Nekas automatisk utökning mejlas administratörerna, och nästa försök görs med en ny debitering först efter `RetryInterval`.
- **Leverantören svarar inte:** debiteringen försöks igen efter 5 minuter, utan att det räknas som ett försök.
- **Ett nytt kort** försöker firmans obetalda månader direkt.
- **Fler platser när nästa månad redan är debiterad men inte betald:** månadens debitering görs om med de nya platserna. Hann den betalas med färre under tiden, eller betalades nästa månad innan köpet gick igenom, får den månaden en egen debitering för resten.
- **Färre platser** gäller från första månaden som varken är betald eller debiterad, och bara om de tagna platserna får plats. Adminpanelen visar från vilken månad.

## Paus

När en månad börjar utan att vara betald:

- Firmans faktureringsrad får `unpaid_since`, och administratörerna mejlas en gång.
- Inga nya challenges kan startas, och butiken stänger.
- Varje challenge som inte har tagit slut får indata `PauseChallenge`. Regelmotorn ber handelsplattformen pausa kontot (`SuspendAccountRequested`), och webhooken `account.paused` skickas till firman. Tradern kan inte öppna nya affärer men kan stänga sina positioner och flytta stoppar, och golven gäller som vanligt.
- Dagarna till fasens tidsgräns och till inaktiviteten räknas inte. När challengen återupptas flyttas båda fram med dagarna den var pausad.

När månaden är betald tas `unpaid_since` bort, varje pausad challenge får `ResumeChallenge`, kontot får handla igen och webhooken `account.resumed` skickas. Bakgrundsjobbet stämmer av varje minut, så challenges som startade eller tog slut under tiden kommer ikapp.

## Leverantörer

| Leverantör | Betalsida | Kortet dras |
|---|---|---|
| `Stripe` | Stripe Checkout med vårt konto. En betalning har `mode=payment`, bara kort (`payment_method_types[0]=card`), en rad per del, `customer_creation=always` första gången och annars firmans kund, och `payment_intent_data[setup_future_usage]=off_session`, så att kortet sparas. Ett nytt kort sparas med `mode=setup`. `success_url` är `/admin/go-live?checkout={CHECKOUT_SESSION_ID}` på firmans adress för handpenningen och go-live, och annars `/admin/billing?checkout={CHECKOUT_SESSION_ID}`. Momsen är en egen rad. Har firman redan en kund hos Stripe, till exempel från handpenningen, används den. | `POST /v1/payment_intents` med firmans kund och kort, `off_session=true`, `confirm=true` och `metadata[source]=card_on_file`. `Idempotency-Key` är `charge-{id}-{försök}-{kort}`. Ett fel från Stripe räknas som ett försök, så nästa försök får en ny nyckel. Svarar Stripe inte görs samma försök igen. |
| `Test` | `/admin/billing/checkout/{id}` i portalen. Ett testkort som betalar (slutar på 4242) eller nekas (0002). | Testkortet som betalar betalar, det andra nekas. Bara för utveckling. |

Stripes webhook till `/api/payments/v1/billing/stripe` kräver `Stripe-Signature` med `Billing:StripeWebhookSecret`, högst 5 minuter gammal.

| Händelse | Blir |
|---|---|
| `checkout.session.completed`, `checkout.session.async_payment_succeeded` | En betalning: betalningen och kortet läses från PaymentIntent, beloppet och valutan måste stämma med debiteringen, och debiteringen blir betald. Ett sparat kort: kortet läses från SetupIntent och ersätter det gamla. Samma sida igen ändrar ingenting. |
| `checkout.session.expired` | Sidan har gått ut. En go-live på den blir `Void`. |
| `payment_intent.succeeded` med `metadata[source]=card_on_file` | En dragning av kortet som tjänsten inte hann spara, om beloppet stämmer. Gäller den en debitering som redan är betald på annat sätt, eller som inte längre ska betalas, sparas den som `paid_twice` och loggas, så att den betalas tillbaka. |

Andra händelser svaras med 200 och används inte. Svarar Stripe inte när en betalning läses svarar webhooken 503, så att Stripe skickar den igen.

## API

### Adminpanelen

Vägarna börjar med `/api/portal/admin` och kräver en administratör.

| Metod och väg | Beskrivning |
|---|---|
| `GET /billing` | Firmans status, sätt att betala (`Paid`, `Complimentary` eller inget i sandlådan), leverantör, platser (`limit`, `slots`, `used`, `reserved`, `free`, `paid`, `suspended`, `warning`), platser från nästa obetalda månad, automatisk utökning, kortet, `unpaidSince`, nästa debitering med och utan moms, de 24 senaste debiteringarna med rader, moms och fakturanummer, priserna med handpenningen, varför firman inte kan gå live (`goLiveProblem`), granskningens status (null innan firman sparat en ansökan), handpenningen som betalats, avstängningen, `shopProblem`, varför butiken inte tar betalt när firman är live, `vat`, hur momsen gäller firman nu, och `sandboxAccounts`, testkontona som avslutas när firman går live. |
| `GET /billing/quote?slots=&expandBy=` | Vad ett antal platser kostar: `Activation` i sandlådan med handpenningen avdragen, `MoreSlots`, `FewerSlots` eller `Unchanged`, raderna som betalas nu, `netAmount`, momsen och `amount`, månadspriset utan moms och från vilken månad, och vad som är fel med valet av platser, annars varför firman inte kan gå live. Med `expandBy` också `expansion`: platserna en runda automatisk utökning lägger till, vad de kostar i månaden och för resten av månaden om det sker i dag. |
| `POST /billing/activate` | `{ "slots", "autoExpandStep" }`. Startar go-live och svarar med `checkoutUrl`. 409 för en firma som inte är i sandlådan, inte är godkänd eller är avstängd, och för en butik som skulle sluta ta betalt när firman går live (testbetalningar eller Stripes testnycklar där de slutar då), 422 för fel antal, 503 när leverantören inte svarar. En tidigare go-live som inte betalats blir `Void`. |
| `PUT /billing/slots` | `{ "slots" }`. Fler dras från kortet direkt, färre gäller från nästa obetalda månad. Svarar med `GET /billing`. 402 med orsaken när kortet nekas, 409 när månaden är obetald, när platser köps just nu, för en firma utan kort eller för färre platser än de tagna, 503 när leverantören inte svarar. |
| `PUT /billing/auto-expand` | `{ "step" }`, 1 till 1 000, eller null för av. |
| `POST /billing/card` | En betalsida som sparar ett nytt kort. Svarar med `checkoutUrl`. |
| `POST /billing/charges/{id}/checkout` | En betalsida för en obetald månad. |
| `POST /billing/charges/{id}/retry` | Drar en obetald månad från kortet nu. 402 med orsaken när det nekas. |
| `GET /billing/charges/{id}/invoice` | Fakturan för en betald debitering, som PDF att ladda ner. 404 för en annan firmas eller en obetald. |
| `GET /billing/checkouts/{id}` | Testsidan: vad den är till, vad den betalar och vilken sida firman kommer tillbaka till. Bara med testleverantören och bara firmans egna. |
| `POST /billing/checkouts/{id}/complete` | `{ "declines" }`. Betalar eller sparar ett testkort. Ett kort som nekas en betalning svarar 402 och sidan är fortfarande öppen. 409 när sidan inte är öppen. |

### Firmans API

| Metod och väg | Beskrivning |
|---|---|
| `GET /api/firm/v1/slots` | Platserna som i adminpanelen, så att firmans egen butik kan sluta sälja när de är slut eller firman är avstängd. |
| `POST /api/firm/v1/accounts` | 409 med orsaken när ingen plats är ledig, när sandlådan är full, när månaden är obetald eller när firman är avstängd. |

## E-post till administratörerna

| Mejl | När |
|---|---|
| `Payment for {firma} declined` | Kortet nekades, med orsaken, nästa försök och när månaden börjar. |
| `{firma} is paused until this month is paid` | En månad började obetald. |
| `{firma} has used {tagna} of {platser} slots` | `WarningPercent` av platserna är tagna. |
| `We have received the application for {firma}` | Ansökan är skickad. När handpenningen skickade den står kvittot med. |
| `Payment received for {firma}` | Handpenningen betalades men ansökan kunde inte skickas, till exempel för att något saknas. Med kvittot. |
| `{firma} is live` | Firman gick live: kvittot, butikens adress, att testkontona avslutades och vad som händer varje månad. |

Mejlen med kvitto köas i samma transaktion som betalningen. Traders vars testkonton avslutas när firman går live får `Your test account with {firma} has ended`, i firmans namn, om firman inte har slagit av mejlen om avslutade challenges.

Mejlen skickas från plattformens adress. Ett mejl som inte går iväg loggas, och adminpanelen visar samma sak.

## Portalen

| Sida | Innehåll |
|---|---|
| `/admin/go-live` | I sandlådan, menyns Go live, i fyra steg som var och ett visar var det är: bolagets uppgifter, handpenningen, vårt svar, och platser och betalning. Sista steget visar före godkännandet bara vad det kostar, i en neutral ruta. Godkänd: priserna med paketet först, antal platser med paketets som förval, automatisk utökning med vad en utökning kostar, priset just nu med moms, rutan "Before you pay" med testkontona som avslutas, butikens status, design och villkor, en bekräftelse när testkonton avslutas, och knappen som betalar och går live. Ett antal platser som inte går visar bara vad som är fel, inte summan. Efter en betalsida väntar sidan tills betalningen är bekräftad, och visar sedan att firman är live och vad som händer nu. |
| `/admin/billing` | Live, menyns Plan and billing: platserna med en stapel, ändring av platser med priset, automatisk utökning med vad en utökning kostar, kortet och nästa betalning, obetalda månader med nytt försök och betalsida, och debiteringarna med moms och fakturan som PDF. Fliken Company details visar bolagets uppgifter som de godkändes. I sandlådan skickas firman till `/admin/go-live`. |
| `/admin/billing/checkout/{id}` | Testsidan för betalningar och kort. |

Varje sida i adminpanelen visar en rad när månaden är obetald, när en betalning nekats, när alla platser är tagna eller när de flesta är det.

## Tabeller

| Tabell | Innehåll |
|---|---|
| `firm_billing` | Hur firman betalar (`Paid` eller `Complimentary`), platserna från nästa obetalda månad, automatisk utökning, leverantören och det sparade kortet (kund, betalningsmetod, märke, sista siffror och utgång), när varningen skickades, sedan när månaden är obetald och när firman gick live. |
| `billing_periods` | Månaderna firman har betalat, med platserna i dem. En månad utan rad är obetald. |
| `billing_charges` | Debiteringarna med nummer i firmans serie, sort, status, månad, antal månader (0 för handpenningen), platser, rader, belopp utan moms, momsen (`vat_treatment`, `vat_percent`, `vat_amount`), beloppet som betalas, kunden, fakturanumret när den är betald, leverantörens referens, senaste nej, försök och nästa försök. |
| `billing_counters` | Firmans nästa debiteringsnummer och fakturanummer. Raden är låst medan ett nummer tas. |
| `billing_checkouts` | Betalsidorna med syfte (`Payment` eller `Card`), debitering, adress och status. |
| `billing_events` | Allt som hänt med firmans betalning, med vem som sa det och leverantörens meddelande. Rader läggs bara till. |

`challenge_accounts` har kolumnen `paused`, så att pausade challenges hittas snabbt.

## Konfiguration

| Inställning | Innehåll |
|---|---|
| `Billing:Provider` | `Stripe` (standard) eller `Test`, bara för utveckling. |
| `Billing:Currency`, `StartupFee`, `ReviewDeposit` | Valuta, startavgift och handpenningen för granskningen (0 för ingen, högst startavgiften). |
| `Billing:PackagePrice`, `PackageSlots` | Paketets pris per månad (över 0) och platserna som ingår (minst 1). Paketets platser är de färsta en firma kan ha. |
| `Billing:SlotPrices` | Priset per plats utöver paketet från varje steg (`From`, `Price`). Första steget börjar på platsen efter paketets. |
| `Billing:MaxSlots` | Högst antal platser. Standard 10 000. |

Förslaget till priser ligger i `appsettings.json`.
| `Billing:ChargeDaysBeforeMonth` | Hur många dagar innan månaden den debiteras. Standard 5. |
| `Billing:RetryInterval`, `MaxAttempts` | Hur länge till nästa försök efter ett nej, och hur många försök en månad får. Standard 1 dygn och 5. |
| `Billing:WarningPercent` | När administratörerna varnas. Standard 80. |
| `Billing:CheckoutLifetime` | Hur länge en betalsida är öppen, 30 minuter till 23 timmar som Stripe tillåter. Standard 1 timme. |
| `Billing:StripeSecretKey`, `StripeWebhookSecret` | Vårt Stripe-kontos nyckel och webhookens signeringshemlighet. Hemligheter. |
| `Billing:VatPercent` | Vår moms i procent. Standard 25. |
| `Billing:Seller:Name`, `Address`, `Country`, `OrganizationNumber`, `VatNumber`, `Email` | Vi som säljare på fakturorna. `Country` (standard `SE`) avgör vilka firmor som betalar vår moms. Utanför utveckling startar tjänsten inte utan namn, adress, organisationsnummer och momsnummer. |
| `Firms:N:Slots` | En konfigurerad firmas platser utan betalning. Tomt för ingen gräns. |

I utveckling används testleverantören. En firma som registrerat sig skickar sin ansökan och går live på http://{firma}.localhost:3002/admin/go-live, och godkänns på vår adminvy http://ops.localhost:3002. `demo-firm` har ingen gräns.

## Begränsningar

- Momsnumret kontrolleras inte mot EU:s register (VIES). Det gör vår personal i granskningen.
- Fakturor skickas inte som bilaga. Mejlet har kvittot i text, och PDF:en laddas ner i adminpanelen.
- Månadsdebiteringar och köpta platser mejlas inte som kvitton. Fakturorna finns under Plan and billing.
- Handpenningen betalas tillbaka bara för hand.
- En firma som slutar betala förblir pausad. Ingen regel avslutar dess challenges efter en tid.
- En debitering som betalats två gånger betalas tillbaka för hand.
- Bara Stripe har en färdig adapter.
- Priserna är ett förslag tills de är bekräftade.

## Tester

- `prop/tests/Prop.Api.Tests/VatAndInvoiceTests`: momsen efter land och momsnummer, avrundning till hela cent, fakturanummer i firmans serie, vad en runda automatisk utökning kostar, och fakturan som en PDF med rätt korsreferenser, båda bolagen, momsen och texten om omvänd skattskyldighet.
- `prop/tests/Prop.Api.Tests/BillingRulesTests`: paketet och priset per steg utöver det, att fler platser utöver paketet aldrig blir billigare, att en månad aldrig debiteras för färre platser än paketets eller de tagna, paketet och platserna utöver det som egna rader, del av en månad, när en månad debiteras, raderna för handpenningen, go-live med och utan avdrag, månaden och fler platser, och att fel inställningar hittas.
- `prop/tests/Prop.Api.Tests/SlotTests`: ingen gräns, en full firma som inte kan starta förrän en challenge tar slut, en order som håller den sista platsen åt sin köpare, en order som går ut och lämnar tillbaka platsen, och en betalning utan plats.
- `prop/tests/Prop.Api.Tests/BillingFlowTests`: att en svensk firma betalar moms med fakturan som PDF bara för firman själv, att en tysk firma med momsnummer inte gör det, att varje firma har sin egen serie av nummer och fakturor, priset för en automatisk utökning, mejlet med kvittot när firman går live och mejlet till traders vars testkonton avslutas, att go-live kräver vårt godkännande, att en butik med testbetalningar hindrar go-live där de slutar live och att en firma som är live inte kan välja testbetalningar eller testnycklar, att testköp i sandlådan räknas i siffrorna tills firman går live, att terminalen listar firmans server och vet var traderna loggar in när firman är live, go-live med testbetalning, kort som nekas och sandlådans konto som avslutas, go-live som inte är tillåten, go-live som startas om eller går ut, fler och färre platser, månaden som dras i förskott, en månad som börjar obetald och pausar challenges på handelsplattformen tills ett nytt kort betalar, en avstängning som håller challengerna pausade fast månaden betalas, en obetald månad som betalas på en betalsida, färre platser som gäller efter månaden som väntar på betalning, automatisk utökning och en nekad utökning som väntar ett dygn, varningen, att testsidan bara är firmans egen och konfigurerade firmors platser.
- `prop/tests/Prop.Api.Tests/StripeBillingTests`: handpenningen på Stripe Checkout och samma kund när firman går live, Stripe Checkout med vår nyckel och bara kort, och webhooken som tar firman live en gång, en förfalskad signatur, dragningen av kortet med en nyckel per försök och kort, ett nekat kort med Stripes orsak, ett nytt kort från en setup-sida, en go-live som startas om och stänger den första sidan, en dragning som Stripe rapporterar, en betalning till som sparas för återbetalning, och platser som Stripe fördröjer förbi nästa månads debitering och ändå gäller nästa månad. momsen som en egen rad på Stripe Checkout, Stripe är en låtsad version (`FakeStripe`).
- `prop/portal/src/lib/goLive.test.ts` och `admin.test.ts`: stegen till live med butikens problem, och att en butik som inte tar betalt live står först bland det som väntar på firman.
- `prop/portal/src/lib/billing.test.ts`: platserna i text och stapel, paketet och prisstegen, kortet, debiteringarnas namn, momsen i text och belopp, fakturans adress, vad en utökning kostar och raden i adminpanelen, också för en avstängd firma.
- `prop/portal/src/lib/goLivePage.test.ts`: var de fyra stegen till live är, och vilket steg sidan öppnar på.
- `prop/portal/e2e/billing.spec.ts`: en godkänd firma ser paketet, momsen, vad en utökning kostar och rutan innan betalningen, går live med testbetalning efter ett kort som nekas, ser att den är live, sina platser, kort och fakturor, köper fler platser och ser sina bolagsuppgifter under en egen flik.
