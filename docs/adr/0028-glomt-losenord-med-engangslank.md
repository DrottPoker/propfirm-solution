# 0028. Glömt lösenord med en engångslänk som loggar ut andra sessioner

- Status: Föreslagen
- Datum: 2026-10-04

## Sammanhang

Ingen kunde byta ett glömt lösenord: inte firmans traders, inte dess administratörer och inte vår personal. Den som glömt det måste be någon annan om en ny inbjudan, och sessioner fortsatte att gälla efter att ett lösenord byttes. En administratör som inte mindes sin firmas adress hade dessutom ingenstans att logga in, eftersom plattformens egen adress bara hade registreringen.

## Beslut

- **Den som glömt lösenordet skriver sin e-post och får en länk** som gäller en gång i en timme, för traders och administratörer på firmans portal och för vår personal på vår adminvy. Svaret är alltid detsamma, så att det aldrig säger vem som har ett konto. En trader som aldrig valt lösenord kan välja ett så här.
- **Länkarna sparas i `password_resets`** med bara en SHA-256-hash av sin token, som inbjudningarna. En ny länk ersätter personens äldre oanvända. En länk gäller bara på sin firmas portal och för sin roll.
- **Länkar kontrolleras när de öppnas**, både länkarna för lösenord och inbjudningarna, så att sidan säger att en länk är använd eller har gått ut innan något skrivs in, och vad personen kan göra i stället.
- **Ett nytt lösenord loggar in personen och ut alla andra sessioner.** Sessionen har en stämpel av lösenordet: början på en SHA-256-hash av lösenordets hash. Varje anrop jämför den med lösenordet som är sparat. Eftersom varje hash har ett eget salt får också samma lösenord valt igen en ny stämpel, och stämpeln avslöjar inget om lösenordet. Det gäller traders, administratörer och vår personal.
- **Konfigurerade testanvändare behåller sitt lösenord** när det redan är det konfigurerade, så att deras sessioner inte loggas ut vid varje start.
- **Plattformens adress får en förstasida och en inloggning som hittar firman.** En administratör skriver sin e-post och får en engångslänk till adminpanelen för varje firma den administrerar, med firmans adress att spara. Svaret är detsamma om e-posten är okänd.
- **Inloggningen skickar tillbaka till sidan man kom från** (`?next=`, bara en sida i portalen), så att en länk i ett mejl öppnar rätt sida också för den som inte var inloggad.

## Konsekvenser

- Den som kommer åt någons e-post kan välja ett nytt lösenord. Det är samma risk som med inbjudningarna, och en tvåstegsinloggning är fortfarande kvar att göra.
- Sessioner från före stämpeln loggades ut en gång när den infördes.
- Lösenordet till terminalen byts inte här. Firmornas traders loggar in i terminalen genom portalen ([ADR 0027](0027-handelsvillkor-och-inloggning-genom-portalen.md)).
