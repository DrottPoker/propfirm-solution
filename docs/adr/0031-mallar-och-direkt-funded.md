# 0031. Fyra mallar för challenges, och direkt funded utan utvärdering

- Status: Föreslagen
- Datum: 2026-10-04

## Sammanhang

En ny challenge utgick alltid från tvåstegsmallen, och firman fick själv ta bort eller lägga till faser och skriva in id och namn för varje kontostorlek. Regelmotorn krävde minst en utvärderingsfas, så direkt funded, som många firmor säljer, gick inte att erbjuda.

## Beslut

- **Fyra mallar**: ett steg, två steg, tre steg och direkt funded, med vanliga regler (se [specen för regelmotorn](../spec/regelmotor.md)). Adminpanelen hämtar dem i firmans valuta.
- **Direkt funded är en challenge utan utvärderingsfaser.** Regelmotorns första konto är då funded-kontot, med samma regler för förlustgränser, inaktivitet och utbetalningar som annars. Inget annat i livscykeln ändras.
- **Flera kontostorlekar på en gång.** Adminpanelen gör en challenge per storlek från mallen, med id och namn efter mallen och storleken (`one-step-50k`, "One-step 50K") och ett pris var. Reglerna är procent av kontostorleken, så de passar alla storlekar. Det görs med samma anrop som en challenge åt gången, så propfirm-tjänsten behöver inget nytt API för det.
- **En ny challenge säljs inte förrän firman säger det**, och kan inte säljas utan pris. Priset sätts direkt på challengens kort.

## Konsekvenser

- En direkt funded challenge räknas inte i andelen som klarar utvärderingen, eftersom den inte har någon.
- Mallarnas regler är förslag. Firman ändrar dem i redigeraren, och konton behåller reglerna de köptes med.
- Storlekarna att välja mellan är fasta i portalen (5K till 200K). Andra storlekar görs i redigeraren.
