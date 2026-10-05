# 0045. Skydd mot missbruk av gratis sandlådor

- Status: Föreslagen
- Datum: 2026-10-05

## Sammanhang

Vem som helst kan registrera en firma gratis och få en sandlåda med en egen server på handelsplattformen ([ADR 0017](0017-firmor-registrerar-sig-sjalva.md)). [ADR 0043](0043-sparrar-innan-vi-godkant-firman.md) hindrar en sådan firma från att nå främlingar. Kvar fanns sätt att använda själva registreringen och sandlådan fel:

- **Registreringen som spam.** Den som registrerar sig kan ange någon annans e-postadress, och vi mejlade då "Confirm your email for {firmanamn}" med ett namn som avsändaren valt, som "Claim your prize at scam.example". Det fanns bara en gräns per IP-adress.
- **Massregistrering.** Ett skript kan registrera tusentals firmor. Varje firma får en server på handelsplattformen och upp till 10 öppna testkonton, och finns kvar för alltid.
- **Supportärenden utan tak.** Ett ärende kunde ha hur många meddelanden som helst, med 3 filer på 5 MB vardera, som sparas i vår databas.
- **Anrop utan gräns.** Firmans API och supportärendena hade ingen gräns för hur ofta de anropas.

## Beslut

- **En robotkontroll på registreringen: Cloudflare Turnstile.** Den är gratis, visar oftast ingen uppgift alls för en människa, och vi använder redan Cloudflare för DNS ([ADR 0040](0040-driften-pa-en-vps.md)). Registreringssidan visar kontrollen, och tjänsten kontrollerar svaret hos Cloudflare innan något annat. Ett svar fungerar en gång. Utan svar, eller med fel svar, nekas registreringen med 422, och svarar inte Cloudflare nekas den med 503. Nycklarna är inställningar (`RobotCheck:SiteKey` och `RobotCheck:SecretKey`). Utanför utveckling startar tjänsten inte utan dem. Lokalt och i testerna är kontrollen avstängd, och Cloudflares testnycklar kan användas på testservern.
- **En adress får högst `Signup:MaxEmailsPerDay` bekräftelsemejl per dygn** (standard 3). En ny registrering ersätter fortfarande personens tidigare obekräftade, men de går ut i stället för att tas bort, så att de räknas. Fler nekas med 429.
- **Bekräftelsemejlet säger inget som avsändaren valt.** Ämnet är "Confirm your email for {plattformen}", och texten säger att någon registrerat en firma med adressen.
- **En sandlåda som inte används stängs.** Varje gång en administratör använder adminpanelen sparas det, högst en gång i timmen per firma (`firms.active_at`). En firma som inte är live och vars adminpanel inte använts på `Sandbox:IdleDays` dagar (standard 60) varnas per mejl en vecka innan, och stängs sedan: dess öppna testkonton avslutas, och inga nya kan starta, varken i adminpanelen, butiken eller firmans API. Så fort en administratör använder adminpanelen igen öppnas sandlådan, med allt firman ställt in. Anrop till firmans API räknas inte, så ett skript håller ingen sandlåda öppen.
- **Supportärenden har tak:** högst 200 meddelanden per ärende, och filer på högst 25 MB per trader och dygn hos en firma och 100 MB per dygn för firmans administratörer tillsammans. Mer nekas med 409 och en förklaring.
- **Gränser per minut:** högst `Limits:SupportWritesPerMinute` nya ärenden och meddelanden per adress (standard 20), och högst `Limits:FirmApiCallsPerMinute` anrop till firmans API per nyckel (standard 600). Fler nekas med 429 och en rubrik som portalen kan visa, som alla gränser per minut nu har.

## Konsekvenser

- Registrering i stor skala kräver att någon klarar Cloudflares kontroll varje gång, och en adress kan inte översköljas med mejl från oss.
- En gratis firma som ingen använder kostar till slut bara några rader i databasen och en tom server på handelsplattformen. Firmor tas inte bort. Det kan komma senare, tillsammans med regler för hur länge vi sparar personuppgifter.
- Registreringssidan laddar ett skript från Cloudflare, och Cloudflare ser besökarens adress.
- Gränserna per minut gäller per adress respektive nyckel, och är högt satta för att aldrig märkas vid vanligt bruk.
