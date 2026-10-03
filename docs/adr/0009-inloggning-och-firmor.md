# 0009. Inloggning och firmor i handelsplattformen

- Status: Föreslagen
- Datum: 2026-10-03

## Sammanhang

Handelsplattformen ska kunna säljas ensam (ADR 0001), så den behöver en egen inloggning. Den är vårt eget varumärke, som TradeLocker och cTrader, och inte white label. Propfirm-plattformen är white label och använder handelsplattformen.

Flera firmor använder samma plattform. Samma trader kan handla hos flera firmor med samma e-postadress. Firmans egna system, till exempel propfirm-plattformen, ska kunna skapa traders och konton, men aldrig röra en annan firmas.

Den första versionen avgjorde firman av adressen tradern gick till, med firmans logga och färger. Det passar inte när alla traders loggar in hos oss.

## Beslut

- **Firmor (tenants)** konfigureras med id, namn, handelsgrupper och en API-nyckel. Id:t är firmans server, med små bokstäver, siffror och bindestreck, till exempel `nordic-prop`. Konfigurationen kontrolleras vid start. Firmor flyttas till databasen när de ska kunna registrera sig själva.
- **Traders loggar in med server, e-post och lösenord**, som i MetaTrader och TradeLocker. Tradern får uppgifterna från firmans portal. E-postadressen är unik inom en firma, inte mellan firmor, så samma e-post hos två firmor är två traders med var sitt lösenord.
- **Servrarna listas öppet** med id och namn, så att inloggningssidan kan visa dem. Firmans portal kan länka med servern vald, till exempel `/login?server=nordic-prop`. Terminalen kommer ihåg den senast använda servern på enheten.
- **Fel server, e-post eller lösenord** ger samma svar och tar lika lång tid. Inloggning begränsas till 10 försök per minut och IP-adress (en inställning, avstängd i utveckling).
- **Sessioner** är en HttpOnly-cookie (SameSite=Lax) med 12 timmars glidande giltighet (en inställning, 30 dagar i utveckling). Nycklarna som skyddar cookien sparas i Postgres, så att sessioner överlever omstarter och fungerar på flera instanser. En firma som tas bort ur konfigurationen avslutar sina traders sessioner.
- **Lösenord** hashas med ASP.NET Core Identitys `PasswordHasher` (PBKDF2).
- **Konton har en ägare.** Bara ägaren kommer åt kontot via API:t och realtid. Andras konton svarar 404, så att det inte avslöjas att de finns.
- **Firmans system** använder headern `X-Api-Key`. Bara nyckelns SHA-256 sparas. Nyckeln ger bara åtkomst till firmans egna grupper, användare och konton.
- **Inget white label i handelsplattformen.** Terminalen har vårt utseende. Firman syns som server på inloggningssidan och som namn bredvid kontot.

## Konsekvenser

- Propfirm-plattformen skapar traders och konton via admin-API:t och visar inloggningsuppgifterna och servern i portalen. Med en engångslänk från admin-API:t går tradern från portalen till terminalen utan att logga in igen (ADR 0012).
- Listan över servrar visar vilka firmor som använder plattformen. Det är normalt för MetaTrader och TradeLocker. Med många firmor behövs sökning i stället för en lista.
- En trader hos flera firmor loggar in en gång per firma.
- Tjänsten kör fortfarande bara i miljön Development. Innan den kan köras i produktion behövs HTTPS, hantering av hemligheter och ett prisflöde med licens.
- Tradern kan inte själv byta eller återställa lösenordet än. Firmans system kan byta det via admin-API:t.
