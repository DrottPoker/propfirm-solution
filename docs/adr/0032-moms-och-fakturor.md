# 0032. Moms efter firmans land, och en faktura för varje betald debitering

- Status: Föreslagen
- Datum: 2026-10-04

## Sammanhang

Vi är ett svenskt bolag, men debiteringarna till firmorna hade ingen moms, och firmorna kunde inte ladda ner någon faktura eller något kvitto. En svensk firma ska betala 25 % moms, och ett bolag behöver en faktura för att dra av momsen och bokföra köpet. Debiteringarnas nummer räknades dessutom för alla firmor tillsammans, så en firma såg luckor som #1003 och #1007, och en fakturaserie per firma saknades.

## Beslut

- **Momsen följer firmans ansökan när debiteringen görs.** En firma i Sverige, i ett annat EU-land utan momsnummer eller utan land betalar vår moms (`Billing:VatPercent`, 25 %). En firma i ett annat EU-land med momsnummer betalar ingen, och köparen redovisar momsen (omvänd skattskyldighet). En firma utanför EU betalar ingen. Vilket land vi är i står i `Billing:Seller:Country`.
- **Priserna är utan moms**, och momsen läggs till som en egen summa. Raderna summerar till beloppet utan moms, och det som betalas är det plus momsen. Hos Stripe är momsen en egen rad.
- **Debiteringen sparar momsen och vem den gäller**, med bolagets namn, registreringsnummer, adress, land och momsnummer, så att fakturan säger samma sak när ansökan ändras senare.
- **Varje firma har sin egen nummerserie** för debiteringar, från 1001. Numren från förut behålls.
- **En betald debitering får nästa nummer i firmans fakturaserie** i samma transaktion som betalningen, så att serien saknar luckor: en debitering som aldrig betalas får aldrig ett fakturanummer. Numret skrivs `{FIRMA}-{nummer}`, till exempel `ACME-0001`.
- **Fakturan är en PDF** som firman laddar ner i adminpanelen, med oss som säljare, firman som köpare, raderna, momsen och att den är betald med kort. Den görs av tjänsten själv, med PDF:ens standardtypsnitt Helvetica, så inget bibliotek eller typsnitt behövs.
- **Mejlen om handpenningen och om att firman är live har kvittot** i text, med fakturanumret.
- Utanför utveckling startar tjänsten inte utan vårt namn, adress, organisationsnummer och momsnummer (`Billing:Seller`).

## Konsekvenser

- Firmorna i testerna är tyska med momsnummer, så beloppen i dem är priserna. Moms och fakturor har egna tester.
- Momsnumret kontrolleras inte mot EU:s register av tjänsten. Det gör vår personal i granskningen.
- Debiteringar från innan momsen sparades har ingen moms (`NotRecorded`), och fakturan säger det.
- Vår revisor behöver godkänna fakturans innehåll och texterna om omvänd skattskyldighet innan första riktiga fakturan.
- Text utanför Latin-1 visas som ? i PDF:en. Det räcker för bolagsnamn och adresser i västra Europa. Fler språk kräver ett inbäddat typsnitt.
