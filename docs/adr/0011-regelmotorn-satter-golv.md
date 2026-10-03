# 0011. Regelmotorn sätter golv och avgör faserna

- Status: Föreslagen
- Datum: 2026-10-03

## Sammanhang

Propfirm-plattformen ska kontrollera challenge-reglerna i realtid: vinstmål, max daglig förlust, max total förlust och minsta antal handelsdagar. Förlustgränserna måste kontrolleras vid varje pris. En regelmotor som räknar på ett eget prisflöde eller på fördröjda händelser kan komma fram till något annat än handelsplattformen. Produktplanen säger att handelsplattformens equity är facit.

Handelsmotorn kan redan sätta golv för equity. Den kontrollerar dem vid varje pris, i samma steg som priset behandlas, och sparar bevisen vid ett brott (ADR 0005).

## Beslut

- **Förlustgränserna blir golv i handelsplattformen.** Regelmotorn räknar aldrig själv på priser. Ett brott upptäcks av handelsmotorn, som stänger allt, stänger av kontot och sparar bevisen.
- **Regelmotorn styr golven och faserna.** Den sätter golven när ett konto öppnas, lägger om det dagliga golvet vid varje ny handelsdag, räknar handelsdagar och avgör när en fas är klar.
- **Det dagliga golvet begärs som dagens startpunkt minus ett avstånd.** Handelsplattformen fixerar nivån när golvet läggs in, från kontot som det är just då. Ingen fördröjning mellan systemen påverkar alltså nivån. Det kräver en ny sorts golv i handelsmotorn (fas 4b).
- **Regelmotorn är ett deterministiskt bibliotek**, `Prop.Rules`, utan klocka, slump och I/O, som handelsmotorn. Tjänsten runt den (fas 4c) gör om firmans definition av handelsdag till indata och sparar alla indata.
- **Fakta från handelsplattformen har plattformens löpnummer.** Upprepade och sena fakta, och fakta om andra konton, ignoreras.
- **Varje challenge sparar en kopia av sin definition.** Firmans senare ändringar påverkar inte pågående challenges.
- **Varje fas får ett nytt konto.** Vinstmålet mäts på saldot och räknas när alla positioner är stängda.

## Konsekvenser

- Regelmotorn och handelsplattformen kan inte komma till olika slutsatser om ett brott.
- Handelsplattformen behöver ett golv som räknas från dagens startpunkt (fas 4b).
- Mellan dagsskiftet och att det nya dagliga golvet läggs in gäller gårdagens golv. Fördröjningen är normalt under en sekund.
- Fler regler, till exempel för jämna resultat eller inaktivitet, läggs till som nya indata och utdata utan att ändra hur förlustgränserna kontrolleras.
