# Spec: registrering och sandlåda

- Fas: 6
- Status: Implementerad i `prop/src/Prop.Api`, `prop/portal` och handelstjänstens partner-API
- Datum: 2026-10-03

## Syfte

En firma ska kunna komma igång själv, utan att prata med oss: registrera sig, få en egen portal och en egen server på handelsplattformen, ställa in utseende och challenges och prova hela kedjan i en sandlåda. Besluten finns i [ADR 0016](../adr/0016-firmor-skapas-medan-handelsplattformen-kor.md) för handelsplattformen och [ADR 0017](../adr/0017-firmor-registrerar-sig-sjalva.md) för propfirm-plattformen.

## Flöde

```
plattformens adress /signup -> firmanamn, kort namn, e-post, lösenord, villkoren
-> bekräftelselänk med e-post (avstängt i utveckling)
-> /verify på plattformens adress -> firman, adressen, administratören och en challenge skapas
-> engångslänk till {kort namn}.<vår domän>/admin/welcome -> administratören är inloggad
bakgrunden: partner-API:t skapar servern och gruppen på handelsplattformen -> firman är i sandlådan
adminpanelen: utseende, challenges, administratörer, API-nyckel och webhook
```

Lokalt ligger plattformen på http://app.localhost:3002 och en ny firma på till exempel http://acme.localhost:3002.

## Firmans status

| Status | Betyder |
|---|---|
| `Provisioning` | Firman finns, men servern på handelsplattformen skapas fortfarande. Konton kan inte startas än. |
| `Sandbox` | Allt fungerar, men firman kan ha högst `Sandbox:MaxOpenAccounts` öppna challenge-konton (standard 10). Portalen visar att det är en testmiljö. |
| `Live` | Firman har platser för sina challenges. Firmor i konfigurationen är live och betalar inte. En registrerad firma går live när vi har granskat och godkänt den (se [specen för granskning och avstängning](granskning.md)), genom att betala startavgiften och första månaden (se [specen för platser och betalning](platser-och-betalning.md)). |

## Det korta namnet

- 2 till 40 tecken: små bokstäver, siffror och bindestreck, inte först eller sist.
- Är firmans id, underdomänen för portalen och servern på handelsplattformen. Det kan inte bytas.
- Reserverade namn, till exempel `www`, `app`, `api`, `admin`, `ops`, `portal`, `mail` och `status`, kan inte väljas. Fler kan läggas till i `Signup:ReservedFirmIds`.
- Ett namn är upptaget om en firma har det, eller om en obekräftad registrering från en annan e-postadress har det. Vid registreringen frågas också handelsplattformen om servern är ledig. Svarar den inte fortsätter registreringen, eftersom servern ändå skapas senare.

## Handelsplattformen: partner-API

Propfirm-plattformen skapar firmans server och grupp med handelsplattformens partner-API under `/api/partner/v1` (ADR 0016). Vägarna beskrivs i [specen för handelstjänsten](handelstjanst.md). En ny firma får en kopia av mallgrupperna, med id som `acme-standard`, och syns inte i serverlistan förrän den går live.

## Propfirm-tjänsten: registrering

Vägarna ligger under `/api/portal` så att portalen skickar dem vidare, men fungerar bara på plattformens adress. Registrering och bekräftelse har samma gräns för försök som inloggningen.

| Metod och väg | Beskrivning |
|---|---|
| `GET /platform` | Plattformens namn, villkorens version och länkar, mallen för firmornas adresser och lösenordets minsta längd. 404 på andra adresser. |
| `GET /signup/availability?firmId=` | Om det korta namnet går att välja, annars varför inte. |
| `POST /signup` | `{ "firmName", "firmId", "email", "password", "acceptTerms" }`. Med krav på bekräftelse skickas ett mejl och svaret är 202. Utan krav skapas firman direkt och svaret är 200 med `adminUrl`. 422 med orsaken för fel i fälten, 409 för ett upptaget namn. En ny registrering med samma e-postadress ersätter den gamla. |
| `POST /signup/verify` | `{ "token" }`. Skapar firman och svarar med `adminUrl`, en engångslänk som loggar in administratören på firmans adress. 401 för en okänd, använd eller utgången länk, 409 om namnet hann tas. |

På firmans adress:

| Metod och väg | Roll | Beskrivning |
|---|---|---|
| `POST /admin/welcome` | Alla | `{ "token" }` från `adminUrl`. Loggar in administratören. Länken gäller en gång i 10 minuter. |
| `POST /admin/invites/accept` | Alla | `{ "token", "password" }`. Skapar administratören från en inbjudan och loggar in. |
| `GET /admin/firm` | Admin | Firmans id, namn, status, portalens adress, utseende, server på handelsplattformen, om en API-nyckel finns, webhookens adress och betalningarna. |
| `PUT /admin/firm/branding` | Admin | `{ "logoUrl", "colors" }`. Loggan är en https-adress eller tom, färgerna portalens färger som `#rrggbb`. |
| `POST /admin/firm/api-key` | Admin | En ny nyckel till firmans API. Visas bara nu, och den gamla slutar fungera. |
| `PUT /admin/firm/webhook` | Admin | `{ "url" }`, en https-adress eller tom för ingen. Första gången skapas en hemlighet, som visas i svaret. |
| `POST /admin/firm/webhook/secret` | Admin | En ny hemlighet för webhooks. Visas bara nu. |
| `PUT /admin/firm/payments` | Admin | Hur portalen tar betalt för challenges. I sandlådan fungerar testbetalningar och Stripes testnycklar. Se [specen för köp i portalen](kop.md). |
| `GET /admin/challenge-templates` | Admin | Mallarna att börja från, med standardvärden. |
| `PUT /admin/challenges/{challengeId}` | Admin | Skapar eller ersätter en challenge, som i firmans API. Konton som redan har startat behåller sina regler. |
| `GET /admin/admins` | Admin | Firmans administratörer. |
| `POST /admin/admins/invites` | Admin | `{ "email" }`. Skickar en inbjudan som gäller en gång i 7 dagar. 409 om personen redan är administratör. |
| `DELETE /admin/admins/{adminId}` | Admin | Tar bort en administratör, vars session slutar fungera direkt. 409 för sig själv. |

`GET /branding` har också firmans `status`, så att portalen kan visa sandlådan.

## Portalen

| Sida | Adress | Innehåll |
|---|---|---|
| `/signup` | Plattformens | Registrering med kontroll av det korta namnet medan det skrivs, och förhandsvisning av adressen. |
| `/verify?token=` | Plattformens | Bekräftar e-postadressen och skickar vidare till firmans adminpanel. |
| `/admin/welcome?token=` | Firmans | Loggar in administratören efter registreringen. |
| `/admin/invite?token=` | Firmans | En inbjuden administratör väljer lösenord. |
| `/admin/challenges` | Firmans | Firmans challenges. Ny challenge från mall, och ändring av befintliga. |
| `/admin/team` | Firmans | Administratörer, inbjudningar och borttagning. |
| `/admin/settings` | Firmans | Logga och färger med förhandsvisning, API-nyckel och webhook. |

Alla sidor på en firma i sandlådan visar en rad om att det är en testmiljö. Medan servern skapas visar adminpanelen det, och knappen för att starta en challenge väntar.

## Tabeller

Propfirm-tjänsten:

| Tabell | Innehåll |
|---|---|
| `firms` | Firman: namn, status, om den är konfigurerad, hash av API-nyckeln, server, krypterad nyckel, grupp och valuta på handelsplattformen, webhookens adress och krypterade hemlighet, portalens adress, logga, färger, villkorens version och när de godkändes. |
| `firm_hosts` | Värdnamnen portalen nås på. Ett värdnamn hör till en firma. |
| `firm_signups` | Registreringar som väntar på bekräftelse: namnen, e-post, hash av lösenordet och av länkens token, när den går ut och när den användes. |
| `admin_invites` | Inbjudningar till administratörer: hash av token, e-post, när den går ut och när den användes. |
| `admin_login_links` | Engångslänkar som loggar in en administratör: hash av token, administratören och när den går ut. |

Handelstjänsten:

| Tabell | Innehåll |
|---|---|
| `tenants` | Firman: server, namn, SHA-256 av nyckeln till admin-API:t, partnern som skapade den och om den listas. |
| `tenant_groups` | Vilken firma varje grupp hör till. |

## Konfiguration

Propfirm-tjänsten:

| Sektion | Innehåll |
|---|---|
| `Platform:Name` | Plattformens namn på registreringen och i mejlen. Ett arbetsnamn tills produkten har ett namn. |
| `Platform:Url` | Plattformens adress, där registreringen ligger. Slutar med `/`. |
| `Platform:FirmPortalUrl` | Mall för en ny firmas portal, med `{firm}` för det korta namnet, till exempel `https://{firm}.example.com/`. |
| `Signup:RequireEmailVerification` | Om e-postadressen ska bekräftas innan firman skapas. Standard ja, avstängt i utveckling. |
| `Signup:TermsVersion`, `Signup:TermsUrl`, `Signup:DpaUrl` | Villkorens version och var villkoren och personuppgiftsbiträdesavtalet finns. |
| `Signup:ReservedFirmIds` | Fler reserverade korta namn. |
| `Sandbox:MaxOpenAccounts` | Högst så många öppna challenge-konton i sandlådan. Standard 10. |
| `Email` | `From`, `FromName` och `Smtp` (`Host`, `Port`, `UserName`, `Password` och `Security`, som är `None`, `StartTls` eller `SslOnConnect`). Lokalt Mailpit på port 1025, utan kryptering. |
| `Secrets:Key` | 32 slumpade byte i base64, som krypterar hemligheterna i databasen. Får aldrig ligga i en fil utanför utveckling. |
| `TradingPlatform:PartnerApiKey` | Propfirm-plattformens nyckel till partner-API:t. |

Handelstjänsten:

| Sektion | Innehåll |
|---|---|
| `Partners` | Partnerna: `Id`, `Name` och `ApiKeySha256`. |
| `Tenancy:NewTenantGroups` | Grupperna i `Trading:Groups` som en ny firma får en kopia av. |

## Begränsningar

- Egen domän sätts fortfarande upp för hand.
- Firman kan inte byta namn eller kort namn.
- Mejl till traders skickas av firman själv.
- Mallen är bara tvåstegs-challengen, och challenges kan inte tas bort.

## Tester

- `trading/tests/Trading.Engine.Tests/GroupTests`: grupper som skapas medan motorn kör (se [specen för handelsmotorn](handelsmotor.md)).
- `trading/tests/Trading.Service.Tests/PartnerApiTests` och `PostgresIdentityTests`: partner-API:t och firmorna i databasen (se [specen för handelstjänsten](handelstjanst.md)).
- `prop/tests/Prop.Api.Tests/SignupTests` och `AdminSettingsTests`: registreringen, servern i bakgrunden, sandlådan och adminpanelens inställningar (se [specen för propfirm-tjänsten](propfirm-tjanst.md)). Den låtsade handelsplattformen har ett partner-API och tar bara emot en servers senaste nyckel, och ett låtsat mejlsystem tar emot mejlen.
- `prop/portal/e2e/signup.spec.ts`: hela vägen i en webbläsare, från registreringen på `app.localhost` till en challenge i den nya firmans sandlåda (se [specen för portalen](portal.md)).
