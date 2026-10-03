# Spec: portalen

- Fas: 4d
- Status: Implementerad i `prop/portal` och `prop/src/Prop.Api/Portal`
- Datum: 2026-10-03

## Syfte

Portalen är firmans egen sida för traders och administratörer, med firmans namn, logga, färger och domän. Tradern följer sina challenges och öppnar handelsterminalen härifrån. Firman startar challenges, bjuder in traders, godkänner funded-konton och avbryter konton i adminpanelen. Besluten finns i [ADR 0014](../adr/0014-vitmarkt-portal-pa-firmans-adress.md). Kontona och regelmotorn beskrivs i [specen för propfirm-tjänsten](propfirm-tjanst.md).

## Sidor

| Sida | För | Innehåll |
|---|---|---|
| `/login` | Traders | Inloggning med e-post och lösenord. |
| `/invite?token=` | Traders | Tradern väljer lösenord med firmans inbjudan och loggas in. |
| `/` | Traders | Översikt över ett konto: status, fas, saldo, equity, vinstmål med förlopp, handelsdagar, förlustgränser med marginal, och vid brott när och varför. Knappen "Open terminal". Har tradern flera konton väljs ett med `?account=`. |
| `/admin/login` | Administratörer | Inloggning för firmans administratörer. |
| `/admin` | Administratörer | Starta en challenge åt en trader, sök konton på e-post och status, och firmans challenges. |
| `/admin/accounts/{id}` | Administratörer | Kontots översikt, inbjudningslänk till portalen, godkännande av funded-kontot, annullering med orsak och hela historiken. |

- Den som inte är inloggad skickas till rätt inloggning, och den som är inloggad med den andra rollen till sin egen startsida.
- Kontots siffror uppdateras var femte sekund.
- Knappen "Open terminal" fungerar när kontot är aktivt. Den hämtar en engångslänk och öppnar terminalen inloggad på fasens konto.
- På en adress som ingen firma har visar portalen bara att ingen portal finns där.

## Utseende

Utseendet hämtas på servern med `GET /api/portal/branding` innan sidan visas. Färgerna blir CSS-variabler och kontrolleras en gång till i portalen innan de används.

| Färg | Används till |
|---|---|
| `background`, `panel`, `border` | Bakgrund, paneler och kanter |
| `foreground`, `muted` | Text och dämpad text |
| `accent` | Knappar, länkar och markeringar |
| `profit`, `loss`, `warning` | Vinst och förlopp, brott och fel, varningar |

## Portalens API i propfirm-tjänsten

Alla vägar börjar med `/api/portal`. Firman känns igen på `X-Forwarded-Host`, eller `Host` om den saknas. Ett okänt värdnamn svarar 404, och en session från en annan firmas adress svarar 401.

| Metod och väg | Roll | Beskrivning |
|---|---|---|
| `GET /branding` | Alla | Firmans namn, logga och färger. |
| `POST /login` | Alla | Traderns inloggning med `{ "email", "password" }`. 401 vid fel, och för en trader som inte har valt lösenord. |
| `POST /invites/accept` | Alla | `{ "token", "password" }`. Sätter lösenordet och loggar in som trader. 422 för ett lösenord som är kortare än `Login:MinimumPasswordLength`, utan att inbjudan förbrukas. 401 för en okänd, använd, ersatt eller utgången inbjudan. |
| `POST /admin/login` | Alla | Administratörens inloggning. |
| `POST /logout` | Alla | Tar bort traderns session. En administratörs session finns kvar. |
| `POST /admin/logout` | Alla | Tar bort administratörens session. En traders session finns kvar. |
| `GET /me` | Trader | Traderns id, e-post, roll och firmans namn. |
| `GET /admin/me` | Admin | Samma för administratören. |
| `GET /accounts` | Trader | Traderns konton. |
| `GET /accounts/{id}` | Trader | Kontot med `live` (saldo, equity och golv med marginal från handelsplattformen) och `breach` (tid, golv, nivå, equity och orsak) när kontot har underkänts. Andras konton svarar 404. |
| `POST /accounts/{id}/terminal-link` | Trader | En engångslänk till terminalen. 409 när kontot inte är aktivt. |
| `GET /admin/challenges` | Admin | Firmans challenges. |
| `GET /admin/accounts?email=&status=&limit=` | Admin | Firmans nyaste konton, högst 500. |
| `POST /admin/accounts` | Admin | Startar en challenge, som i firmans API. |
| `GET /admin/accounts/{id}` | Admin | Kontot med `live` och `breach`. |
| `GET /admin/accounts/{id}/history` | Admin | Varje indata och beslut. |
| `POST /admin/accounts/{id}/approve-funding` | Admin | Godkänner funded-kontot. 409 innan faserna är klara. |
| `POST /admin/accounts/{id}/cancel` | Admin | Avbryter med `{ "reason" }`. |
| `POST /admin/accounts/{id}/invite` | Admin | En inbjudningslänk till portalen för kontots trader. |

Inloggningarna och inbjudningar har gemensamt en gräns på `Login:AttemptsPerMinute` försök i minuten per IP-adress, som standard 10. Svaret är då 429. Webbläsarens adress och protokoll tas från `X-Forwarded-For` och `X-Forwarded-Proto` när anropet kommer från en betrodd proxy, lokalt bara loopback. Annars skulle alla som når tjänsten genom portalen dela samma gräns.

## Sessioner och lösenord

- Traders och administratörer har var sin session, i cookien `prop_trader` respektive `prop_admin`. Samma webbläsare kan alltså vara inloggad som trader och som administratör samtidigt, och en inbjudan som används där loggar inte ut administratören.
- Cookies hör till värdnamnet. Lokalt kan en andra trader därför vara inloggad samtidigt på http://127.0.0.1:3002, medan den första är det på http://localhost:3002.
- Cookierna är HttpOnly och SameSite Lax, och gäller tills de har varit oanvända i `Login:SessionLifetime`, som standard 12 timmar. De markeras Secure när webbläsaren använder HTTPS.
- Nycklarna som skyddar cookierna sparas i tabellen `data_protection_keys`, så sessioner överlever en omstart.
- Lösenord sparas som PBKDF2-hashar. En okänd e-postadress tar lika lång tid som ett fel lösenord.
- En inbjudan gäller en gång i 7 dagar. Bara en SHA-256-hash av dess token sparas. En ny inbjudan tar bort traderns äldre oanvända.

## Konfiguration

Portalen:

| Variabel | Innehåll |
|---|---|
| `PROP_API_URL` | Propfirm-tjänstens adress, som standard http://localhost:5201. Läses när portalen startar eller byggs. |

Propfirm-tjänsten:

| Sektion | Innehåll |
|---|---|
| `Login` | Regler för lösenord och inloggning: `MinimumPasswordLength` (standard 10), `AttemptsPerMinute` per IP-adress (standard 10, 0 för ingen gräns) och `SessionLifetime`, hur länge en oanvänd session gäller (standard 12 timmar). I utveckling är reglerna avstängda och sessionen gäller i 30 dagar. |

Per firma under `Firms`:

| Inställning | Innehåll |
|---|---|
| `Portal:Url` | Portalens adress, som slutar med `/`. Används i inbjudningslänkarna. |
| `Portal:Hosts` | Värdnamnen portalen nås på, utan port. Ett värdnamn hör till en firma. |
| `Portal:LogoUrl` | Loggans adress med https, eller tomt. |
| `Portal:Colors` | Färger som ersätter standardfärgerna, som `#rrggbb`. |
| `SeedAdmins` | Administratörer med `Email` och `Password`. Skapas vid varje start, eller får det konfigurerade lösenordet igen. Bara för utveckling, tills firmor registrerar sig själva. |
| `SeedTraders` | Traders på samma sätt, så att de kan logga in utan inbjudan. Bara för utveckling. |

I utveckling nås `demo-firm` på `localhost` och `127.0.0.1` med lila accentfärg. Administratören är `admin@test.com` med lösenordet `admin`, och traderna `anna@test.com` med `anna` och `test@test.com` med `test`. Reglerna för lösenord och inloggning är avstängda. Det gäller bara lokal utveckling.

## Begränsningar

- Inget köp i portalen. Firman tar betalt själv och startar kontot via API:t eller adminpanelen.
- Firman skickar inbjudan själv. Portalen skickar inga e-postmeddelanden.
- Inga utbetalningar.
- Firmans administratörer konfigureras. De kan inte bjuda in fler administratörer i portalen än.
- Sessioner återkallas inte när ett lösenord byts.
- Egen domän med TLS-certifikat sätts upp för hand.

## Tester

- `prop/tests/Prop.Api.Tests/PortalApiTests`: utseende per värdnamn, inbjudan och inloggning, att inbjudan bara fungerar en gång, inte efter 7 dagar och ersätts av en ny, fel lösenord, att traders bara ser sina konton, siffror i realtid och när handelsplattformen inte svarar, bevis vid brott, att sessioner och inbjudningar bara gäller hos sin firma, att adminpanelen bara är för administratörer, adminpanelens flöden, att administratörer bara ser sin firma, att en session överlever en omstart, att gränsen för inloggning räknas per webbläsare bakom portalen, att en administratör och en trader kan vara inloggade samtidigt i samma webbläsare, att konfigurerade traders loggar in med sitt lösenord efter varje start och att reglerna för inloggning går att stänga av.
- `prop/portal/src/lib/*.test.ts`: förlopp mot vinstmålet, vilka golv som visas, när knapparna fungerar och färgerna.
- `prop/portal/e2e`: hela kedjan med handelsplattformen, propfirm-tjänsten och portalen. En konfigurerad trader loggar in med sitt korta lösenord. Administratören startar en challenge och skapar en inbjudan, tradern väljer lösenord i samma webbläsare, ser kontot och öppnar terminalen med en länk som handelsplattformen godtar, och administratören avbryter kontot.
