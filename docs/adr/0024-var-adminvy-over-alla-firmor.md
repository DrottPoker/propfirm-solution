# 0024. Vår adminvy över alla firmor: översikt, kontroller och betalningar

- Status: Föreslagen
- Datum: 2026-10-04

## Sammanhang

Vår adminvy ([ADR 0021](0021-vi-granskar-firmor-innan-de-gar-live.md)) var en lista med firmor i tre flikar och en sida per firma med ansökan, beslutsknappar och historik. Personalen såg inte vad som väntade på dem över alla firmor, vad firmorna betalar oss, vilka betalningar som inte gått igenom eller om en firma dröjer med att betala sina traders. Granskningen hade inget stöd för vad som skulle kontrolleras, och besluten togs direkt med knappar under en textruta.

Firmornas egen adminpanel fick en ny utformning i [ADR 0023](0023-adminpanelens-oversikt-och-firmans-logga.md). Vår adminvy ska följa samma mönster.

## Beslut

- **Siffrorna räknas när de läses, med SQL över alla firmor.** `OpsFigures` räknar firmorna i varje grupp, de debiteringar som betalats, vecka för vecka, hur långt firmorna som registrerat sig kommit, och de senaste händelserna. Inget räknas i förväg. Nya index på tid gör frågorna billiga.
- **Det som väntar på oss är en lista överst på översikten**: ansökningar att granska, äldst först, debiteringar där kortet nekats, firmor vars traders väntat mer än 7 dagar på en utbetalning, och firmor vars handelsserver inte blivit klar på 10 minuter.
- **En utbetalning är sen när tradern väntat mer än 7 dagar sedan begäran**, oavsett om firman godkänt den. Det är det som skadar traders och därmed vårt rykte. Gränsen är en konstant i propfirm-tjänsten och skickas med i svaren, så att portalen säger samma sak.
- **Vad en firma betalar per månad räknas som dess egen fakturering gör**: paketet och platserna den valt, eller så många som dess öppna challenges tar. En avstängd firma debiteras som vanligt och räknas med.
- **Firmans grupper och steg följer av data som redan finns**: status, avstängning, granskning och fakturering. En live-firma med obetald månad eller nekat kort är obetald.
- **Granskningen har fem kontroller** (momsnummer i VIES, bolagsregistret, ägarna, villkoren om utbetalningar, webbplatsen och länkarna). De bockas i medan en ansökan väntar på oss eller på ändringar och sparas i `firm_review_checks` med vem som bockade och när. Varje beslut sparar vilka kontroller som var bockade i firmans historik.
- **Ett godkännande stoppas inte av obockade kontroller.** Personalen ser vilka som saknas i rutan som frågar en gång till. Vi vill inte låsa granskningen innan vi vet hur den fungerar i praktiken.
- **Varje beslut tas i en ruta.** Att be om ändringar och att neka kräver ett meddelande, och meningar för obockade kontroller kan läggas till med ett klick. Avstängning säger vad som händer innan den görs.
- **Vi ser kontonummer och belopp hos firmorna, inte vilka traderna är.** Svaren om en firmas utbetalningar har inga e-postadresser.
- **Vår adminvy får en egen färg** (turkos med mörk text), så att den aldrig tas för en firmas portal.

## Konsekvenser

- Översikten och betalningssidan läser alla firmor vid varje anrop. Det räcker för hundratals firmor. Med många fler behövs sammanställda tabeller eller sidindelning.
- Kontrollerna är fasta i koden. Nya kontroller kräver en ändring i både propfirm-tjänsten och portalen.
- Bockade kontroller ligger kvar när firman skickar en ändrad ansökan. Personalen bockar ur det som behöver kontrolleras igen.
- Mejlet till administratörerna öppnas i personalens egen e-postklient. Det sparas inte hos oss.
