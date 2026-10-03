# 0009. Inloggning och firmor i handelsplattformen

- Status: Föreslagen
- Datum: 2026-10-03

## Sammanhang

Handelsplattformen ska kunna säljas ensam (ADR 0001), så den behöver en egen inloggning. Samma trader kan ha konton hos flera firmor med samma e-postadress. Firmans egna system, till exempel propfirm-plattformen, ska kunna skapa traders och konton, men aldrig röra en annan firmas.

## Beslut

- **Firmor (tenants)** konfigureras med id, domäner, handelsgrupper, en API-nyckel och utseende. Konfigurationen kontrolleras vid start. Firmor flyttas till databasen när de ska kunna registrera sig själva.
- **Traders** loggar in med e-post och lösenord. E-postadressen är unik inom en firma, inte mellan firmor. Firman avgörs av adressen som terminalen och API:t nås på, så en trader kan bara logga in hos sin egen firma.
- **Sessioner** är en HttpOnly-cookie (SameSite=Lax) med 12 timmars glidande giltighet. Nycklarna som skyddar cookien sparas i Postgres, så att sessioner överlever omstarter och fungerar på flera instanser.
- **Lösenord** hashas med ASP.NET Core Identitys `PasswordHasher` (PBKDF2). Fel e-post och fel lösenord ger samma svar och tar lika lång tid. Inloggning begränsas till 10 försök per minut och IP-adress.
- **Konton har en ägare.** Bara ägaren kommer åt kontot via API:t och realtid. Andras konton svarar 404, så att det inte avslöjas att de finns.
- **Firmans system** använder headern `X-Api-Key`. Bara nyckelns SHA-256 sparas. Nyckeln ger bara åtkomst till firmans egna grupper, användare och konton.
- **White label:** firman kan ange namn, logga (https) och färger som `#rrggbb`. Färgerna kontrolleras både i tjänsten och i terminalen, eftersom de hamnar i CSS.

## Konsekvenser

- Propfirm-plattformen skapar traders och konton via admin-API:t. När den byggs behövs en engångsinloggning, så att en trader kan gå från portalen till terminalen utan att logga in igen.
- Tjänsten kör fortfarande bara i miljön Development. Innan den kan köras i produktion behövs HTTPS, hantering av hemligheter och ett prisflöde med licens.
- Byte av lösenord och återställning av lösenord finns inte än.
