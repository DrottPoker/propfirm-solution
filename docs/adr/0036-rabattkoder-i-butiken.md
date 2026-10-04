# 0036. Rabattkoder i butiken, och koder för nya försök

- Status: Föreslagen
- Datum: 2026-10-05

## Sammanhang

Nästan alla propfirmor säljer med rabattkoder och kampanjer, och många ger rabatt till den som försöker igen efter en underkänd challenge. Utan koder fick firman sköta försäljningen utanför portalen, och ett underkänt konto erbjöd inget nytt försök alls.

## Beslut

- **Firman skapar koder i adminpanelen** under Discount codes: en procent (1 till 99) eller ett belopp i en av prisvalutorna, för alla challenges eller några, valfritt högst ett antal gånger och till en sista dag. En kod känns igen utan hänsyn till stora och små bokstäver.
- **Propfirm-tjänsten räknar priset.** Butiken frågar efter priset med koden innan köparen betalar, och ordern sparar koden och priset före den. Leverantören får det rabatterade beloppet. Priset blir aldrig under 1 i sin valuta, så en kod kan inte ge en challenge gratis. Firman startar gratis challenges själv.
- **Användningar räknas på betalda ordrar och ordrar som väntar på betalning.** Koden låses när ordern sparas, i samma transaktion som platsen, så att två köpare aldrig tar den sista gången. En order som går ut lämnar tillbaka sin gång.
- **En kod för nya försök** gäller bara en köpare vars tidigare challenge hos firman underkändes, med samma e-post. Ett underkänt konto vars challenge fortfarande säljs visar "Try again", med den kod för nya försök som ger mest rabatt, och öppnar butiken med challengen och koden ifyllda.
- **En använd kod tas inte bort**, eftersom ordern ska kunna visa vad den betalade. Firman stänger av den i stället.

## Konsekvenser

- Firman kan sälja med kampanjer i sin egen portal, och underkända traders får en väg tillbaka.
- Koder kan inte skapas med firmans API än, och en kod gäller en köpare i taget, inte en grupp.
- Inga kvitton eller rapporter per kod. Ordrarna visar koden och priset före den, och webhooken `order.paid` har båda.
