# 0019. Köp i portalen med firmans egen betalningsleverantör

- Status: Föreslagen
- Datum: 2026-10-03

## Sammanhang

Hittills tar firman betalt för en challenge på egen hand och startar sedan kontot med firmans API eller i adminpanelen (ADR 0014). En firma som kommer igång själv (ADR 0017) har ofta ingen egen webbutik. Traders ska därför kunna köpa en challenge direkt i firmans portal.

Propfirmor använder många olika betalningsleverantörer. Stripe är vanligast men stänger ibland av propfirmor, så kryptoleverantörer och mindre leverantörer är också vanliga. Pengarna ska gå direkt till firman. Vi ska aldrig hålla traderns pengar eller se kortuppgifter, eftersom det skulle ge oss krav på licenser och kortregler.

## Beslut

- **Vi äger köpet, leverantören tar bara emot pengarna.** Ett köp är en **order** i propfirm-tjänsten med challenge, pris, e-post och status (`Pending`, `Paid` eller `Expired`). Ordern skapas innan tradern skickas till betalningen. När betalningen är bekräftad blir ordern `Paid`, och kontot startas i samma transaktion.
- **Bara ett meddelande från leverantören, eller firmans API, räknas som betalt.** Att tradern kommer tillbaka till portalen räknas aldrig, eftersom vem som helst kan öppna en adress.
- **Varje leverantör är en adapter.** Firman väljer leverantör i adminpanelen:
  - `Stripe`: vi skapar en Checkout Session med firmans egen hemliga nyckel, och tradern betalar på Stripes sida. Stripes webhook till `/api/payments/v1/stripe/{firma}` kontrolleras med firmans signeringshemlighet. Belopp och valuta i betalningen jämförs med ordern.
  - `External`, egen koppling: tradern skickas till firmans egen betalsida med orderns id. Firman anropar `POST /api/firm/v1/orders/{id}/mark-paid` när den har fått betalt. Då fungerar vilken leverantör som helst utan att vi bygger en adapter för den.
  - `Test`: en betalsida i portalen där tradern trycker på "Pay" utan att några pengar dras. Den fungerar bara för firmor i sandlådan, så att de kan prova hela kedjan innan de har en leverantör. I utveckling får också firmor som är live använda den.
- **Fler färdiga adaptrar läggs till efter hand.** En ny leverantör kräver en adapter som startar betalningen och läser leverantörens meddelanden. Resten av köpet är detsamma.
- **Priset ligger utanför challengen.** Pris, valuta och om challengen säljs i portalen sparas för sig. Regelmotorns definition handlar bara om regler. Ordern behåller priset den skapades med.
- **Firmans nycklar krypteras** med `Secrets:Key`, som de andra hemligheterna (ADR 0017), och visas aldrig igen efter att de sparats.
- **I sandlådan dras inga riktiga pengar genom oss.** En firma i sandlådan får bara använda Stripes testnycklar. Det skyddar traders tills kontrollen av firman finns (fas 9).
- **Exakt ett konto per betald order.** Ordern låses när den markeras som betald. Samma meddelande flera gånger, eller en betalning som kommer efter att ordern gått ut, ger aldrig fler konton.
- **Återbetalningar och bestridanden markeras på ordern.** Stripes meddelanden om återbetalning och bestridande sätter en tid på ordern och skickar webhooks till firman. Kontot rörs inte. Firman avgör själv om det ska avbrytas.
- **Tradern kommer in i portalen med en inbjudan per e-post.** Är köparen inte inloggad, och har tradern inget lösenord, mejlar plattformen en inbjudan i firmans namn när ordern är betald. Inbjudan till e-postadressen visar att köparen äger den, så att ingen kan köpa sig in på någon annans konton. En inloggad trader köper med sin egen e-postadress.
- **Köparen följer ordern med en hemlig länk.** Leverantören skickar tillbaka köparen till `/orders/{id}?token=`. Bara en hash av token sparas.

## Konsekvenser

- En firma utan egen webbutik kan sälja challenges direkt. Firmor som redan har en egen butik fortsätter med firmans API som förut.
- Varje händelse på en order sparas i `order_events`, med leverantörens meddelande som bevis.
- Firmans system får webhooks för `order.paid`, `order.refunded` och `order.disputed`.
- `/api/payments/v1` måste nås från internet, eftersom leverantörerna skickar sina meddelanden dit (ADR 0018). Adressen till propfirm-tjänsten är inställningen `Platform:ApiUrl`, och adminpanelen visar firmans webhook-adress för Stripe.
- Stripe kräver att en order går ut tidigast efter 30 minuter och senast efter 24 timmar, så `Payments:OrderLifetime` måste ligga där emellan.
- Inbjudan efter köpet skickas från plattformens e-postadress med firmans namn. Kan den inte skickas kan köparen be om den igen från orderns sida, och firman kan alltid skapa en ny i adminpanelen.
- Rabattkoder, moms, kvitton från oss och prenumerationer ingår inte. Stripe skickar kvitton, och firmans egen koppling får sköta det själv.
