# Spec: registrering och sandlåda

- Fas: 6, plattformens förstasida, inloggningshjälpen, listningen i terminalen, kontovalutan, namnförslag, välkomstmejlet, guiden och egen domän efter genomgången som ny firma, och spärrarna innan vi godkänt firman
- Status: Implementerad i `prop/src/Prop.Api`, `prop/portal` och handelstjänstens partner-API
- Datum: 2026-10-04

## Syfte

En firma ska kunna komma igång själv, utan att prata med oss: registrera sig, få en egen portal och en egen server på handelsplattformen, ställa in utseende och challenges och prova hela kedjan i en sandlåda. Besluten finns i [ADR 0016](../adr/0016-firmor-skapas-medan-handelsplattformen-kor.md) för handelsplattformen och [ADR 0017](../adr/0017-firmor-registrerar-sig-sjalva.md) för propfirm-plattformen.

## Flöde

```
plattformens adress / -> vad firman får och vad det kostar -> "Start free sandbox"
-> /signup -> firmanamn, kort namn (med lediga förslag när det är taget), kontovaluta, e-post, lösenord, villkoren, robotkontrollen
-> bekräftelselänk med e-post (avstängt i utveckling)
-> /verify på plattformens adress -> firman, adressen och administratören skapas, och ett välkomstmejl köas
-> engångslänk till {kort namn}.<vår domän>/admin/welcome -> administratören är inloggad -> guiden /admin/get-started
bakgrunden: partner-API:t skapar servern och gruppen i firmans valuta på handelsplattformen, med en första challenge -> firman är i sandlådan
adminpanelen: utseende, challenges, administratörer, API-nyckel och webhook
```

Lokalt ligger plattformen på http://app.localhost:3002 och en ny firma på till exempel http://acme.localhost:3002.

## Firmans status

| Status | Betyder |
|---|---|
| `Provisioning` | Firman finns, men servern på handelsplattformen skapas fortfarande. Konton kan inte startas än. |
| `Sandbox` | Allt fungerar, men firman kan ha högst `Sandbox:MaxOpenAccounts` öppna challenge-konton (standard 10). Portalen visar att det är en testmiljö. Tills vi har godkänt firman gäller också [spärrarna nedan](#innan-vi-har-godkänt-firman). |
| `Live` | Firman har platser för sina challenges. Firmor i konfigurationen är live och betalar inte. En registrerad firma går live när vi har granskat och godkänt den (se [specen för granskning och avstängning](granskning.md)), genom att betala startavgiften och första månaden (se [specen för platser och betalning](platser-och-betalning.md)). |

## Det korta namnet

- 2 till 40 tecken: små bokstäver, siffror och bindestreck, inte först eller sist.
- Är firmans id, underdomänen för portalen och servern på handelsplattformen. Det kan inte bytas.
- Reserverade namn, till exempel `www`, `app`, `api`, `admin`, `ops`, `portal`, `mail` och `status`, kan inte väljas. Fler kan läggas till i `Signup:ReservedFirmIds`.
- Ett namn är upptaget om en firma har det, eller om en obekräftad registrering från en annan e-postadress har det. För ett upptaget eller reserverat namn föreslås upp till tre lediga med en vanlig ändelse (`-capital`, `-fx`, `-trading`, `-funded`, `-prop`, `-hq`, `-markets` eller `-group`), utan att handelsplattformen frågas. Vid registreringen frågas också handelsplattformen om servern är ledig. Svarar den inte fortsätter registreringen, eftersom servern ändå skapas senare.

## Handelsplattformen: partner-API

Propfirm-plattformen skapar firmans server och grupp med handelsplattformens partner-API under `/api/partner/v1` (ADR 0016). Vägarna beskrivs i [specen för handelstjänsten](handelstjanst.md). En ny firma får en kopia av mallgrupperna, med id som `acme-standard`, och syns inte i serverlistan förrän den går live. `FirmProvisioner` berättar för handelsplattformen med `PATCH /tenants/{id}` att servern ska listas när firman är live, och att firmans traders loggar in på portalens `/terminal` (ADR 0027). Det som senast skickades sparas i `firms.trading_listing`, så att det skickas igen bara när något ändrats, också efter ett avbrott.

## Propfirm-tjänsten: registrering

Vägarna ligger under `/api/portal` så att portalen skickar dem vidare, men fungerar bara på plattformens adress. Registrering och bekräftelse har samma gräns för försök som inloggningen.

| Metod och väg | Beskrivning |
|---|---|
| `GET /platform` | Plattformens namn, villkorens version och länkar, mallen för firmornas adresser, lösenordets minsta längd, om e-postadressen bekräftas, `prices` (som i `GET /admin/billing`), `sandboxMaxOpenAccounts` och `robotCheckSiteKey`, robotkontrollens publika nyckel eller null när den är av, för förstasidan och registreringen. 404 på andra adresser. |
| `POST /login-help` | `{ "email" }`. Mejlar en engångslänk till adminpanelen för varje firma e-posten administrerar, som gäller i en timme, och adressen till firmans inloggning. Alltid 202, så att svaret aldrig säger vem som har en firma. Samma gräns för försök som inloggningen. |
| `GET /signup/availability?firmId=` | Om det korta namnet går att välja, annars varför inte, med lediga förslag (`suggestions`). |
| `POST /signup` | `{ "firmName", "firmId", "email", "password", "acceptTerms", "currency", "robotCheck" }`. `robotCheck` är svaret från robotkontrollen, som kontrolleras först: 422 med fältet `robotCheck` utan eller med fel svar, 503 när kontrollen inte svarar (ADR 0045). `currency` är kontonas valuta, en av `Signup:Currencies`, som standard den första. 422 för en annan. Med krav på bekräftelse skickas ett mejl och svaret är 202. Utan krav skapas firman direkt och svaret är 200 med `adminUrl`. 422 med orsaken för fel i fälten, 409 för ett upptaget namn. En ny registrering med samma e-postadress ersätter den gamla, som går ut. 429 när adressen redan fått `Signup:MaxEmailsPerDay` bekräftelsemejl det senaste dygnet. Bekräftelsemejlet säger inget som den som registrerade valt, som firmans namn. |
| `POST /signup/verify` | `{ "token" }`. Skapar firman, köar välkomstmejlet med adressen till adminpanelen och de första stegen, och svarar med `adminUrl`, en engångslänk som loggar in administratören på firmans adress och öppnar guiden. 401 för en okänd, använd eller utgången länk, 409 om namnet hann tas. |

På firmans adress:

| Metod och väg | Roll | Beskrivning |
|---|---|---|
| `POST /admin/welcome` | Alla | `{ "token" }` från `adminUrl`. Loggar in administratören. Länken gäller en gång i 10 minuter. Sidan `/admin/welcome` öppnar sedan `next`, guiden efter registreringen, annars adminpanelen. |
| `POST /admin/invites/accept` | Alla | `{ "token", "password" }`. Skapar administratören från en inbjudan och loggar in. |
| `GET /admin/firm` | Admin | Firmans id, namn, status, portalens adress, utseende, server på handelsplattformen, om en API-nyckel finns, webhookens adress och betalningarna. |
| `PUT /admin/firm/branding` | Admin | `{ "colors" }`, portalens färger som `#rrggbb`, också texten på accentfärgen (`accent-foreground`). |
| `PUT /admin/firm/logo` | Admin | Laddar upp loggan i formulärfältet `file`: PNG, JPEG, WebP eller SVG upp till 1 MB, känd på sitt innehåll. 413 för en större och 422 för något annat, också en SVG med skript, händelser, inbäddade sidor eller entiteter (ADR 0023). |
| `DELETE /admin/firm/logo` | Admin | Tar bort loggan, så att portalen visar firmans namn. |
| `POST /admin/firm/api-key` | Admin | En ny nyckel till firmans API. Visas bara nu, och den gamla slutar fungera. |
| `PUT /admin/firm/webhook` | Admin | `{ "url" }`, en https-adress på internet eller tom för ingen. 422 för en IP-adress som inte är publik eller ett namn som `localhost` eller `*.internal`, och namnet kontrolleras igen vid varje anrop (ADR 0044). Första gången skapas en hemlighet, som visas i svaret. |
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
| `/` | Plattformens | Förstasidan: vad en firma får, hur det går till från sandlådan till live, vad sandlådan och live kostar, och knapparna "Start free sandbox" och "Log in to your firm". |
| `/login` | Plattformens | För en administratör som inte minns firmans adress: e-posten får en länk till adminpanelen för varje firma den administrerar. |
| `/signup` | Plattformens | Registrering med kontroll av det korta namnet medan det skrivs, förhandsvisning av adressen och robotkontrollen, som ber om ett nytt svar efter varje försök. |
| `/verify?token=` | Plattformens | Bekräftar e-postadressen och skickar vidare till firmans adminpanel. |
| `/admin/welcome?token=` | Firmans | Loggar in administratören efter registreringen. |
| `/admin/invite?token=` | Firmans | En inbjuden administratör väljer lösenord. |
| `/admin` | Firmans | I sandlådan stegen till live: servern, challenges, priser, betalning, utseende, att prova som trader, vår granskning, KYC och att gå live. Se [specen för portalen](portal.md). |
| `/admin/challenges` | Firmans | Firmans challenges. Ny challenge från mall, kopia av en annan och ändring av befintliga. |
| `/admin/team` | Firmans | Administratörer, inbjudningar och borttagning. |
| `/admin/design` | Firmans | Logga, tema och färger med förhandsvisning. |
| `/admin/integrations` | Firmans | API-nyckel och webhook. |
| `/admin/trading`, `/admin/notifications` | Firmans | Handelsvillkoren och notiserna. Se [specen för portalen](portal.md). |

Alla sidor på en firma i sandlådan visar en rad om att det är en testmiljö. Medan servern skapas visar adminpanelen det, och knappen för att starta en challenge väntar.

## Innan vi har godkänt firman

Det mesta fungerar i sandlådan, men det som når människor utanför firman i vårt namn väntar på vår granskning ([ADR 0043](../adr/0043-sparrar-innan-vi-godkant-firman.md)). Godkänd betyder att granskningen har status `Approved`, eller att firman är live. Regeln finns i `Prop.Api/Review/FirmApproval.cs`. Firman når bara sina egna administratörer:

| Spärr | Hur |
|---|---|
| Mejl bara till administratörerna | Alla mejl om firman i mejlkön, och firmans mejl till traderns kort och köparens inbjudan, går bara till firmans administratörer. Ett mejl till någon annan sparas i `email_outbox` med `withheld_at` och skickas aldrig. `GET /admin/accounts/{id}/email-trader` säger varför i `withheld`, `POST` på samma väg svarar 409, och butikens "Email me the link" svarar 409. |
| Bara teamet köper | En order från någon annan än en administratör nekas med 403. `GET /shop` har `teamOnly`, och butiken säger det. Firmans egen betalsida säljer till andra först när firman är live, också efter vårt godkännande. |
| Bara teamet loggar in som traders | Inloggning, inbjudan, bekräftelse med inbjudan, länk för nytt lösenord och lösenord på orderns sida nekas med 403 för andra, utan att länken förbrukas. En traders session kontrolleras vid varje anrop. Inbjudningslänkar och terminallänkar, i adminpanelen och firmans API, nekas med 409 för andras konton. |
| Få inbjudningar till teamet | Högst `Sandbox:MaxAdminInvites` inbjudningar på 30 dagar (standard 10), också de som tagits tillbaka eller skickats igen. Fler svarar 409. Inbjudningar tas därför inte bort när de tas tillbaka eller ersätts, utan går ut, och städas bort en månad efter att de gick ut. |
| Ingen egen domän | `PUT /admin/domain` svarar 409, och `GET /admin/domain` har `waitsForApproval`. En domän som lades till innan regeln fanns blir inte aktiv, och DNS frågas inte, förrän vi har godkänt firman. |

Adminpanelen säger det i förväg: panelen som startar en challenge (där rutan för att mejla tradern bara går att kryssa i för en administratörs adress), traderns kort, Write to a trader, Notifications och Your domain.

## Skydd mot missbruk

Registreringen och sandlådan är gratis, så de skyddas också mot att användas i stor skala ([ADR 0045](../adr/0045-skydd-mot-missbruk-av-gratis-sandlador.md)):

- **Robotkontroll.** Registreringssidan visar Cloudflare Turnstile när `RobotCheck` har nycklar, och tjänsten kontrollerar svaret innan något annat (`Prop.Api/Signup/RobotCheck.cs`).
- **Få bekräftelsemejl.** En adress får högst `Signup:MaxEmailsPerDay` bekräftelsemejl per dygn, och mejlet säger inget som avsändaren valt.
- **Vilande sandlåda.** Varje gång en administratör använder adminpanelen sparas det i `firms.active_at`, högst en gång i timmen per firma. En firma som inte är live och vars adminpanel inte använts på `Sandbox:IdleDays` dagar mejlas en vecka innan, och stängs sedan (`sandbox_closed_at`): dess öppna testkonton avslutas, och inga nya startar, med orsaken i svaret. Nästa gång en administratör använder adminpanelen öppnas den igen. Anrop till firmans API räknas inte. Se `Prop.Api/Firms/IdleSandboxes.cs`.

## Tabeller

Propfirm-tjänsten:

| Tabell | Innehåll |
|---|---|
| `firms` | Firman: namn, status, om den är konfigurerad, hash av API-nyckeln, kontovalutan den valde (`account_currency`), server, krypterad nyckel, grupp och valuta på handelsplattformen, webhookens adress och krypterade hemlighet, portalens adress, logga, färger, villkorens version och när de godkändes. |
| `firm_hosts` | Värdnamnen portalen nås på. Ett värdnamn hör till en firma. |
| `firm_logos` | Loggan firman laddat upp: innehållstypen, filen och dess SHA-256, som är en del av adressen portalen visar den på. |
| `firm_signups` | Registreringar som väntar på bekräftelse: namnen, e-post, hash av lösenordet och av länkens token, kontovalutan, när den går ut och när den användes. |
| `admin_invites` | Inbjudningar till administratörer: hash av token, e-post, när den går ut och när den användes. |
| `admin_login_links` | Engångslänkar som loggar in en administratör: hash av token, administratören och när den går ut. |

Handelstjänsten:

| Tabell | Innehåll |
|---|---|
| `tenants` | Firman: server, namn, SHA-256 av nyckeln till admin-API:t, partnern som skapade den, om den listas och var dess traders loggar in (`login_url`). |
| `tenant_groups` | Vilken firma varje grupp hör till. |

## Konfiguration

Propfirm-tjänsten:

| Sektion | Innehåll |
|---|---|
| `Platform:Name` | Plattformens namn på registreringen och i mejlen. Ett arbetsnamn tills produkten har ett namn. |
| `Platform:Url` | Plattformens adress, där registreringen ligger. Slutar med `/`. |
| `Platform:FirmPortalUrl` | Mall för en ny firmas portal, med `{firm}` för det korta namnet, till exempel `https://{firm}.example.com/`. |
| `Signup:RequireEmailVerification` | Om e-postadressen ska bekräftas innan firman skapas. Standard ja, avstängt i utveckling. |
| `Signup:TermsVersion`, `Signup:TermsUrl`, `Signup:DpaUrl` | Villkorens version, som sparas med firman men inte visas, och var villkoren och personuppgiftsbiträdesavtalet finns. Utanför utveckling startar tjänsten inte utan dem, så att registreringen alltid länkar till dem. |
| `Signup:Currencies` | Kontovalutorna en firma kan välja, den första som standard. Handelsplattformen måste erbjuda dem (`Tenancy:Currencies`). Standard USD, EUR och GBP. |
| `Signup:ReservedFirmIds` | Fler reserverade korta namn. |
| `Sandbox:MaxOpenAccounts` | Högst så många öppna challenge-konton i sandlådan. Standard 10. |
| `Sandbox:MaxAdminInvites` | Högst så många inbjudningar till teamet på 30 dagar innan vi har godkänt firman. Standard 10. |
| `Sandbox:IdleDays` | En sandlåda vars adminpanel inte använts på så många dagar stängs tills någon kommer tillbaka. Standard 60, minst 14. |
| `Signup:MaxEmailsPerDay` | Högst så många bekräftelsemejl till en adress per dygn. Standard 3. |
| `RobotCheck:SiteKey`, `RobotCheck:SecretKey` | Nycklarna till Cloudflare Turnstile. Båda eller ingen, och utanför utveckling startar tjänsten inte utan dem. Cloudflares testnycklar fungerar på testservern. `RobotCheck:VerifyUrl` är var svaren kontrolleras. |
| `Email` | `From`, `FromName` och `Smtp` (`Host`, `Port`, `UserName`, `Password` och `Security`, som är `None`, `StartTls` eller `SslOnConnect`). Lokalt Mailpit på port 1025, utan kryptering. |
| `Secrets:Key` | 32 slumpade byte i base64, som krypterar hemligheterna i databasen. Får aldrig ligga i en fil utanför utveckling. |
| `TradingPlatform:PartnerApiKey` | Propfirm-plattformens nyckel till partner-API:t. |

Handelstjänsten:

| Sektion | Innehåll |
|---|---|
| `Partners` | Partnerna: `Id`, `Name` och `ApiKeySha256`. |
| `Tenancy:NewTenantGroups` | Grupperna i `Trading:Groups` som en ny firma får en kopia av. |
| `Tenancy:Currencies` | Kontovalutorna en ny firma kan få i stället för mallgruppernas. Var och en behöver ett instrument mot USD, eftersom motorn räknar om genom USD. Standard USD, EUR och GBP. |

## Begränsningar

- Egen domän läggs till under Your domain i adminpanelen, när vi har godkänt firman, och blir aktiv när DNS-posterna finns (ADR 0039). Certifikatet görs av vår proxy, som inte är uppsatt än.
- Firman kan inte byta namn, kort namn eller kontovaluta.
- Villkoren och personuppgiftsbiträdesavtalet skriver vi själva. I utveckling finns inga, och registreringen visar dem utan länk.
- Notiserna till traders har en fast text på engelska. Firman väljer bara vilka som skickas.
- En ny firma får en tvåstegs-challenge. Fler kommer från fyra mallar (se [specen för regelmotorn](regelmotor.md)), och challenges kan inte tas bort.

## Tester

- `trading/tests/Trading.Engine.Tests/GroupTests`: grupper som skapas medan motorn kör (se [specen för handelsmotorn](handelsmotor.md)).
- `trading/tests/Trading.Service.Tests/PartnerApiTests` och `PostgresIdentityTests`: partner-API:t och firmorna i databasen (se [specen för handelstjänsten](handelstjanst.md)).
- `prop/tests/Prop.Api.Tests/SignupTests` och `AdminSettingsTests`: registreringen, servern i bakgrunden, sandlådan och adminpanelens inställningar (se [specen för propfirm-tjänsten](propfirm-tjanst.md)). Den låtsade handelsplattformen har ett partner-API och tar bara emot en servers senaste nyckel, och ett låtsat mejlsystem tar emot mejlen.
- `prop/tests/Prop.Api.Tests/PasswordResetTests`: plattformens mejl med en inloggningslänk för varje firma, och samma svar för en okänd e-post.
- `prop/tests/Prop.Api.Tests/BeforeApprovalTests`: spärrarna innan vi godkänt firman: mejl bara till administratörerna, också i kön, och till alla efter godkännandet; bara administratörer loggar in som traders, med inbjudningslänkar och terminallänkar bara för deras konton, och en gammal inbjudan som inte förbrukas; gränsen för inbjudningar, också med en som tagits tillbaka; och egen domän först efter godkännandet, också för en domän som lades till innan.
- `prop/tests/Prop.Api.Tests/OrderFlowTests`: också att bara teamet köper innan vi godkänt firman, och att firmans egen betalsida säljer bara till teamet innan firman är live.
- `prop/tests/Prop.Api.Tests/AbuseLimitsTests`: robotkontrollen före registreringen, och ingen kontroll utan nycklar; tre bekräftelsemejl per adress och dygn utan text som avsändaren valt; webhooks som inte får peka in i vårt nät; taken för supportärenden; och gränserna per minut med rubriken i svaret.
- `prop/tests/Prop.Api.Tests/IdleSandboxTests`: en sandlåda som ingen använder varnas, stängs med sina testkonton och öppnas igen av adminpanelen, och att adminpanelen håller den öppen.
- `prop/tests/Prop.Api.Tests/SignupTests`: också lediga förslag för ett namn som är taget eller reserverat, kontovalutan hela vägen till handelsplattformen och den första challengen, och välkomstmejlet.
- `prop/portal/e2e/signup.spec.ts`: hela vägen i en webbläsare, från förstasidan och registreringen på `app.localhost` till en challenge i den nya firmans sandlåda, och plattformens inloggning som tar emot en e-post (se [specen för portalen](portal.md)).
