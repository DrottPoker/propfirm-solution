# 0033. Mejl till traders i firmans utseende, med svar till firmans support

- Status: Föreslagen
- Datum: 2026-10-04

## Sammanhang

Mejlen till en firmas traders var ren text utan logga, kom från vår adress `no-reply@prop-platform` och svar gick ingenstans. En trader som svarade på ett mejl om en utbetalning eller en avslutad challenge nådde varken firman eller oss, och mejlen såg inte ut att komma från firman tradern köpt av.

## Beslut

- **Mejlen till traders har firmans utseende**: firmans namn som avsändare, loggan överst, eller namnet i accentfärgen utan logga, och en knapp i accentfärgen till kontot eller länken. Samma text finns som ren text för mejlprogram som inte visar HTML.
- **Firman anger en supportadress** under Notifications. Den blir svarsadressen (`Reply-To`) på mejlen till dess traders, och mejlen säger att tradern kan svara.
- **Mejlen skickas fortfarande från vår adress.** Firmans egen avsändardomän kräver att firman verifierar domänen hos vår e-postleverantör (SPF, DKIM), och kommer senare.
- **Mejlen till firmor och till vår personal kommer från oss**, i Kronants utseende som HTML med samma text som ren text (se [ADR 0047](0047-ett-gemensamt-designsystem.md)). Först var de bara ren text.
- Utkorgen sparar HTML och svarsadressen med mejlet, så att ett nytt försök skickar samma mejl.

## Konsekvenser

- Mejlprogram som blockerar bilder visar firmans namn i stället för loggan.
- En firma utan supportadress får mejl som inte går att svara på, som förut.
- Texterna är på engelska och fasta. Firman väljer vilka som skickas, inte vad de säger.
