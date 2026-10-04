# Spec: granskning av firmor och avstängning

- Fas: 9a
- Status: Implementerad i `prop/src/Prop.Api/Review`, `prop/src/Prop.Api/Billing` och `prop/portal`
- Datum: 2026-10-03

## Syfte

En firma i sandlådan ska granskas av oss innan den får ta emot riktiga traders. Det skyddar traders mot firmor som tar avgifter och aldrig betalar ut, och därmed vårt eget rykte. Vi granskar själva i en egen adminvy. En firma som redan är live och missköter sig ska vi kunna stänga av från samma vy. Besluten finns i [ADR 0021](../adr/0021-vi-granskar-firmor-innan-de-gar-live.md).

## Flöde

```
sandlådan -> /admin/verification: bolagets uppgifter, ägare, länkar och frivilliga dokument
-> Submit -> handpenning på en betalsida (Stripe Checkout eller testsidan), sparar kortet
-> handpenningen betald -> ansökan skickad, vår personal mejlas
-> vår adminvy /ops: granska
   godkänn -> firman mejlas -> /admin/billing: betala startavgiften minus handpenningen och platserna -> live
   be om ändringar -> firman mejlas -> ändra och skicka igen, utan ny handpenning
   neka -> firman mejlas -> kan inte gå live, handpenningen betalas inte tillbaka
live -> vår adminvy: stäng av med en orsak -> challenges pausas, butiken stänger -> slå på igen
```

## Ansökan

| Fält | Krav när ansökan skickas |
|---|---|
| `companyName` | Bolagets juridiska namn. Högst 200 tecken. |
| `registrationNumber` | Organisationsnummer eller motsvarande. Högst 100 tecken. |
| `country` | Landet där bolaget är registrerat, som ISO 3166-1 alpha-2, till exempel `SE`. |
| `vatNumber` | Momsnummer med landskoden först, som i EU:s momsregister, till exempel `SE559000123401`. Ett bolag i EU anger det eller `noVatNumber`. Frivilligt utanför EU, där det kan vara ett annat skattenummer, högst 30 bokstäver och siffror. Mellanslag, punkter och bindestreck tas bort och bokstäverna blir versaler. |
| `noVatNumber` | `true` när bolaget säger att det inte har något momsnummer. Då sparas inget momsnummer. |
| `address` | Bolagets registrerade adress. Högst 500 tecken. |
| `website` | Firmans webbplats. Frivillig, en https-adress. |
| `contactName` | Vem vi pratar med. Högst 200 tecken. |
| `contactPhone` | Frivillig. Högst 50 tecken. |
| `owners` | Ägarna med minst 25 procent, eller de största om ingen har det, med namn och andel i procent. Minst en och högst 10, och tillsammans högst 100 procent. |
| `termsUrl` | Firmans villkor mot sina traders, en https-adress. Där står reglerna och hur utbetalningar går till. |
| `links` | Frivilliga länkar, till exempel sociala medier och omdömen. Högst 5 https-adresser. |
| `description` | Frivillig. Vilka firman är och hur den säljer challenges och betalar ut. Högst 2 000 tecken. |

- Ett bolag i EU, Sverige också, anger sitt momsnummer eller kryssar i att det inte har något. Vi är ett svenskt bolag, så ett bolag i ett annat EU-land slipper svensk moms bara med ett giltigt momsnummer. Inget land kräver att alla bolag är momsregistrerade, till exempel inte de under omsättningsgränsen, så numret kan inte krävas. Ett bolag i EU utan momsnummer betalar svensk moms. Tjänsten kontrollerar bara formen: landets prefix (`EL` för Grekland) och 2-12 siffror eller bokstäver efter det. Vi kontrollerar numret i EU:s momsregister (VIES) när vi granskar.
- Ett utkast sparas med bara de fält som är ifyllda. Fälten kontrolleras när de sparas, och att allt som krävs finns när ansökan skickas.
- Dokument är frivilliga, till exempel registreringsbevis. Högst 10 filer per firma, högst 10 MB var, i formaten PDF, PNG och JPEG. Formatet kontrolleras på filens innehåll, inte på namnet. Filerna sparas krypterade.
- Ansökan och dokumenten kan ändras som utkast och när vi har bett om ändringar, annars inte.

## Granskningens status

| Status | Betyder |
|---|---|
| `Draft` | Firman fyller i ansökan. Även när den har påbörjat men inte betalat handpenningen. |
| `Submitted` | Ansökan är skickad och väntar på oss. |
| `ChangesRequested` | Vi har bett om ändringar, med ett meddelande. Firman ändrar och skickar igen utan ny handpenning. |
| `Approved` | Firman får gå live genom att betala. |
| `Rejected` | Firman får inte gå live. Beslutet är slutligt, och handpenningen betalas inte tillbaka. |

- En firma utan ansökan har status `Draft` med en tom ansökan.
- Bara en ansökan med status `Submitted` kan godkännas eller få en begäran om ändringar. Den kan nekas när den är `Submitted` eller `ChangesRequested`.
- Konfigurerade firmor granskas inte. De är live från början.

## Handpenningen

- `Billing:ReviewDeposit` (förslag 200 USD) betalas när ansökan skickas första gången, på en betalsida som också sparar kortet. Är den 0 skickas ansökan direkt utan betalning.
- Den är en debitering av sorten `Deposit`, och syns bland firmans debiteringar. En betalsida som inte betalas i tid blir `Void`, och firman kan skicka igen.
- Den dras av från startavgiften när firman går live, med det belopp som faktiskt betalades. Raden heter då `Startup fee, less the deposit of 100.00 USD`, och försvinner om den blir 0.
- Den betalas aldrig tillbaka, inte heller när firman nekas. Det står vid knappen innan firman betalar.
- `ReviewDeposit` kan inte vara större än `StartupFee`.

## Avstängning

- Vi kan stänga av en firma i vilken status som helst, med en orsak som firmans administratörer ser. Den gäller tills vi slår på firman igen.
- Medan firman är avstängd kan inga challenges startas, butiken stänger, och varje challenge som inte har tagit slut pausas som när en månad är obetald (se [specen för platser och betalning](platser-och-betalning.md)). Tradern kan stänga sina positioner, och dagarna till tidsgränsen och inaktiviteten räknas inte.
- När firman slås på igen återupptas challengerna, om inte månaden är obetald.
- Månaderna debiteras som vanligt medan firman är avstängd. En avstängd firma kan inte gå live.
- Administratörerna mejlas när firman stängs av och när den slås på igen.

## Vår personal

- Personalen loggar in på vår adminvy, som har en egen adress (`Platform:OpsUrl`, lokalt http://ops.localhost:3002). Sessionen har en egen cookie, `prop_ops`, och gäller bara där.
- Personalen läggs in i konfigurationen med `Staff:SeedUsers`. Lokalt finns `ops@test.com` med lösenordet `ops`.
- Personalen mejlas när en ansökan skickas.

## Propfirm-tjänsten: firmans adminpanel

Vägarna börjar med `/api/portal/admin` och kräver en administratör.

| Metod och väg | Beskrivning |
|---|---|
| `GET /verification` | Granskningens status, ansökan, dokumenten, vårt senaste meddelande, när den skickades och avgjordes, handpenningen (belopp, valuta och om den är betald), om ansökan kan ändras, varför den inte kan skickas och EU-länderna, där firman anger momsnummer eller att den saknar ett (`euCountries`). |
| `PUT /verification/application` | Sparar ansökan som utkast. 422 med fältet för ett fel, 409 när den inte kan ändras. Svarar med `GET /verification`. |
| `POST /verification/documents` | Laddar upp ett dokument som `multipart/form-data` med fältet `file`. 201 med dokumentet. 409 när ansökan inte kan ändras eller firman har 10 dokument, 413 för en för stor fil, 415 för ett annat format. |
| `GET /verification/documents/{id}` | Hämtar dokumentet. |
| `DELETE /verification/documents/{id}` | Tar bort dokumentet. 409 när ansökan inte kan ändras. |
| `POST /verification/submit` | Skickar ansökan. Svarar med `checkoutUrl` när handpenningen ska betalas, annars `null` och ansökan är skickad. 422 med fältet för det som saknas, 409 när den inte kan skickas, 503 när betalningen inte fungerar. |

`GET /billing` har `goLiveProblem` med orsaken när firman inte är godkänd, och `suspension` när den är avstängd. `GET /billing/quote` drar av handpenningen från startavgiften.

## Propfirm-tjänsten: vår adminvy

Vägarna börjar med `/api/portal/ops` och fungerar bara på `Platform:OpsUrl`. Det kontrolleras innan inloggningen, så på andra adresser svarar de 404. Alla utom `GET /ops`, inloggningen och utloggningen kräver personal.

| Metod och väg | Beskrivning |
|---|---|
| `GET /ops` | Plattformens namn. 404 på andra adresser, så att portalen vet att adressen är vår adminvy. |
| `POST /ops/login` | `{ "email", "password" }`. Samma gräns för försök som andra inloggningar. |
| `POST /ops/logout` | Loggar ut. |
| `GET /ops/me` | Vem som är inloggad. |
| `GET /ops/firms?filter=` | Firmorna: `ToReview` (standard, de som väntar på oss, äldst först), `Suspended` eller `All`. Med status, granskningens status, när ansökan skickades och om firman är avstängd. |
| `GET /ops/firms/{firmId}` | Firman: namn, status, adress, när den registrerades, administratörerna, ansökan med dokument, granskningens status och vårt meddelande, handpenningen, faktureringen i korthet, avstängningen och historiken. |
| `GET /ops/firms/{firmId}/documents/{id}` | Hämtar ett dokument. |
| `POST /ops/firms/{firmId}/approve` | `{ "message" }`, frivilligt. 409 när ansökan inte väntar på oss. |
| `POST /ops/firms/{firmId}/request-changes` | `{ "message" }`, krävs. 409 när ansökan inte väntar på oss. |
| `POST /ops/firms/{firmId}/reject` | `{ "message" }`, krävs. 409 när ansökan inte är skickad. |
| `POST /ops/firms/{firmId}/suspend` | `{ "reason" }`, krävs. 409 när firman redan är avstängd. |
| `POST /ops/firms/{firmId}/unsuspend` | Slår på firman igen. 409 när den inte är avstängd. |

Varje beslut sparar vem i personalen som tog det.

## E-post

| Mejl | Till | När |
|---|---|---|
| `{firma} is waiting for review` | Personalen | En ansökan skickas. |
| `Changes needed for {firma}` | Firmans administratörer | Vi ber om ändringar, med meddelandet. |
| `{firma} is approved` | Firmans administratörer | Vi godkänner, med länken till Billing. |
| `{firma} was not approved` | Firmans administratörer | Vi nekar, med meddelandet. |
| `{firma} is suspended` | Firmans administratörer | Vi stänger av firman, med orsaken. |
| `{firma} is no longer suspended` | Firmans administratörer | Vi slår på firman igen. |

Ett mejl som inte går iväg loggas, och adminpanelen visar samma sak.

## Portalen

| Sida | Adress | Innehåll |
|---|---|---|
| `/admin/verification` | Firmans | Granskningens status och vårt meddelande, ansökan, dokumenten och knappen som skickar med handpenningen. |
| `/admin/billing` | Firmans | I sandlådan: stegen till live, med länk till granskningen tills firman är godkänd. Avdraget för handpenningen syns i priset. |
| `/ops/login` | Vår adminvy | Inloggning för personalen. |
| `/ops` | Vår adminvy | Firmorna som väntar på oss, och flikar för avstängda och alla. |
| `/ops/firms/{id}` | Vår adminvy | Ansökan, dokumenten, besluten, avstängningen, faktureringen och historiken. |

Varje sida i firmans adminpanel visar en rad när firman är avstängd, med orsaken.

## Tabeller

| Tabell | Innehåll |
|---|---|
| `firm_reviews` | Granskningen per firma: status, ansökan som JSON, vårt senaste meddelande, när den skickades och avgjordes och av vem. |
| `firm_documents` | Dokumenten: namn, format, storlek, SHA-256, innehållet krypterat med AES-GCM, vem som laddade upp och när. |
| `firm_events` | Allt som hänt i granskningen och med avstängningen, med vem som gjorde det. Ansökan sparas som den var när den skickades. Rader läggs bara till. |
| `staff_users` | Vår personal: e-post och hash av lösenordet. |

`firms` har kolumnerna `suspended_at` och `suspension_reason`.

## Konfiguration

| Inställning | Innehåll |
|---|---|
| `Platform:OpsUrl` | Vår adminvys adress, med `/` sist. Lokalt http://ops.localhost:3002/. |
| `Billing:ReviewDeposit` | Handpenningen. 0 för ingen. Högst `StartupFee`. Förslaget ligger i `appsettings.json`. |
| `Staff:SeedUsers` | Personal som skapas vid start, eller får lösenordet igen: `Email` och `Password`. För utveckling. |

`Billing:AllowGoLiveWithoutVerification` finns inte längre. En firma går live när den är godkänd.

## Begränsningar

- Vi granskar för hand. Automatisk kontroll mot bolagsregister eller en leverantör kan läggas till senare.
- Inga ID-handlingar för ägarna.
- Personalen läggs in i konfigurationen. Inbjudningar och tvåstegsinloggning för personalen kommer med härdningen för produktion.
- Ett nekat beslut kan inte göras om. Firman får registrera sig på nytt.
- Handpenningen betalas tillbaka bara för hand.

## Tester

- `prop/tests/Prop.Api.Tests/ReviewTests`: ansökan som sparas och kontrolleras, momsnumret eller svaret att bolaget saknar ett, som bara behövs i EU, och momsnumret som sparas utan mellanslag, dokument med fel format och för stora filer, handpenningen på en betalsida, en ansökan som skickas utan handpenning, personalen som mejlas, godkännande, begäran om ändringar som skickas igen utan ny handpenning, nekad firma, att en firma bara går live när den är godkänd, att handpenningen dras av från startavgiften och att adminvyn bara finns på sin adress och kräver personal.
- `prop/tests/Prop.Api.Tests/SuspensionTests`: en avstängd firma kan inte starta challenges, dess challenges pausas och återupptas, butiken stänger, och en obetald månad håller challengerna pausade när firman slås på igen.
- `prop/tests/Prop.Api.Tests/BillingRulesTests`: avdraget för handpenningen.
- `prop/tests/Prop.Api.Tests/VatNumbersTests`: att ett bolag i EU anger momsnummer eller att det saknar ett, hur numret skrivs och vilka former som tas emot och nekas.
- `prop/portal/src/lib/verification.test.ts`: ansökans fält, ägarna och granskningens texter.
- `prop/portal/e2e/verification.spec.ts`: en ny firma fyller i ansökan, betalar handpenningen, granskas och godkänns i vår adminvy och går live.
