# 0020. Förbetalda platser för aktiva challenges

- Status: Föreslagen
- Datum: 2026-10-03

## Sammanhang

Produktplanen säger att firman alltid betalar i förskott för ett antal platser, alltså hur många challenges den kan ha aktiva samtidigt (se Affärsmodell och prissättning). Vi fakturerar aldrig i efterhand, så en firma som läggs ner kan inte lämna obetalda skulder. En challenge som aldrig tar slut får inte äta upp firmans intäkt, så planen lägger också till en regel om inaktivitet och en valfri tidsgräns per fas.

Hittills har firman inte betalat oss något, och en firma som registrerat sig kan inte gå live. Kontrollen av bolag och ägare innan live kommer i fas 9.

## Beslut

- **En plats är en challenge som inte har tagit slut.** En challenge tar en plats från start till underkänd eller avbruten, i alla faser och som funded. Sandlådan har sin egen gräns och inga platser. Konfigurerade firmor har platser utan att betala, ett fast antal eller ingen gräns.
- **En order som väntar på betalning håller en plats.** Annars kan flera köpare betala för den sista platsen, och någon betalar för en challenge som inte kan startas. Starter och ordrar låser firmans platser i sin transaktion, så den sista platsen tas en gång. Butiken stänger när ingen plats är ledig, och firmans API och adminpanel visar platserna.
- **Månaden är en kalendermånad i UTC.** Firman betalar varje månad i förskott, `ChargeDaysBeforeMonth` dagar innan den börjar (standard 5). Fler platser betalas direkt för resten av månaden, räknat i dagar inklusive dagen för köpet, och för nästa månad också om den redan är betald. Färre platser gäller från nästa månad som inte är debiterad än, och bara om de öppna challengerna får plats.
- **Priset per plats sjunker i steg.** Varje plats kostar priset för sitt eget steg, så fler platser aldrig blir billigare totalt. Startavgiften och priserna är inställningar och exempel tills de är bestämda.
- **Vi äger betalningen, leverantören tar bara emot pengarna.** Varje debitering är en rad med rader för vad den betalar, och varje händelse sparas i `billing_events` med leverantörens meddelande som bevis. Det som en betalning betalar för, till exempel en månads platser eller att firman går live, ändras i samma transaktion som debiteringen markeras som betald. Samma betalning flera gånger ger aldrig mer än en gång.
- **Leverantören är en adapter.** `Stripe` med vårt eget konto: firman betalar första gången på en Stripe Checkout-sida som sparar kortet på en Stripe-kund, och kortet debiteras sedan utan firman (`off_session`). Ett nytt kort sparas på en Checkout-sida i läget `setup`. `Test`: en sida i portalen med testkort som betalar eller nekas, bara för utveckling.
- **Varje försök har en egen idempotensnyckel**, `charge-{id}-{försök}-{kort}`. Ett nytt försök eller ett nytt kort blir en ny betalning, men samma försök två gånger dras aldrig två gånger. Svarar Stripe med ett eget fel räknas det som ett försök, eftersom Stripe sparar svaret på nyckeln. Svarar Stripe inte alls görs samma försök igen med samma nyckel. Debiteringen är låst medan kortet dras, och en debitering ändras bara så länge den inte är betald.
- **En månad som nekas försöks igen** varje dygn, högst `MaxAttempts` gånger. Administratörerna mejlas vid varje nej, med när månaden börjar. Firman kan försöka igen direkt eller betala med ett annat kort på en Checkout-sida. Ett nytt kort försöker de obetalda månaderna direkt.
- **En månad som börjar obetald pausar firman.** Inga nya challenges kan startas, butiken stänger och firmans challenges pausas. Regelmotorn får indata `PauseChallenge`, och handelsplattformen stänger av nya affärer på kontot. Tradern kan stänga sina positioner och flytta stoppar, och golven gäller som vanligt. Dagarna till tidsgränsen och inaktiviteten räknas inte medan challengen är pausad, så tradern förlorar ingen tid. När månaden är betald återupptas allt.
- **Handelsplattformen får pausade konton** (`Suspended`). Väntande ordrar tas bort när kontot pausas, eftersom de annars skulle öppna positioner. Admin-API:t har `suspend` och `resume`, som går att upprepa.
- **En challenge tar bara slut på tid när firmans händelser är lästa.** Dagen som avslutar den väntar tills handelsplattformens händelser från före dagen är hanterade, så att en affär gjord i sista stund räknas även när händelsen kommer sent, till exempel efter en omstart.
- **Inaktivitet och tidsgräns är regler i regelmotorn.** En challenge där ingen ny position öppnats på `InactivityDays` dagar tar slut, i alla faser. Mallen har 30 dagar. En fas kan ha en tidsgräns i dagar efter dagen den började. Båda kontrolleras när en handelsdag börjar och ger utdatan `ChallengeExpired`, webhooken `account.expired` och status `Failed`, och frigör platsen.
- **Firman går live genom att betala.** Den första betalningen är startavgiften och platserna för resten av månaden, och för nästa månad också om den redan ska debiteras. När den är betald blir firman live, dess testkonton från sandlådan avslutas och dess obetalda testordrar går ut. Tills kontrollen av firman finns (fas 9) går det bara när `Billing:AllowGoLiveWithoutVerification` är på, vilket bara är i utveckling.
- **Automatisk utökning** köper `AutoExpandStep` platser till med kortet när den sista lediga platsen tas, om firman har slagit på det.
- **Administratörerna varnas** en gång per mejl när `WarningPercent` av platserna är tagna (standard 80), och igen först när användningen har sjunkit under gränsen.

## Konsekvenser

- Vi har aldrig en skuld att driva in. En firma som slutar betala pausas, och dess traders behåller sina konton tills den betalar eller avbryter dem.
- Stripes webhook för våra egna betalningar, `/api/payments/v1/billing/stripe`, måste nås från internet (ADR 0018), och signeras med `Billing:StripeWebhookSecret`.
- En betalning som kommer för en debitering som redan är betald på annat sätt sparas som `paid_twice` och loggas som fel, och betalas tillbaka för hand.
- Debiteringen av kortet sker medan debiteringen är låst i databasen, i högst Stripes svarstid.
- Moms, kvitton från oss och fakturor med våra bolagsuppgifter ingår inte. De bestäms när bolaget och dess land är bestämt. Stripe kan skicka kvitton.
- Priserna i inställningarna är exempel. De bestäms efter intervjuerna och offerterna för prisdata.
