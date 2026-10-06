# Spec: propfirm-tjänsten

- Fas: 4c, portalens del i 4d, utbetalningar i 5, firmor i databasen, registrering och sandlåda i 6, platser och betalning i 7, köp i portalen i 8, granskning och avstängning i 9a, handelshistorik för traderns översikt efter 9a, e-post genom en utkorg, notiser, glömt lösenord, utbetalningsmetoder, handelsvillkor, moms och fakturor, mejl i firmans utseende, lösenordet efter köpet, kontona i terminalen, rabattkoder, firmans kontroller av traders, egen domän och konsistensregel efter genomgången som ny firma, våra mejl som HTML och kvittot efter ett köp efter genomgången av UI och UX, delstängningar i handelshistoriken och regelmotorn efter jämförelsen med konkurrenterna
- Status: Implementerad i `prop/src/Prop.Api`
- Datum: 2026-10-06

## Syfte

Tjänsten driver firmornas challenges. Den har firmans API och portalens API, kör regelmotorn (se [specen för regelmotorn](regelmotor.md)) för varje challenge-konto och kopplar den till handelsplattformen genom dess publika admin-API (ADR 0012). Besluten om hur det hålls korrekt finns i [ADR 0013](../adr/0013-journal-och-utkorg-i-propfirm-tjansten.md), och om handelshistoriken bakom traderns översikt i [ADR 0022](../adr/0022-handelshistorik-for-traderns-oversikt.md). Portalen och dess API beskrivs i [specen för portalen](portal.md), hur firmor registrerar sig själva i [specen för registrering och sandlåda](registrering.md), köp av challenges i portalen i [specen för köp i portalen](kop.md), vad firman betalar oss i [specen för platser och betalning](platser-och-betalning.md) och vår granskning av firmor och avstängning i [specen för granskning och avstängning](granskning.md).

## Delar

| Del | Ansvar |
|---|---|
| `FirmCatalog`, `FirmApiKeyFilter` | Firmorna i minnet: id, namn, status, hash av nyckeln för firmans API, server och nyckel på handelsplattformen, webhook, portal och avstängning. Den som ändrar en firma lägger in den nya versionen (ADR 0017). |
| `FirmStore`, `SecretProtector` | Firmorna i databasen. Handelsplattformens nyckel, webhook-hemligheten och firmornas dokument krypteras med AES-GCM och `Secrets:Key`. |
| `FirmSeeder` | Sparar de konfigurerade firmorna i databasen vid start och laddar alla firmor. Stoppar starten om konfigurationen är fel, eller om en konfigurerad firma har samma id som en som registrerat sig. |
| `FirmProvisioner` | Skapar servern på handelsplattformen för varje firma som registrerat sig, och flyttar firman till sandlådan med en första challenge. Försöker igen tills det lyckas. Berättar också för handelsplattformen var firmans traders loggar in (portalens `/terminal`) och att servern ska listas i terminalen när firman är live (ADR 0027). |
| `FirmLoops` | Startar bakgrundsjobben per firma när firman har en server, även för firmor som blir klara medan tjänsten kör. |
| `SignupService` | Registrering, bekräftelse av e-postadressen och skapandet av firman, adressen och administratören. |
| `FirmAdmins` | Administratörernas engångslänkar, inbjudningar, borttagning och när de senast loggade in. `PortalAuth.SignInAsync`, som alla sätt att logga in som administratör går genom, sparar tiden. |
| `IEmailSender`, `SmtpEmailSender` | E-post från plattformen med SMTP, som ren text och, när mejlet har det, också HTML, och med en svarsadress. Lokalt fångas allt av Mailpit. |
| `PlatformEmails`, `TraderEmails`, `EmailLayout` | Mejlen till firmor och vår personal, och mejlen till traders i firmans namn och utseende: loggan, accentfärgen på knappen, svar till firmans supportadress och en sidfot med firmans namn, supportadress och varför tradern får mejlet (ADR 0033). Alla mejl har samma text som ren text och HTML. `EmailLayout` skriver båda från samma delar (stycken, rubriker, en knapp, steg, tabeller och citat), med tabeller och inline-stilar som mejlprogram visar lika, utan webbtypsnitt och SVG. Våra egna mejl skrivs som ren text, och `PlatformEmails.Create` gör HTML av texten i Kronants utseende: ordmärket "Kronant" i en serif med produktens namn i små versaler bredvid, grafit på vitt och varmt benvitt, tunna linjer, en mässingsfärgad knapp med mörk text för huvudlänken, numrerade rader som steg och rader med "namn: värde" som en tabell, och en sidfot med plattformens namn och varför mottagaren får mejlet och, när det går, var det ändras. Ett nytt mejl som skapas med `Create` får mallen utan mer arbete. |
| `EmailOutbox`, `EmailWorker` | Mejl, med HTML och svarsadress, som köas i samma transaktion som ändringen de berättar om och skickas i bakgrunden, äldst först, med nya försök efter 30 sekunder och dubbel väntan upp till 4 timmar, i 12 försök (ADR 0025). |
| `Notifications`, `InactivityReminderWorker` | Notiserna till firmans administratörer och traders när regelmotorn fattar ett beslut eller en order betalas, och påminnelsen om att öppna en affär innan challengen tar slut. Varje slag kan stängas av av firman (ADR 0025). `Notifications.Preview` bygger varje slag med samma kod och påhittade uppgifter, för "Show the email" i adminpanelen, utan att köa något. |
| `PasswordResets`, `PasswordResetEndpoints` | Länkarna för att välja nytt lösenord för traders, administratörer och vår personal, och kontrollen av länkar och inbjudningar när de öppnas (ADR 0028). |
| `PayoutMethods` | Hur traders vill få betalt, krypterat med `SecretProtector` (ADR 0026). |
| `TradingConditionsEndpoints` | Firmans handelsvillkor på handelsplattformen: instrumenten, hävstången, påslaget och provisionen (ADR 0027). |
| `AccountActions` | Det som går att göra med ett konto, gemensamt för firmans API och portalen. |
| `PayoutActions`, `PayoutQueries` | Utbetalningar: begäran, firmans beslut och listor, gemensamt för firmans API och portalen. En utbetalning som tradern begär i portalen får en kopia av traderns utbetalningsmetod. |
| `PortalEndpoints`, `PortalAuth`, `PortalFirmFilter` | Portalens API, sessioner och att firman känns igen på värdnamnet. |
| `PortalUsers`, `PortalSeeder` | Traders och administratörer i portalen, inbjudningar, och de administratörer och traders som konfigurerats för utveckling. |
| `ChallengeCatalog` | Challenges per firma. Kontrolleras av regelmotorn och mot kända tidszoner. |
| `ChallengeService` | Kör regelmotorn med kontot låst. Steget, tillståndet, kommandona och webhooks sparas i samma transaktion. När ett konto öppnas för en fas köas också hur terminalen ska visa det: namnet som i portalen, vinstmålet, handelsdagens tidszon och adressen till kontot i portalen (ADR 0035). Efter varje steg köas reglerna som terminalen visar och varnar för, när de ändrats (ADR 0052). |
| `TerminalRules` | Reglerna för terminalen ur regelmotorns tillstånd (ADR 0052): handelsdagar som krävs och som räknats, när steget måste vara klart och när en ny position senast måste öppnas, som början på handelsdagen i UTC och bara medan steget handlas, och för ett finansierat konto konsekvensregeln med bästa dagens andel av vinsten. |
| `TradingAccountDescriber` | En gång vid start: berättar för handelsplattformen hur öppna konton som öppnades innan den kunde få veta det ska visas, och reglerna för konton som inte fått dem, en gång per konto. |
| `ITradingPlatform`, `TradingPlatformClient` | Handelsplattformens admin-API v1, även kontot värderat just nu för portalen och firmans handelsvillkor. Fler plattformar kan få egna adaptrar. |
| `ITradingPartner`, `TradingPartnerClient` | Handelsplattformens partner-API v1, som skapar servrar åt firmor som registrerar sig (ADR 0016) och sätter om de listas och var deras traders loggar in. |
| `TradingEventConsumer` | Läser firmans händelseström och gör om händelser till fakta. Löpnumret sparas i samma transaktion som besluten. |
| `TradingHistoryRecorder` | Läser firmans händelseström med en egen läsposition och sparar positioner, saldoändringar och golvens nivåer för kontona tjänsten har öppnat (ADR 0022). Varje händelse sparas en gång per löpnummer, så historiken byggs om genom att läsa strömmen igen. |
| `TradingHistoryQueries`, `AccountPerformance` | Läser historiken och fasernas start och resultat, och räknar resultatet i dag, statistiken och resultatet per dag med decimaltal. |
| `AdminPanelEndpoints`, `AdminFigures` | Adminpanelens egna vyer (ADR 0023): översikten med konton per grupp, utbetalningar, försäljning, andelen som klarar, veckor och händelser, sökningen bland kontona, kontots trader med firmans kontroller, mejlet till tradern och förhandsvisningen av det, utbetalningskön med om tradern är kontrollerad och varje challenges siffror. Siffrorna räknas med SQL när de läses. |
| `AccountDetailsBuilder`, `HistoryActions` | Kontona som portalen visar dem, för ett konto eller alla en traders på en gång med en fråga per sorts data, med positionerna ett brott stängde och saldot efteråt, och för en underkänd challenge ett nytt försök med firmans bästa kod för nya försök. Och kontots historik per fas: grafens data, affärerna sida för sida och som CSV-fil. |
| `TradingCommandWorker` | Kör kommandona mot handelsplattformen i ordning per firma och försöker igen vid avbrott. En firmas kommandon väntar tills den har en server. Ett nekat uttag rapporteras till regelmotorn i samma transaktion, så att utbetalningen blir `Failed`. |
| `TradingDayScheduler`, `TradingStreamProgress` | Startar handelsdagen för varje öppet konto när dagen börjar, och kommer ikapp efter en omstart. En dag som avslutar en challenge på tid väntar tills firmans händelseström är läst förbi dagens början. |
| `WebhookWorker`, `WebhookOutbox` | Webhooks till firman köas i samma transaktion som ändringen bakom dem, och skickas signerade tills de tas emot. De skickas bara till publika adresser på internet, genom `PublicAddresses`, utan att följa omdirigeringar (ADR 0044). |
| `WebhookDeliveries`, `WebhookEvents` | Alla händelser webhooks berättar om, de senaste leveranserna för adminpanelen och testhändelsen `webhook.test`. |
| `OrderService`, `OrderStore` | Köp i portalen: ordern med priset, betalningen som startar kontot i samma transaktion, återbetalningar, bestridanden och inbjudan till köparen (ADR 0019). |
| `PriceCatalog` | Challengernas priser i portalen, skilda från challengerna. |
| `DiscountStore`, `DiscountRules`, `DiscountEndpoints` | Firmans rabattkoder och hur ofta de använts, priset med en kod, och adminpanelens koder (ADR 0036). |
| `TraderChecks` | Firmans egna kontroller av en trader, ID och adress, som firman bockar för i adminpanelen (ADR 0037). |
| `CustomDomainStore`, `DomainVerifier`, `DomainVerificationWorker`, `IDnsLookup`, `AdminDomainEndpoints` | Firmans egen domän: TXT-posten som visar att den är firmans och CNAME-posten som pekar hit, uppslagningen med DNS över HTTPS var femte minut och med "Check now", och frågan från vår proxy om en adress får ett certifikat (ADR 0039). |
| `StripeClient`, `StripeSignature` | Stripe Checkout med firmans egen nyckel, och kontrollen av Stripes webhooks. |
| `ShopEndpoints`, `PaymentEndpoints`, `OrderActions` | Butiken och köparens order i portalen, adminpanelens ordrar, priser och betalningar, firmans API för ordrar och priser och Stripes webhook. |
| `ChallengeSeeder` | Skapar firmans konfigurerade challenges vid start. |
| `SlotService` | Firmans platser: hur många challenges den kan ha öppna och hur många som är tagna, också av ordrar som väntar på betalning. Starter och ordrar låser firmans platser i sin transaktion. |
| `BillingService`, `BillingStore`, `BillingRules` | Vad firman betalar oss: go-live, månaden i förskott, fler och färre platser, nytt kort, nekade betalningar, paus av firmans challenges och priserna (ADR 0020). |
| `IBillingGateway`, `StripeBillingGateway`, `TestBillingGateway` | Betalsidor och dragningar av det sparade kortet hos Stripe med vårt konto, eller testbetalningar i utveckling. |
| `BillingWorker` | Varje minut och när något ändras: betalsidor som gått ut, kort som ska dras, nästa månad, paus och återupptagande, automatisk utökning och varningen om platserna. |
| `BillingEndpoints` | Adminpanelens betalning, firmans API för platserna och Stripes webhook för våra egna betalningar. |
| `ReviewService`, `ReviewStore`, `ReviewEndpoints` | Vår granskning av firman: ansökan, dokumenten, att skicka den med handpenningen, våra beslut och avstängning (ADR 0021). |
| `StaffUsers`, `StaffAuth`, `StaffSeeder`, `OpsHost` | Vår personal, dess session på vår adminvys adress och att vår adminvy bara finns där. |
| `OpsEndpoints`, `OpsFirms`, `StaffNotifier` | Vår adminvys API, firman som personalen ser den och mejlet till personalen när en ansökan kommer. |
| `OpsPanelEndpoints`, `OpsFigures` | Vår adminvy över alla firmor (ADR 0024): översikten med det som väntar på oss, firmorna med sökning och grupper, och vad firmorna betalar. Siffrorna räknas med SQL när de läses. |

## Flöde

```
firmans system -> POST /accounts -> regelmotorn: öppna konto för fas 1
-> kommando: öppna konto -> handelsplattformen -> händelsen AccountCreated i strömmen
-> regelmotorn: kontot är öppet -> kommandon: sätt golv för total och daglig förlust
tradern handlar -> PositionOpened och PositionClosed i strömmen -> handelsdagar och saldo
-> fasen klar -> kommandon: stäng kontot, öppna nästa -> webhook account.passed
midnatt i challengens tidszon -> regelmotorn: ny handelsdag -> kommando: lägg om det dagliga golvet
golvet bryts -> EquityFloorBreached i strömmen -> underkänd, med händelsen som bevis -> webhook account.breached
ingen ny position på 30 dagar, eller fasens tidsgräns passerad -> ny handelsdag -> underkänd, kontot stängs -> webhook account.expired
firmans månad börjar obetald -> challengen pausas -> kommando: pausa kontot -> webhook account.paused
tradern begär utbetalning -> kommando: ta ut vinsten -> BalanceAdjusted i strömmen -> väntar på firman -> webhook payout.requested
firman godkänner -> webhook payout.approved -> firman betalar själv och markerar som betald -> webhook payout.paid
varje beslut ovan -> notiserna köas i samma transaktion -> mejl till tradern eller firmans administratörer
```

## Från handelsplattformen till regelmotorn

| Händelse | Blir |
|---|---|
| `AccountCreated` | `AccountOpened` för handelsdagen när kontot öppnades. |
| `PositionOpened` | `PositionOpened` för handelsdagen när positionen öppnades. |
| `PositionClosed` | `AccountUpdated` med saldot efter och antalet öppna positioner. |
| `PositionPartiallyClosed` | `AccountUpdated` med saldot efter. Positionen räknas som öppen tills den sista delen stängs (ADR 0051). |
| `EquityFloorBreached` | `FloorBreached`. Hela händelsen sparas som bevis i steget. |
| `AccountDisabled` | `AccountDisabled`. |
| `EquityFloorSet` | Bara golvets nivå, för visning. |
| `BalanceAdjusted` | `BalanceAdjusted` med operationens id, beloppet och saldot efteråt. Hela händelsen sparas i steget. |

Händelser om konton som tjänsten inte har öppnat åt firman, och andra händelser, flyttar bara läspositionen framåt.

## Handelshistoriken

`TradingHistoryRecorder` läser samma ström som regelmotorn men med en egen läsposition i `trading_history_cursors`. En firma som saknar läsposition läser strömmen från början, så historiken fylls i för konton som handlade innan den fanns. Att ta bort historiken och läspositionen bygger om den. Den kan ligga någon sekund efter regelmotorn.

| Händelse | Sparas som |
|---|---|
| `AccountCreated` | Saldoändringen `Created` med startsaldot. |
| `PositionOpened` | En position med symbol, sida, volym, öppningspris, tid och provision, och saldoändringen `Opened` med provisionen. |
| `PositionClosed` | Positionens stängningspris, tid, vinst före provision, provision och orsak, och saldoändringen `Closed` med vinsten efter provision. Stängningen har positionens egna fält, så positionen blir hel även om öppningen aldrig lästes. Volymen den stängde sparas för sig. |
| `PositionPartiallyClosed` | En rad i `trading_partial_closes` med delens volym, pris, vinst och provision, och saldoändringen `Closed` med delens vinst efter provision. Den stängda affären räknar in delarna: volymen, vinsten och provisionen läggs till, och stängningspriset är deras genomsnitt viktat med volymen (ADR 0051). |
| `BalanceAdjusted` | Saldoändringen `Adjusted`, till exempel en utbetalnings uttag. Den räknas inte som resultat. |
| `EquityFloorSet` | Golvets nivå och när den sattes, till exempel det dagliga golvet vid varje handelsdag. |

När regelmotorn startar eller klarar en fas sparas tiden, saldot och handelsdagarna på fasens rad i `trading_accounts`, och beslutet som avslutade challengen (brott, tiden slut eller annullering) på kontot. Det sker i samma transaktion som steget. Konton som fanns innan fick samma uppgifter från sina steg när databasen uppdaterades.

Tjänsten räknar, och portalen bara visar:

- **Resultatet i fasen** är equity, eller saldot när kontot inte värderas, minus startsaldot.
- **Resultatet i dag** är equity nu minus saldot när handelsdagen började, utan insättningar och uttag sedan dess. Ett konto som öppnades under dagen började den på startsaldot.
- **Vinstmålet** är hur långt saldot har kommit från startsaldot mot målet, från 0 till 100.
- **Statistiken** räknas på stängda positioner efter provision för öppning och stängning: antal, vinnare och förlorare (resultat över och under noll), andel vinnare, snitt för vinst och förlust, bästa och sämsta affär, profit factor (vinsternas summa delat med förlusternas, utan värde när det inte finns förluster), lots, resultat och provision.
- **Dag för dag** visar varje handelsdag med en stängd position eller som regelmotorn räknat som handelsdag. En position hör till handelsdagen den stängdes, enligt challengens handelsdag.
- **Versionen** av kontots historik är det senaste löpnumret i historiken och antalet steg. Den ändras när något nytt har hänt, så portalen hämtar historiken igen bara då.

## Handelsdagar

En handelsdag börjar vid challengens klockslag i dess tidszon och har namn efter det lokala datum den börjar på. En dag som avslutar en challenge för att tiden tagit slut startas först när firmans händelseström är läst till slutet med en läsning som började minst 30 sekunder efter dagens början. Då räknas en affär som gjordes i sista stund, även när händelsen kommer sent, till exempel efter en omstart. En dag som börjar 17:00 på en söndag är alltså söndagens. Övergången mellan sommartid och vintertid följs. Ett klockslag i timmen som hoppas över flyttas till första tiden som finns, och ett klockslag i timmen som upprepas räknas första gången.

## Konton på handelsplattformen

- Id är `{firma}-{nummer}-{fas}`, till exempel `demo-firm-1001-1`. Numret börjar på 1001 för varje firma, och samma id ges vid ett nytt försök.
- Kontot får sitt namn, vinstmål, tidszon och adress i portalen med `PUT /accounts/{id}/details` direkt efter att det öppnats, så att terminalen visar det som portalen (ADR 0035). Firmans logga skickas med listningen.
- Kontot får sina regler med `PUT /accounts/{id}/rules` när det öppnats och igen när de ändras, till exempel när en handelsdag räknas, en ny position flyttar sista dagen eller bästa dagens andel ändras (ADR 0052). Det som senast berättades sparas i `described_rules`, och kommandot köas bara när reglerna skiljer sig från det. Ett avslutat konto får inga nya.
- Traderns användare på handelsplattformen skapas när det första kontot öppnas, med ett slumpat lösenord som aldrig visas. Tradern loggar in med en länk från `POST /accounts/{id}/login-link`, eller med knappen "Open terminal" i portalen. Terminalen skickar firmans traders till portalens `/terminal`, som öppnar den igen med en engångslänk, också när sessionen i terminalen har gått ut.

## Tabeller

| Tabell | Innehåll |
|---|---|
| `challenge_definitions` | Firmans challenges. |
| `traders` | Firmans traders, deras användare på handelsplattformen, hash av lösenordet till portalen och när det valdes, namnet och landet från köpet och när e-posten bekräftades. E-postadressen är unik inom firman. |
| `firm_admins` | Firmans administratörer i portalen, när lösenordet valdes och när de senast loggade in (`last_login_at`, tomt för den som inte har loggat in sedan det började sparas). E-postadressen är unik inom firman. |
| `password_resets` | Länkar för att välja nytt lösenord: hash av token, slag (`trader`, `admin` eller `staff`), personen, när länken går ut och när den användes. |
| `email_outbox` | Mejl som väntar på att skickas, med texten, HTML och svarsadressen, försök, senaste fel, när de skickades eller gavs upp, och en nyckel som hindrar att samma påminnelse köas två gånger. `withheld_at` är satt för ett mejl som aldrig skickas, eftersom vi inte har godkänt firman och mottagaren inte är en administratör (ADR 0043). |
| `trader_payout_methods` | Traderns utbetalningsmetod, krypterad. |
| `portal_invites` | Inbjudningar till portalen: hash av token, trader, när den går ut och när den användes. |
| `data_protection_keys` | Nycklarna som skyddar portalens sessioner. |
| `challenge_accounts` | Challenge-konton med regelmotorns tillstånd, status, fas, handelsdag, om challengen är pausad, firmans referens, beslutet som avslutade challengen, om kontot startades i sandlådan vilket konto på handelsplattformen som senast fick sitt namn (`described_account_id`) och reglerna det senast fick (`described_rules`). |
| `challenge_steps` | Varje indata och beslut i ordning, med handelsplattformens händelse som bevis. |
| `trading_accounts` | Konton på handelsplattformen per fas, med saldo, öppna positioner och golvens nivåer som plattformen senast rapporterade, och när fasen startade och klarades, med saldot och handelsdagarna. |
| `trading_cursors` | Hur långt varje firmas händelseström är läst. |
| `trading_history_cursors` | Hur långt handelshistoriken har läst varje firmas händelseström. |
| `trading_positions`, `trading_balance_changes`, `trading_floor_levels` | Handelshistoriken för kontona tjänsten har öppnat: positioner från öppning till stängning, med volymen den sista stängningen stängde (`close_volume`), varje saldoändring med saldot efter, och golvens nivåer över tid. |
| `trading_partial_closes` | Delar av positioner som stängts före resten, en rad per händelse (ADR 0051). En stängd affär räknar in sina delar. |
| `trading_commands` | Kommandon till handelsplattformen, i ordning per firma. |
| `webhook_deliveries` | Webhooks till firman, med försök och svar. |
| `payouts` | Utbetalningar med status, vinst, vinstandel, belopp, tider, orsak, firmans referens, om vinsten lades tillbaka vid ett nej och traderns utbetalningsmetod när den begärdes, krypterad. Regelmotorns steg är revisionsloggen, tabellen är för att hitta utbetalningar. |
| `trader_checks` | Firmans bockade kontroller av en trader, `identity` och `address`, med när och av vilken administratör. |
| `firm_counters` | Nästa kontonummer per firma. |
| `firm_domains` | Firmans egen domän med token för TXT-posten, status (`Pending` eller `Active`), när den senast slogs upp och vad som saknades. |
| `firms`, `firm_hosts`, `firm_logos` | Firmorna, värdnamnen deras portaler nås på, också en egen domän när den är aktiv, och loggorna de har laddat upp. `firms` har också vilka notiser firman stängt av (`email_settings`) och vad handelsplattformen senast fick veta om var traderna loggar in och om servern listas (`trading_listing`). Se [specen för registrering och sandlåda](registrering.md). |
| `firm_signups`, `admin_invites`, `admin_login_links` | Registreringar som väntar på bekräftelse, inbjudningar till administratörer och engångslänkar som loggar in en administratör. |
| `firm_billing`, `billing_periods`, `billing_charges`, `billing_checkouts`, `billing_events` | Hur firman betalar oss och dess kort, betalda månader med platser, debiteringar, betalsidor och allt som hänt. Se [specen för platser och betalning](platser-och-betalning.md). |
| `firm_reviews`, `firm_documents`, `firm_events`, `firm_review_checks`, `staff_users` | Vår granskning av firmorna, deras dokument, allt som hänt i granskningen och med avstängningen, våra bockade kontroller och vår personal. Se [specen för granskning och avstängning](granskning.md). |
| `firm_identity_settings`, `identity_sessions`, `trader_identity` | Hur firman kontrollerar sina traders, varje kontroll hos Didit eller testkontrollen och varje traders resultat. Se [specen för ID-kontroll](id-kontroll.md). |
| `support_tickets`, `support_ticket_counters`, `support_messages`, `support_attachments` | Supportärenden mellan traders och firman, nästa ärendenummer per firma, meddelandena och filerna, krypterade. Se [specen för supportärenden](support.md). |
| `challenge_prices` | Vad challengerna kostar i portalen och om de säljs där. |
| `discount_codes` | Firmans rabattkoder: procent eller belopp i en valuta, vilka challenges, högst hur många gånger, sista dag, om koden är för nya försök och om den är på. |
| `orders`, `order_events`, `order_counters` | Köp i portalen med status, pris, leverantör, betalning, kontot de startade, om ordern gjordes i sandlådan och rabattkoden med priset före den, allt som hänt varje order och nästa ordernummer per firma. Firmans val av leverantör, krypterade Stripe-nycklar, betalsida och villkor ligger i `firms`. Se [specen för köp i portalen](kop.md). |

## Firmans API

Alla vägar börjar med `/api/firm/v1` och kräver firmans nyckel i headern `X-Api-Key`. Andra firmors data svarar 404. En nyckel gör högst `Limits:FirmApiCallsPerMinute` anrop per minut (standard 600), och fler svarar 429 (ADR 0045). Alla svar 429 har en rubrik som säger att vänta en minut.

| Metod och väg | Beskrivning |
|---|---|
| `PUT /challenges/{challengeId}` | Skapar eller ersätter en challenge. Den ska vara i valutan för firmans konton på handelsplattformen. Konton som redan har startat behåller sina regler. |
| `GET /challenges` | Firmans challenges. |
| `POST /accounts` | Startar en challenge med `{ "email", "challengeId", "reference" }`. Samma `reference` igen ger samma konto. Svarar 201, eller 200 för ett konto som redan finns. 409 med orsaken när ingen plats är ledig, när en firma i sandlådan redan har `Sandbox:MaxOpenAccounts` öppna konton eller när firmans månad är obetald. |
| `GET /slots` | Firmans platser: gräns, tagna, reserverade av ordrar, lediga, om månaden är betald och om de flesta är tagna. Se [specen för platser och betalning](platser-och-betalning.md). |
| `GET /accounts?email=` | Traderns konton. |
| `GET /accounts/{id}` | Status, fas, konto på handelsplattformen, startsaldo och valuta, handelsdagar, vinstmål, saldo, golvens nivåer, om challengen är pausad, och handelsdagen då fasen tar slut på tid (`stageDeadline`) och då challengen tar slut utan en ny position (`inactivityDeadline`). |
| `GET /accounts/{id}/history` | Varje indata och beslut, med bevisen vid brott. |
| `POST /accounts/{id}/approve-funding` | Firman har gjort sina kontroller, och tradern får funded-kontot. 409 innan faserna är klara, och när firman vill ha traderns ID kontrollerat först och det inte är. |
| `POST /accounts/{id}/cancel` | Avbryter med `{ "reason" }` och stänger kontot på handelsplattformen. |
| `POST /accounts/{id}/login-link` | En engångslänk som loggar in tradern i terminalen på fasens konto. |
| `POST /accounts/{id}/invite` | En inbjudningslänk till portalen, för firman att skicka till tradern. Gäller en gång i 7 dagar och ersätter traderns äldre oanvända. |
| `POST /accounts/{id}/payouts` | Begär en utbetalning åt tradern, för firmor vars egen webbplats låter tradern begära. Svarar 201 med utbetalningen, eller 409 med orsaken när den inte kan begäras nu. |
| `GET /accounts/{id}/payouts` | Kontots utbetalningar, nyaste först. |
| `GET /payouts?status=&limit=` | Firmans nyaste utbetalningar, högst 500. `status` kan anges flera gånger, till exempel `status=Pending&status=Approved` för de som väntar på firman. |
| `GET /payouts/{payoutId}` | En utbetalning. Utbetalningarna har `payTo`, traderns utbetalningsmetod när den begärdes i portalen, och tomt för de som firman begärt åt tradern. |
| `POST /payouts/{payoutId}/approve` | Firman har gjort sina kontroller av tradern och skickar pengarna. 409 om utbetalningen inte väntar på godkännande. |
| `POST /payouts/{payoutId}/mark-paid` | Firman har betalat, med en valfri egen `{ "reference" }`. 409 om utbetalningen inte är godkänd. |
| `POST /payouts/{payoutId}/reject` | Nekar en utbetalning som väntar eller är godkänd, med `{ "reason", "returnProfit" }`. Orsaken visas för tradern. Med `returnProfit` läggs vinsten tillbaka på kontot, annars är den förlorad (ADR 0037). 409 när vinsten inte kan läggas tillbaka för att kontot har avslutats. |
| `GET /prices`, `PUT /challenges/{challengeId}/price` | Priserna i portalen, och ett pris med `{ "amount", "currency", "forSale" }`. |
| `GET /orders?status=&limit=`, `GET /orders/{orderId}` | Firmans ordrar från portalen, och en order med allt som hänt den. |
| `POST /orders/{orderId}/mark-paid`, `POST /orders/{orderId}/mark-refunded` | Firmans egen betalsida har fått betalt, och kontot startar, eller har betalat tillbaka. Se [specen för köp i portalen](kop.md). |
| `GET /traders/identity?email=`, `PUT /traders/identity` | Traderns ID-kontroll, och resultatet från firmans egen tjänst. Se [specen för ID-kontroll](id-kontroll.md). |

`AccountResponse` har `nextPayout` för funded-konton: vinsten, vinstandelen, traderns belopp, handelsdagar sedan förra utbetalningen, med en konsistensregel den bästa dagens resultat och regelns andel, och om en utbetalning kan begäras nu, annars varför inte. Se [specen för regelmotorn](regelmotor.md). Utbetalningarna har `profitReturned` och `timeZone`, challengens tidszon.

Tjänsten publicerar OpenAPI på `/openapi/v1.json`. Dokumentet skrivs till `prop/portal/openapi/prop-api.json` när tjänsten byggs, och CI kontrollerar att det är committat.

## Webhooks

| Händelse | När |
|---|---|
| `account.stage_started` | En fas har fått sitt konto. |
| `account.passed` | En fas är klar. |
| `account.funding_awaited` | Alla faser är klara, och firman ska godkänna funded-kontot. |
| `account.breached` | Ett golv bröts. Innehåller orsaken, nivån och equity. |
| `account.cancelled` | Kontot avbröts. |
| `account.expired` | Challengen tog slut på tid: `data.reason` är `TimeLimit` eller `Inactivity`, och `data.day` handelsdagen då den tog slut. |
| `account.paused`, `account.resumed` | Challengen pausades eftersom firmans månad är obetald, eller fortsätter. `data.daysPaused` är dagarna som tidsgränserna flyttades. |
| `payout.requested` | Tradern har begärt en utbetalning och vinsten är uttagen från kontot. Firman ska godkänna. |
| `payout.approved` | Firman godkände utbetalningen. |
| `payout.paid` | Firman markerade utbetalningen som betald. |
| `payout.rejected` | Firman nekade utbetalningen. Innehåller orsaken och om vinsten lades tillbaka (`profitReturned`). |
| `order.paid`, `order.refunded`, `order.disputed` | En order i portalen är betald, återbetald eller bestridd. `data.order` är ordern och `account` kontot den startade. |
| `trader.identity_verified`, `trader.identity_declined` | Vår inbyggda ID-kontroll godkände eller nekade en trader. `account` är tomt, `data.trader` är traderns id och e-post och `data.identity` resultatet, utan handlingen. |
| `webhook.test` | En test som firman skickade från adminpanelen. `account` är tomt, och `data.message` säger vad det är. |

- Kroppen är `{ "id", "type", "createdAt", "account": { "id", "number", "email", "challengeId", "reference" }, "data": { ... } }`, där `data` är regelmotorns beslut. För utbetalningar har `data.payout` utbetalningens id, vinst, vinstandel, belopp och status.
- Headrarna `Prop-Webhook-Id` och `Prop-Webhook-Event` anger vilken leverans och händelse det är. `Prop-Signature` är `t={unix-sekunder},v1={hex}`, där `hex` är HMAC-SHA256 med firmans hemlighet av `{t}.{kropp}`.
- Svar 2xx räknas som mottaget. Annars görs ett nytt försök efter 30 sekunder, med dubbel väntan varje gång och högst 6 timmar, i 16 försök (ungefär ett och ett halvt dygn). Samma leverans kan komma mer än en gång och känns igen på sitt id.

## Konfiguration

| Sektion | Innehåll |
|---|---|
| `ConnectionStrings:Prop` | Propfirm-plattformens databas. Lokalt Postgres från `deploy/docker-compose.yml`. |
| `TradingPlatform` | `Url` till handelstjänsten (slutar med `/`), hur länge ett anrop väntar på nya händelser och hur många som läses åt gången. |
| `Firms` | Firmor som sparas i databasen vid varje start och är live, för utveckling och tester: `Id`, `Name`, `ApiKeySha256`, `Trading` (`Server`, `ApiKey`, `Group`, `Currency` med standard `USD`), `Webhook` (`Url`, `Secret` på minst 32 tecken), `Portal` (se [specen för portalen](portal.md)), `SeedChallenges`, `SeedAdmins` och `SeedTraders`. |
| `Firms:N:SeedChallenges` | Challenges som skapas eller ersätts vid varje start, med `Template`, `Id`, `InitialBalance` och `Currency`. Mallen `TwoStep` är standardmallen. `QuickTest` är samma mall med 0,1 % vinstmål och utan minsta antal handelsdagar, så att hela vägen till en utbetalning kan provas på några minuter. Den är bara för utveckling. Konton som redan har startat behåller sina regler. |
| `TradingPlatform:PartnerApiKey`, `Platform`, `Signup`, `Sandbox`, `Email`, `Secrets` | Registreringen och sandlådan. Se [specen för registrering och sandlåda](registrering.md). |
| `Platform:ApiUrl`, `Payments`, `Firms:N:Payments`, `Firms:N:SeedChallenges:M:Price` | Köp i portalen. Se [specen för köp i portalen](kop.md). |
| `Billing`, `Firms:N:Slots` | Platser och vad firman betalar oss. Se [specen för platser och betalning](platser-och-betalning.md). |
| `Platform:OpsUrl`, `Billing:ReviewDeposit`, `Staff` | Vår granskning och vår adminvy. Se [specen för granskning och avstängning](granskning.md). |
| `Identity`, `Billing:IdentityChecks` | ID-kontrollen genom Didit och vad den kostar firman. Se [specen för ID-kontroll](id-kontroll.md). |
| `Domains` | Firmornas egna domäner: `CnameTarget`, värdnamnet de pekar på, tomt för att stänga av egna domäner, `CheckInterval`, hur ofta väntande domäner slås upp (standard 5 minuter), och `DnsOverHttpsUrl`, resolvern med JSON-API (standard Cloudflare). Se ADR 0039. |
| `Login` | Regler för lösenord och inloggning: `MinimumPasswordLength` (standard 10), `AttemptsPerMinute` per IP-adress (standard 10, 0 för ingen gräns) och `SessionLifetime`, hur länge en oanvänd session gäller (standard 12 timmar). I utveckling är reglerna avstängda och sessionen gäller i 30 dagar. |

I utveckling registrerar sig firmor på http://app.localhost:3002/signup utan bekräftelse av e-postadressen, får sin portal på till exempel http://acme.localhost:3002, skickar sin ansökan med testbetalning och går live på http://acme.localhost:3002/admin/go-live, och godkänns av `ops@test.com` med lösenordet `ops` på vår adminvy http://ops.localhost:3002. Fakturorna har säljaren från `Billing:Seller` i `appsettings.Development.json`. Mejl hamnar i Mailpit på http://localhost:8025. Firman `demo-firm` har nyckeln `dev-prop-key` och ingen gräns för platser. Den använder servern `demo-firm` på handelsplattformen med nyckeln `dev-admin-key`, har challengerna `two-step-100k` och `quick-test-100k`, som säljs för 499 och 9 USD med testbetalningar på http://localhost:3002/buy, portalen på http://localhost:3002, administratören `admin@test.com` med lösenordet `admin` och traderna `anna@test.com` med `anna` och `test@test.com` med `test`. Allt detta gäller bara lokal utveckling.

## Begränsningar

- Tjänsten startar bara i miljön Development. Före produktion behövs HTTPS och hantering av hemligheter, till exempel för handelsplattformens nyckel och webhook-hemligheten.
- Tjänsten körs som en instans. Läsningen av händelseströmmen och kommandona har ännu inga lås mellan instanser, och firmorna hålls i minnet.
- En firma går live först när vi har granskat och godkänt den. Den kan inte byta namn eller kort namn, eller tas bort än.
- Ett kommando som handelsplattformen avvisar läggs åt sidan som misslyckat och syns bara i databasen och loggen. Ett nekat uttag gör dessutom utbetalningen `Failed`.
- Utbetalningar som firman har godkänt betalas av firman själv, till traderns utbetalningsmetod. Tjänsten hanterar aldrig pengar och kontrollerar inte metoden.
- Ett mejl som inte går iväg efter 12 försök ges upp och syns bara i `email_outbox` och loggen.
- Köp i portalen betalas till firmans egen leverantör. Tjänsten ser bara att betalningen är gjord.
- Firmans kontroller av traders bockas för för hand. En koppling till en tjänst för identitetskontroll, till exempel Sumsub eller Veriff, kommer senare.
- Alla administratörer får göra allt. Roller, till exempel ägare, administratör och support, kommer senare.
- Certifikatet för en egen domän görs av vår proxy, som frågar `GET /api/tls/allowed?domain=` innan den hämtar ett. Proxyn är inte uppsatt än (steg 10).

## Tester

Testerna ligger i `prop/tests/Prop.Api.Tests`. De kör tjänsten mot riktig Postgres i en container via Testcontainers och kräver Docker. Handelsplattformen är en låtsad version i minnet som beter sig som vår. Klockan flyttas bara när testet säger till, och firmans webhooks tas emot av testet.

- `FirmApiTests`: nyckeln, challenges och deras kontroll, en direkt funded challenge som börjar på funded-kontot, start av konto med golven i rätt ordning, firmans referens, felaktiga starter, att firmor inte ser varandras konton, inloggningslänken och inbjudan till portalen.
- `PortalApiTests`: portalens API, se [specen för portalen](portal.md).
- `PayoutFlowTests`: en utbetalning från begäran via uttaget på handelsplattformen till godkänd och betald, med webhooks, orsaker att neka, konsistensregeln, ett nekat uttag som gör utbetalningen `Failed` och ett nytt försök som lyckas, nej från firman, nej med vinsten tillbaka på kontot och mejlet om det, traderns begäran och administratörens beslut i portalen med utbetalningsmetoden, att metoden sparas krypterad, att andra firmor inte når utbetalningarna och gränserna för listor.
- `PasswordResetTests`: länkarna för nytt lösenord, kontrollen av inbjudningar och plattformens mejl med inloggningslänkar. Se [specen för portalen](portal.md).
- `SignupTests`: registrering med och utan bekräftelse av e-postadressen, länkens livslängd, korta namn som är ogiltiga, reserverade eller tagna, också på handelsplattformen, kontroll av formuläret, att registreringen bara finns på plattformens adress, ett mejl som inte gick iväg, en server som skapas när handelsplattformen är tillbaka och ett svar som gick förlorat, välkomstlänken, inloggning med lösenordet från registreringen, sandlådans gräns, omstart och att en konfigurerad firma inte kan ta över en registrerad.
- `NotificationPreviewTests`: förhandsvisningen av varje notis i firmans namn och utseende, om firmans challenges och utan att något köas eller skickas. Se [specen för portalen](portal.md).
- `AdminLastLoginTests`: när varje administratör senast loggade in, på alla sätt att logga in. Se [specen för portalen](portal.md).
- `AdminSettingsTests`: färgerna, en logga som laddas upp, visas och tas bort och loggor som nekas, nyckel för firmans API, webhooks som signeras med firmans hemlighet och en ny hemlighet, inbjudningar till administratörer, som skickas igen eller dras tillbaka, en borttagen administratör som loggas ut direkt, att administratörer bara når sin egen firma, challenges från mallen och i firmans valuta, och att inställningarna bara är för administratörer.
- `AdminPanelTests`: adminpanelens översikt, sökningen bland kontona, kontots trader, firmans kontroller av tradern, mejlet till tradern och förhandsvisningen av det, historiken för firmans konton, utbetalningskön och challengernas siffror. Se [specen för portalen](portal.md).
- `OrderFlowTests`: köp i portalen med testbetalning, Stripe och firmans egen betalsida. Se [specen för köp i portalen](kop.md).
- `DiscountTests`: rabattkoder i butiken, att en kod inte används oftare än firman tillåter, kontrollen av nya koder, koder för nya försök, "Try again" på ett underkänt konto och vad ett brott stängde.
- `SupportTests`: supportärenden mellan traders och firman, se [specen för supportärenden](support.md).
- `IdentityTests` och `DiditDecisionTests`: ID-kontrollen med testkontrollen, Didit och firmans egen tjänst, och debiteringen, se [specen för ID-kontroll](id-kontroll.md).
- `DomainTests`: en egen domän som blir portalens adress när posterna finns, också med samma adresser i stället för CNAME, DNS som inte svarar, domäner som nekas, att en domän hör till en firma, att den kan tas bort och att konfigurerade firmor inte kan byta.
- `SmtpEmailSenderTests`: ett riktigt mejl genom SMTP till Mailpit i en container, och att en mejlserver som inte svarar ger ett fel som går att hantera.
- `PlatformEmailsTests`, `TraderEmailsTests` och `ReceiptTests`: våra mejl med samma text som HTML i Kronants utseende, ordmärket, knappen, stegen och tabellerna, länkar i texten, mejlen till traders i firmans utseende med sidfoten, och kvittot efter ett köp i mejlet och som PDF. Se [specen för köp i portalen](kop.md).
- `SlotTests`, `BillingFlowTests`, `StripeBillingTests` och `BillingRulesTests`: platserna och betalningen. Se [specen för platser och betalning](platser-och-betalning.md).
- `ReviewTests` och `SuspensionTests`: vår granskning, vår adminvy och avstängning. Se [specen för granskning och avstängning](granskning.md).
- `OpsPanelTests`: vår adminvy över alla firmor, med översikten, sökningen bland firmorna, kontrollerna, en firmas utbetalningar och vad firmorna betalar. Se [specen för granskning och avstängning](granskning.md).
- `ExpiryTests`: en challenge utan ny position i 30 dagar som tar slut och stänger kontot med webhooken och orsaken, en affär på sista dagen som räknas fast händelsen kommer efter att dagen tagit slut, en ny position som flyttar sista dagen, en fas med tidsgräns som tar slut, och en tidsgräns som är kortare än fasens handelsdagar.
- `ChallengeFlowTests`: kontot som terminalen visar det, också för konton som öppnades innan, reglerna som berättas när kontot öppnas och igen bara när en handelsdag räknas, och igen för konton som saknar dem, dagliga golvet vid midnatt i Stockholm, en klarad fas som stänger kontot och öppnar nästa och mejlar tradern, brott med bevis, godkänd finansiering med mejlen till administratören och tradern, en notis som firman stängt av och inte skickas, påminnelsen om att öppna en affär en gång per sista dag, annullering, avbrott i handelsplattformen där kommandona behåller sin ordning, omstart där varje händelse ändå hanteras exakt en gång, och signerade webhooks som skickas igen.
- `TerminalRulesTests`: reglerna för terminalen under en utvärdering med handelsdagar och tidsgränser vid midnatt i Stockholm, på ett finansierat konto med bästa dagens andel mot konsekvensregeln, och att tidsgränser bara finns medan steget handlas.
- `TradingPlatformClientTests`: klienten mot svar som handelsplattformens, positionernas fält i händelserna, att regelmotorns golv blir plattformens regler, att ett konto värderas med sina golv, att ett uttag bara dras en gång och bär plattformens orsak vid nej, och att konton pausas och återupptas.
- `TraderDashboardTests`: varje affär med provisioner och saldot efter, att historiken byggs om från strömmens början, kontona på traderns översikt med faser, resultat i dag och i fasen, vinstmål och golvens avstånd, att versionen ändras med historiken, en klarad fas med start, resultat och handelsdagar, när och varför ett konto slutade, affärer sida för sida och som CSV-fil, att en trader bara ser sina egna konton, och traderns utbetalningar med summor.
- `AccountPerformanceTests`: statistiken, avrundningen, dag för dag med handelsdagar i Stockholm, resultatet i dag och förloppet mot vinstmålet.
- `TradingContractTests`: varje väg och fält som klienten använder finns i `contracts/trading/trading-service.json`.
- `TradingDaysTests`: handelsdagar i olika tidszoner, vid klockslag på kvällen och vid sommartid och vintertid.
