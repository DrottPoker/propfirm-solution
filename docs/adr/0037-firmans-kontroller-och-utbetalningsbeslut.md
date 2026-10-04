# 0037. Firmans kontroller av traders, nej med återförd vinst och en konsistensregel

- Status: Föreslagen
- Datum: 2026-10-05

## Sammanhang

Godkännandet av funded-konton och utbetalningar nämnde KYC, men plattformen hade inget steg för det. Ett nej till en utbetalning tog alltid vinsten (ADR 0015), så en firma som bara väntade på traderns ID kunde inte neka utan att tradern förlorade vinsten. Många firmor har också en konsistensregel för funded-konton, så att en utbetalning inte bygger på en enda lyckad dag.

## Beslut

- **Firmans kontroller av en trader** är en checklista per trader på traderns kort i adminpanelen: "ID checked" och "Address checked", med när och vilken administratör som bockade. Godkännandet av ett funded-konto och av en utbetalning påminner och frågar först när kontrollerna inte är klara, men hindrar inte, eftersom det är firmans beslut. Utbetalningskön visar om tradern är kontrollerad. En koppling till en tjänst för identitetskontroll, till exempel Sumsub eller Veriff, kommer senare och fyller då i samma kontroller.
- **Ett nej har två val:** "Reject, the profit is forfeited", för en bruten regel, eller "Reject, put the profit back", till exempel medan tradern blir klar med kontrollerna. Regelmotorn ger då `DepositRequested`, och vinsten sätts in på kontot den togs från med ett eget id, så att den bara sätts in en gång. Det går bara medan kontot handlas. Tradern får veta i mejlet om vinsten kom tillbaka.
- **En konsistensregel** kan sättas på funded-fasen, 10 till 100 %: den bästa handelsdagen sedan förra utbetalningen får ha gett högst så stor del av vinsten. Regelmotorn räknar varje dags resultat från stängda positioner, och räkningen börjar om när en fas börjar och efter varje utbetalning. Regeln avgör om en utbetalning kan begäras, med orsaken, och portalen visar den bästa dagens andel.

## Konsekvenser

- Firman kan dokumentera sina kontroller i plattformen, och tradern förlorar inte vinsten för att en kontroll dröjer.
- Kontrollerna bockas för hand tills en tjänst för identitetskontroll kopplas in.
- Ett nej med återförd vinst ändrar saldot igen, och golv som följer saldot flyttas med som vid andra insättningar.
- Konsistensregeln ingår i challengens definition, så konton som redan har startat behåller sina regler.
