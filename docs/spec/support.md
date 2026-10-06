# Spec: supportärenden

- Fas: supportärenden mellan traders och firman, efter rättelserna efter genomgången som ny firma
- Status: Implementerad i `prop/src/Prop.Api/Support` och `prop/portal`
- Datum: 2026-10-05

## Syfte

En trader ska kunna fråga sin firma om ett konto, en utbetalning eller något annat direkt i portalen, och firman ska kunna svara där den redan arbetar, eller skriva till en trader först. Hela konversationen finns på ett ställe, med kontot den gäller, och båda får veta när den andra har skrivit. Besluten finns i [ADR 0041](../adr/0041-supportarenden-mellan-traders-och-firman.md).

## Flöde

1. Tradern öppnar ett ärende under Support i portalen, eller med "Ask about this account" på ett kontos sida, med en rubrik, ett meddelande, valfritt ett av sina konton och upp till tre filer. Ärendet väntar på firman (`Open`).
2. Firmans administratörer får ett mejl med meddelandet och en länk till ärendet i adminpanelen. Ärendet syns under Support, med antalet som väntar i menyn och en rad i Needs you på översikten.
3. En administratör svarar, med filer om det behövs. Ärendet väntar på tradern (`Answered`), och tradern får ett mejl med svaret i firmans namn och en länk till ärendet. Administratören kan också svara och stänga ärendet i samma steg med "Send and close", eller stänga det utan svar.
4. Tradern ser svaret som oläst i menyn och i listan tills ärendet öppnas. Tradern skriver tillbaka, och ärendet väntar på firman igen, eller stänger det med "Close ticket".
5. Ett nytt meddelande i ett stängt ärende öppnar det igen, från tradern väntande på firman och från firman väntande på tradern.

**Firman skriver först.** En administratör klickar på "Write to a trader" under Support, eller "Write to the trader" på traderns kort på ett kontos sida, och skriver traderns e-post, valfritt ett av traderns konton, en rubrik, ett meddelande och filer. Ärendet väntar på tradern från början (`Answered`, öppnat av firman), och tradern får meddelandet per mejl och ser det som oläst. Ett ärende har högst 200 meddelanden, en trader lägger till högst 25 MB filer per dygn och firmans administratörer tillsammans 100 MB, och högst `Limits:SupportWritesPerMinute` ärenden och meddelanden skrivs per adress och minut (ADR 0045). Mer nekas med 409, och för många per minut med 429. Innan vi har godkänt firman går mejlet bara till en trader som är en av firmans administratörer, vilket sidan säger (ADR 0043), men ärendet syns ändå i traderns portal. För tradern heter det "Message from {firma}" i stället för "Answered". Sedan går det som andra ärenden.

## Sidor

| Sida | För | Innehåll |
|---|---|---|
| `/support` | Traders | Traderns ärenden, det senast skrivna först: rubrik, status, vem som skrev sist och början av meddelandet, numret, kontot och när. Ett oläst svar har en prick och fet rubrik. Knappen "New ticket". 50 åt gången med "Show more". |
| `/support/new?account=` | Traders | Ett nytt ärende: rubrik, "About" med traderns konton eller "No account in particular", meddelande och filer. `?account=` väljer kontot. Rubriken, meddelandet och filerna kontrolleras innan något skickas, och tjänsten kontrollerar dem igen. |
| `/support/{id}` | Traders | Ärendet: rubrik, status, numret, kontot som länk och när det öppnades, knappen "Close ticket", meddelandena med vem som skrev och när och filerna, vad som händer nu, och rutan för att skriva tillbaka. Svar som tradern inte läst räknas som lästa när sidan visar dem. Sidan frågar efter nya meddelanden var 15:e sekund. |
| `/admin/support?group=&search=` | Administratörer | Firmans ärenden i grupperna "Waiting for you" (det som väntat längst först), "Waiting for the trader", "Closed" och "All" (det senast skrivna först), med antalet i varje. Sökning på en del av traderns e-post eller rubriken, eller numret med eller utan #. Ett ärende som väntat mer än ett dygn är markerat. Adressen följer gruppen och sökningen. |
| `/admin/support/new?email=&account=` | Administratörer | Firman skriver till en trader först: traderns e-post, "About" med traderns konton när e-posten är en hel adress som finns hos firman, rubrik, meddelande och filer. `?email=` och `?account=` fyller i tradern och kontot, som länken "Write to the trader" på traderns kort gör. Sidan säger att ID-handlingar och lösenord aldrig ska begäras i ett ärende. |
| `/admin/support/{id}` | Administratörer | Ärendet med traderns e-post och namn, kontot som länk, länkar till traderns konton och ärenden, meddelandena med vilken administratör som svarade, och rutan för svaret med "Send" och "Send and close". "Close without answering" stänger utan mejl. |

- Traderns sidhuvud har länken Support med antalet svar som tradern inte läst. På en telefon har knappen Menu en prick när det finns sådana.
- Adminpanelens meny har Support efter Payouts, med antalet ärenden som väntar på firman.
- Traderns kort på ett kontos sida i adminpanelen har länkarna "Write to the trader" och "Support tickets" till traderns ärenden.
- Ett ärende som firman öppnat säger det: "Ticket #12 from Demo Firm" för tradern, "Ticket #12 to ..." och "Opened by your team" i adminpanelen.
- Tradern ser firmans namn som avsändare av svaren, aldrig vilken administratör som svarade. I adminpanelen står tradern, "You" för egna svar och e-postadressen för andra administratörers.
- Bilder visas små i konversationen. Alla filer laddas ned med sitt namn när de klickas.

## Portalens API

Alla vägar börjar med `/api/portal`. Ett ärende som inte är traderns, eller inte firmans, svarar 404. Ett meddelande skickas som `multipart/form-data` med fälten nedan och filerna i fältet `files`. Ett fel har `field` (`subject`, `body`, `accountId` eller `files`) när det gäller ett fält.

| Metod och väg | Roll | Beskrivning |
|---|---|---|
| `GET /support/summary` | Trader | `active`, traderns ärenden som inte är stängda, och `unread`, de med ett svar som tradern inte läst. |
| `GET /support/tickets?cursor=&limit=` | Trader | Traderns ärenden, det senast skrivna först, högst 100 åt gången (standard 50). `next` är värdet för `cursor` till nästa sida. Varje ärende har nummer, rubrik, status, `openedBy` (`Trader` eller `Firm`), konto, tider, `waitingSince`, antalet meddelanden, `lastAuthor`, `preview` (början av det senaste meddelandet på en rad) och `unread`. |
| `POST /support/tickets` | Trader | Öppnar ett ärende med `subject`, `body`, valfritt `accountId` och filer. 201 med ärendet. 422 utan rubrik eller meddelande, för långa, eller med ett konto som inte är traderns, 409 med tio ärenden som inte är stängda. |
| `GET /support/tickets/{id}` | Trader | Ärendet med alla meddelanden, äldst först, och `unread`. |
| `POST /support/tickets/{id}/messages` | Trader | Skriver i ärendet med `body` och filer. Ett besvarat eller stängt ärende väntar på firman igen. |
| `POST /support/tickets/{id}/close` | Trader | Stänger ärendet. Ett stängt ärende förblir stängt. |
| `POST /support/tickets/{id}/read` | Trader | Tradern har läst svaren. 204. |
| `GET /support/attachments/{id}` | Trader | En fil i ett av traderns ärenden, som nedladdning. |
| `GET /admin/support/summary` | Admin | `open`, ärendena som väntar på firman, `oldestWaiting`, sedan när det äldsta väntat, och `answered`. |
| `GET /admin/support/tickets?group=&search=&cursor=&limit=` | Admin | En sida av firmans ärenden i gruppen (`Open`, standard, `Answered`, `Closed` eller `All`), med `counts` för samma sökning i varje grupp. |
| `POST /admin/support/tickets` | Admin | Skriver till en trader först med `traderEmail`, `subject`, `body`, valfritt `accountId` och filer. 201 med ärendet, som väntar på tradern. 422 när ingen trader hos firman har e-posten, och som för traderns ärenden. Tradern får meddelandet per mejl. |
| `GET /admin/support/tickets/{id}` | Admin | Ärendet med alla meddelanden, `adminEmail` på firmans svar, traderns namn och `closedByAdmin`. |
| `POST /admin/support/tickets/{id}/messages` | Admin | Svarar med `body`, filer och valfritt `close=true`, som stänger ärendet med svaret. Tradern får svaret per mejl. |
| `POST /admin/support/tickets/{id}/close` | Admin | Stänger ärendet utan svar. Inget mejl. |
| `GET /admin/support/attachments/{id}` | Admin | En fil i ett av firmans ärenden, som nedladdning. |

Filerna hämtas med `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; sandbox` och `Cache-Control: private, no-store`.

## Regler och gränser

| Regel | Värde |
|---|---|
| Rubrik | 1 till 120 tecken, på en rad. Radbrytningar blir mellanslag. |
| Meddelande | 1 till 5 000 tecken. Radbrytningarna behålls, och tomrum före och efter tas bort. |
| Filer | PDF, PNG eller JPEG, kända från innehållet, högst 5 MB och 3 per meddelande. 413 för en för stor fil och 415 för fel sort. |
| Ärenden per trader | Högst 10 som tradern öppnat och som inte är stängda. Ärenden som firman öppnat räknas inte, och ett stängt ärende som öppnas igen räknas inte mot gränsen när det öppnas. |
| Konto | Ett av traderns egna konton hos firman, eller inget. |

## Mejl

| Slag | Till | När |
|---|---|---|
| `firmSupport` | Alla firmans administratörer | Ett ärende börjar vänta på firman: det öppnas, eller tradern skriver i ett besvarat eller stängt ärende. Ämnet nämner tradern vid namn när det finns, och texten med e-posten bredvid. Mejlet har meddelandet citerat, högst 2 000 tecken, hur många filer det har och en länk till ärendet i adminpanelen. Fler meddelanden medan ärendet redan väntar ger inga fler mejl. |
| `traderSupportAnswers` | Tradern | Firman svarar, eller skriver först ("Message from {firma}: {rubrik}"). Mejlet kommer i firmans namn och utseende med hela meddelandet, hur många filer det har, om ärendet stängdes och knappen "Open the ticket". Det ber tradern svara i portalen, men ett svar på mejlet går till firmans supportadress. |

Båda kan stängas av under Notifications i adminpanelen. Att stänga ett ärende utan svar mejlar ingen.

## Tabeller

| Tabell | Innehåll |
|---|---|
| `support_tickets` | Ärendena: firma, nummer, trader, konto, rubrik, status, vem som öppnade det (`opened_by`), när det öppnades, senast skrevs i, började vänta på firman, senast besvarades och lästes av tradern, när och av vem det stängdes (`trader` eller administratörens e-post) och antalet meddelanden. |
| `support_ticket_counters` | Nästa ärendenummer per firma. |
| `support_messages` | Meddelandena i ordning per ärende, med författare (`Trader` eller `Firm`), administratören som svarade och texten. |
| `support_attachments` | Filerna per meddelande i ordning, med namn, sort, storlek, SHA-256 och innehållet krypterat med AES-GCM, knutet till firman och filen. |

## Tester

- `prop/tests/Prop.Api.Tests/SupportTests`: ett ärende om ett konto med en fil, mejlet till administratörerna med meddelandet, filen krypterad i databasen och hämtad med sina huvuden, kön med antal och början av meddelandet, firmans svar med mejlet till tradern och olästa svar, ett mejl per väntan hur ofta tradern än skriver, stängt av tradern, öppnat igen och besvarat och stängt av firman. Vad som nekas: rubrik och meddelande som saknas eller är för långa, andras och okända konton, för många, för stora och fel sorts filer, och gränsen på tio ärenden. Att ett ärende bara nås av sin trader och sin firma, också dess filer. Grupperna, sökningen på e-post, rubrik och nummer, ordningen, sidorna med markör och traderns egen lista. Att firman skriver till en trader först, med mejlet, olästa meddelanden och traderns svar, att e-posten måste vara en trader hos firman och kontot traderns, och att firmans ärenden inte räknas mot traderns gräns. Att firman kan stänga av mejlen.
- `prop/portal/src/lib/support.test.ts`: statusen för tradern och firman, också för ett ärende som firman öppnat, vem som skrev, filerna som tas emot, bilderna och deras adress, tecknen kvar och hur länge ett ärende väntat. `admin.test.ts` har ärendena i Needs you.
- `prop/portal/e2e/tickets.spec.ts`: tradern frågar om ett konto från kontots sida med en skärmbild, firman ser antalet i menyn, öppnar ärendet med kontot, svarar och stänger, tradern ser svaret som oläst i menyn, läser det och öppnar ärendet igen med ett nytt meddelande. Firman skriver till en trader från traderns kort, med tradern och kontot ifyllda, tradern ser meddelandet som oläst och svarar med en fil, och firman ser svaret vänta.
