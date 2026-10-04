# 0023. Adminpanelens översikt, sökning och firmans egen logga

- Status: Föreslagen
- Datum: 2026-10-04

## Sammanhang

Adminpanelen var en rad med åtta länkar, och startsidan ett formulär för att starta en challenge och en tabell med konton. Firman såg inte vad som väntade på den, hur försäljningen och utbetalningarna gick eller hur många som klarade sina challenges. Kontona gick bara att söka på hela e-postadressen och på en status. Beslut om utbetalningar och ordrar togs i webbläsarens egna frågerutor, och kontosidan visade inte traderns graf och affärer. Loggan var en adress som firman själv måste ha någonstans, och texten på knapparna var alltid vit, också på en ljus varumärkesfärg.

Portalen räknar aldrig pengar. Belopp och summor kommer från propfirm-tjänsten.

## Beslut

- **Översiktens siffror räknas när de läses, med SQL mot firmans egna tabeller.** `AdminFigures` och nya frågor i `ChallengeQueries` och `PayoutQueries` räknar kontona i varje grupp, försäljningen och utbetalningarna de senaste 30 dagarna, andelen som klarade alla utvärderingsfaser de senaste 90 dagarna, försäljning och utbetalningar per vecka i tolv veckor och de senaste händelserna. Inget räknas i förväg: frågorna läser kontona, faserna, ordrarna och utbetalningarna som redan finns, med nya index på firma och tid.
- **Händelserna byggs av det som redan sparas.** Startade och köpta challenges, klarade faser, startade funded-konton, avslutade challenges och utbetalningar läses från sina tabeller och sorteras på tid. Det blir ingen egen tabell för händelser.
- **Andelen som klarar är de klarade utvärderingarna delat med de klarade och de underkända.** En challenge som firman har annullerat räknas inte, eftersom den inte säger något om hur svår challengen är.
- **Belopp i olika valutor hålls isär.** Summorna ges per valuta, och diagrammet visar firmans valuta.
- **Kontolistan söker på en del av e-postadressen, på kontonumret och på en del av firmans referens, och delar upp kontona i grupper:** utvärdering, väntar på firman, funded och avslutade. Listan bläddras med kontonumret som markör, och svaret har antalet i varje grupp.
- **Firmans API ändras inte.** Adminpanelens vägar under `/api/portal/admin` får egna svar, som `AdminAccountsResponse` och `AdminPayoutResponse`, medan firmans API under `/api/firm/v1` är som förut.
- **Firman laddar upp sin logga, och portalen visar den från sin egen adress.** Loggan sparas i `firm_logos` och nås på `/api/portal/logo/{sha256}`. Adressen ändras med innehållet, så loggan cachas för alltid. PNG, JPEG, WebP och SVG upp till 1 MB tas emot, kända på sitt innehåll. En SVG får inte ha skript, händelser, inbäddade sidor eller entiteter, och den skickas med `Content-Security-Policy: sandbox` och `nosniff`, så att en SVG som öppnas för sig inte kan köra något på portalens adress.
- **Texten på knapparna är en färg som firman väljer** (`accent-foreground`), så att en ljus varumärkesfärg kan få mörk text. Adminpanelen visar kontrasten och varnar under 4,5:1.
- **Firman mejlar tradern från adminpanelen** (`POST /admin/accounts/{id}/email-trader`): en inbjudan att välja lösenord, eller att challengen har startat för en trader som redan har ett. Mejlet går i firmans namn.

## Konsekvenser

- Översikten frågar databasen var 30:e sekund medan den är öppen. Frågorna begränsas av firma och tid, men en firma med mycket stora volymer kan behöva sammanställda tabeller senare.
- Siffrorna räknas om när de läses, så en återbetalning ändrar en vecka som redan har varit.
- Loggorna gör databasen något större. En bildtjänst med CDN kan ta över utan att adressen i portalen ändras.
- Plattformen mejlar bara tradern när firman ber om det. Challenges som startas via firmans API får inget mejl från oss, som förut.
