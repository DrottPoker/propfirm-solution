# Spec: portalen

- Fas: 4d, utbetalningar i 5, registrering och adminpanelens inställningar i 6, platser och betalning i 7, köp i 8
- Status: Implementerad i `prop/portal` och `prop/src/Prop.Api/Portal`
- Datum: 2026-10-03

## Syfte

Portalen är firmans egen sida för traders och administratörer, med firmans namn, logga, färger och domän. Tradern köper challenges, följer dem, öppnar handelsterminalen och begär utbetalningar härifrån. Firman startar challenges, bjuder in traders, godkänner funded-konton, hanterar utbetalningar och avbryter konton i adminpanelen. På plattformens egen adress visar samma portal i stället registreringen, med vårt namn (se [specen för registrering och sandlåda](registrering.md)). Besluten finns i [ADR 0014](../adr/0014-vitmarkt-portal-pa-firmans-adress.md) och [ADR 0017](../adr/0017-firmor-registrerar-sig-sjalva.md). Kontona och regelmotorn beskrivs i [specen för propfirm-tjänsten](propfirm-tjanst.md).

## Sidor

| Sida | För | Innehåll |
|---|---|---|
| `/login` | Traders | Inloggning med e-post och lösenord, och en länk till butiken när firman säljer i portalen. |
| `/buy` | Alla | Challengerna till salu med regler och pris. Köparen anger e-post, eller köper som inloggad trader, godkänner firmans villkor och skickas till betalningen. Se [specen för köp i portalen](kop.md). |
| `/orders/{id}?token=` | Köparen | Ordern efter betalningen. Väntar på att leverantören bekräftar, och berättar sedan hur tradern kommer in i portalen. |
| `/checkout/test?order=&token=` | Köparen | Testbetalning utan pengar, för firmor i sandlådan. |
| `/invite?token=` | Traders | Tradern väljer lösenord med firmans inbjudan och loggas in. |
| `/` | Traders | Översikt över ett konto: status, fas, saldo, equity, vinstmål med förlopp, handelsdagar, förlustgränser med marginal, sista dagen att öppna en ny affär och att klara fasen, att kontot är pausat, och vid brott eller när tiden tagit slut när och varför. Knappen "Open terminal". För ett funded-konto vinsten, traderns andel, handelsdagar sedan förra utbetalningen och knappen "Request payout", och kontots utbetalningar. Har tradern flera konton väljs ett med `?account=`. |
| `/admin/login` | Administratörer | Inloggning för firmans administratörer. |
| `/admin` | Administratörer | Starta en challenge åt en trader och sök konton på e-post och status. Pausade konton är markerade. Medan firmans server skapas visas det i stället. |
| `/admin/accounts/{id}` | Administratörer | Kontots översikt, inbjudningslänk till portalen, godkännande av funded-kontot, annullering med orsak, kontots utbetalningar med firmans beslut och hela historiken. |
| `/admin/orders` | Administratörer | Ordrar från portalen: köpare, challenge, belopp, leverantör, status och återbetalningar eller bestridanden. Ordrar från firmans egen betalsida markeras som betalda härifrån, och ordrar som inte gick genom Stripe som återbetalda. |
| `/admin/payouts` | Administratörer | Utbetalningar som väntar på firman, eller alla. Firman godkänner, markerar som betald med en valfri referens, eller nekar med en orsak som tradern ser. |
| `/admin/challenges` | Administratörer | Firmans challenges. En ny challenge utgår från mallen, och befintliga kan ändras: storlek, handelsdagens start och tidszon, dagar utan en ny affär innan challengen tar slut, och varje fas mål, handelsdagar, förlustgränser, vinstandel och tidsgräns. En till tre faser före funded. Varje challenge har sitt pris i portalen, i en valuta som kan vara en annan än kontots, och om den säljs där. |
| `/admin/billing` | Administratörer | Gå live genom att betala, platserna, fler eller färre platser, automatisk utökning, kortet, obetalda månader och debiteringarna. Se [specen för platser och betalning](platser-och-betalning.md). |
| `/admin/billing/checkout/{id}` | Administratörer | Testsidan för betalningar till plattformen och kort, i utveckling. |
| `/admin/team` | Administratörer | Administratörerna, inbjudningar som väntar, en ny inbjudan med e-post, och borttagning av andra än sig själv. |
| `/admin/settings` | Administratörer | Firman och dess status, logga och färger med förhandsvisning, betalningar (ingen försäljning, testbetalning, Stripe med firmans nycklar och adressen för Stripes webhook, eller firmans egen betalsida, och villkoren för köpare), nyckel för firmans API och webhookens adress och hemlighet. Nycklar och hemligheter visas bara en gång. |
| `/admin/welcome?token=` | Alla | Engångslänken efter registreringen. Loggar in administratören och öppnar adminpanelen. |
| `/admin/invite?token=` | Alla | En inbjuden administratör väljer lösenord och loggas in. |
| `/signup`, `/verify?token=` | Plattformen | Registreringen och bekräftelsen av e-postadressen. Se [specen för registrering och sandlåda](registrering.md). |

- Den som inte är inloggad skickas till rätt inloggning, och den som är inloggad med den andra rollen till sin egen startsida.
- Kontots siffror uppdateras var femte sekund.
- När firman säljer i portalen har traderns sidhuvud länken "Buy a challenge", och en trader utan konton får en knapp till butiken.
- Knappen "Request payout" fungerar när regelmotorn säger att en utbetalning kan begäras. Annars visas orsaken. Tradern bekräftar först, eftersom hela vinsten tas från handelskontot direkt.
- Knappen "Open terminal" fungerar när kontot är aktivt. Den hämtar en engångslänk och öppnar terminalen inloggad på fasens konto.
- På en adress som ingen firma har visar portalen bara att ingen portal finns där. På plattformens adress skickas firmans sidor till registreringen, och på en firmas adress finns inte plattformens sidor.
- En firma i sandlådan visar en rad om att det är en testmiljö på alla sidor, så att ingen trader tror att den är på riktigt.
- Adminpanelen visar en rad på alla sidor när firmans månad är obetald, när en betalning nekats eller när platserna är slut eller nästan slut.
- Ett pausat konto kan öppnas i terminalen, där tradern kan stänga sina positioner men inte öppna nya.

## Utseende

Utseendet hämtas på servern med `GET /api/portal/branding` innan sidan visas, tillsammans med firmans status. Färgerna blir CSS-variabler och kontrolleras en gång till i portalen innan de används. En registrerad firma ändrar logga och färger under Settings, och sidan ritas om med det nya utseendet när det sparas.

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
| `GET /branding` | Alla | Firmans namn, logga, färger och status (`Provisioning`, `Sandbox` eller `Live`). |
| `POST /login` | Alla | Traderns inloggning med `{ "email", "password" }`. 401 vid fel, och för en trader som inte har valt lösenord. |
| `POST /invites/accept` | Alla | `{ "token", "password" }`. Sätter lösenordet och loggar in som trader. 422 för ett lösenord som är kortare än `Login:MinimumPasswordLength`, utan att inbjudan förbrukas. 401 för en okänd, använd, ersatt eller utgången inbjudan. |
| `POST /admin/login` | Alla | Administratörens inloggning. |
| `POST /logout` | Alla | Tar bort traderns session. En administratörs session finns kvar. |
| `POST /admin/logout` | Alla | Tar bort administratörens session. En traders session finns kvar. |
| `GET /me` | Trader | Traderns id, e-post, roll och firmans namn. |
| `GET /admin/me` | Admin | Samma för administratören. |
| `GET /accounts` | Trader | Traderns konton. |
| `GET /accounts/{id}` | Trader | Kontot med `live` (saldo, equity och golv med marginal från handelsplattformen), `breach` (tid, golv, nivå, equity och orsak) när ett golv bröts, `expiry` (tid, orsak och dag) när tiden tog slut och `payouts`, kontots utbetalningar. Andras konton svarar 404. |
| `POST /accounts/{id}/terminal-link` | Trader | En engångslänk till terminalen. 409 när kontot inte är aktivt. |
| `POST /accounts/{id}/payouts` | Trader | Begär en utbetalning av funded-kontots vinst. 201 med utbetalningen, eller 409 med orsaken. |
| `GET /admin/challenges` | Admin | Firmans challenges. |
| `GET /admin/accounts?email=&status=&limit=` | Admin | Firmans nyaste konton, högst 500. |
| `POST /admin/accounts` | Admin | Startar en challenge, som i firmans API. |
| `GET /admin/accounts/{id}` | Admin | Kontot med `live` och `breach`. |
| `GET /admin/accounts/{id}/history` | Admin | Varje indata och beslut. |
| `POST /admin/accounts/{id}/approve-funding` | Admin | Godkänner funded-kontot. 409 innan faserna är klara. |
| `POST /admin/accounts/{id}/cancel` | Admin | Avbryter med `{ "reason" }`. |
| `POST /admin/accounts/{id}/invite` | Admin | En inbjudningslänk till portalen för kontots trader. |
| `GET /admin/payouts?status=&limit=` | Admin | Firmans nyaste utbetalningar, högst 500, valfritt bara de med vissa statusar. |
| `POST /admin/payouts/{payoutId}/approve` | Admin | Godkänner en utbetalning som väntar. |
| `POST /admin/payouts/{payoutId}/mark-paid` | Admin | Markerar en godkänd utbetalning som betald, med `{ "reference" }`. |
| `POST /admin/payouts/{payoutId}/reject` | Admin | Nekar en utbetalning som väntar eller är godkänd, med `{ "reason" }`. |
| `POST /admin/welcome`, `POST /admin/invites/accept` | Alla | Inloggning med länken efter registreringen, och en inbjuden administratörs val av lösenord. |
| `GET /admin/firm` och vägarna under den, `GET /admin/challenge-templates`, `PUT /admin/challenges/{challengeId}`, `/admin/admins` | Admin | Firmans inställningar, challenges och administratörer. Se [specen för registrering och sandlåda](registrering.md). |
| `GET /shop`, `/orders` och vägarna under den | Alla | Butiken och köparens order. Se [specen för köp i portalen](kop.md). |
| `/admin/orders`, `/admin/prices`, `PUT /admin/challenges/{challengeId}/price`, `PUT /admin/firm/payments` | Admin | Ordrar, priser och betalningar. Se [specen för köp i portalen](kop.md). |
| `/admin/billing` och vägarna under den | Admin | Platserna och vad firman betalar oss. Se [specen för platser och betalning](platser-och-betalning.md). |

Inloggningarna och inbjudningar har gemensamt en gräns på `Login:AttemptsPerMinute` försök i minuten per IP-adress, som standard 10. Svaret är då 429. Webbläsarens adress och protokoll tas från `X-Forwarded-For` och `X-Forwarded-Proto` när anropet kommer från en betrodd proxy, lokalt bara loopback. Annars skulle alla som når tjänsten genom portalen dela samma gräns.

## Sessioner och lösenord

- Traders och administratörer har var sin session, i cookien `prop_trader` respektive `prop_admin`. Samma webbläsare kan alltså vara inloggad som trader och som administratör samtidigt, och en inbjudan som används där loggar inte ut administratören.
- Cookies hör till värdnamnet. Lokalt kan en andra trader därför vara inloggad samtidigt på http://127.0.0.1:3002, medan den första är det på http://localhost:3002.
- Cookierna är HttpOnly och SameSite Lax, och gäller tills de har varit oanvända i `Login:SessionLifetime`, som standard 12 timmar. De markeras Secure när webbläsaren använder HTTPS.
- Nycklarna som skyddar cookierna sparas i tabellen `data_protection_keys`, så sessioner överlever en omstart.
- En administratör som tas bort loggas ut direkt: varje anrop med administratörens cookie kontrollerar att administratören finns kvar.
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

Per konfigurerad firma under `Firms`. Firmor som registrerat sig ställer in samma saker i adminpanelen.

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

- Köp i portalen betalas till firmans egen leverantör: Stripe, firmans egen betalsida eller testbetalning i sandlådan. Firmor med en egen butik startar konton via API:t som förut.
- Plattformen mejlar bekräftelser och inbjudningar till administratörer, och en inbjudan till köpare som inte har något lösenord. Andra inbjudningar till traders skickar firman själv.
- Firman skickar pengarna till tradern själv. Portalen markerar bara utbetalningen som betald.
- Sessioner återkallas inte när ett lösenord byts. En borttagen administratör loggas ändå ut direkt.
- Egen domän med TLS-certifikat sätts upp för hand.

## Tester

- `prop/tests/Prop.Api.Tests/PortalApiTests`: utseende per värdnamn, inbjudan och inloggning, att inbjudan bara fungerar en gång, inte efter 7 dagar och ersätts av en ny, fel lösenord, att traders bara ser sina konton, siffror i realtid och när handelsplattformen inte svarar, bevis vid brott, att sessioner och inbjudningar bara gäller hos sin firma, att adminpanelen bara är för administratörer, adminpanelens flöden, att administratörer bara ser sin firma, att en session överlever en omstart, att gränsen för inloggning räknas per webbläsare bakom portalen, att en administratör och en trader kan vara inloggade samtidigt i samma webbläsare, att konfigurerade traders loggar in med sitt lösenord efter varje start och att reglerna för inloggning går att stänga av.
- `prop/tests/Prop.Api.Tests/PayoutFlowTests`: traderns begäran och administratörens beslut i portalen, och att en trader inte når andras konton eller adminpanelens beslut.
- `prop/tests/Prop.Api.Tests/OrderFlowTests`: köp i portalen. Se [specen för köp i portalen](kop.md).
- `prop/portal/src/lib/*.test.ts`: platserna, prisstegen och raden om betalningen i adminpanelen, sista dagen att handla och att klara fasen, vilka ordrar firman kan markera och vad köparens ordersida säger, förlopp mot vinstmålet, vilka golv som visas, när knapparna fungerar, vilka beslut som går att fatta om en utbetalning, utbetalningarnas texter, färgerna och att standardfärgerna är de i `globals.css`, förslaget på kort namn och kontrollen av det, och challenge-redigeraren fram och tillbaka med tidsgräns och dagar utan en ny affär.
- `prop/portal/e2e`: hela kedjan med handelsplattformen, propfirm-tjänsten och portalen. En konfigurerad trader loggar in med sitt korta lösenord. Administratören startar en challenge och skapar en inbjudan, tradern väljer lösenord i samma webbläsare, ser kontot och öppnar terminalen med en länk som handelsplattformen godtar, och administratören avbryter kontot. En trader klarar challengen `quick-test-100k` med handel och insättningar på handelsplattformen, begär en utbetalning i portalen, och administratören godkänner och markerar den som betald. En firma registrerar sig på `app.localhost`, hamnar i sin adminpanel på sin egen adress, ser att den är i sandlådan, får sin server och startar en challenge, byter färg och gör en egen challenge med en fas. Registreringen finns bara på plattformens adress, och tagna och reserverade korta namn visas medan de skrivs. En besökare köper en challenge med testbetalning och firman ser den betalda ordern, och en inloggad trader köper och kommer direkt till sitt nya konto. En firma går live med testbetalning efter ett kort som nekas, ser sina platser och sitt kort och köper fler platser.
