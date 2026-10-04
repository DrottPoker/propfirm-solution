# Spec: portalen

- Fas: 4d, utbetalningar i 5, registrering och adminpanelens inställningar i 6, platser och betalning i 7, köp i 8, granskning och vår adminvy i 9a, traderns översikt, adminpanelens och vår adminvys nya utformning efter 9a
- Status: Implementerad i `prop/portal` och `prop/src/Prop.Api/Portal`
- Datum: 2026-10-04

## Syfte

Portalen är firmans egen sida för traders och administratörer, med firmans namn, logga, färger och domän. Tradern köper challenges, följer dem med graf, statistik och affärer, öppnar handelsterminalen och begär utbetalningar härifrån. I adminpanelen ser firman först det som väntar på den och hur firman går, och startar challenges, bjuder in traders, godkänner funded-konton, hanterar utbetalningar och ordrar, avbryter konton och ställer in sina challenges, sitt utseende och sina integrationer. På plattformens egen adress visar samma portal i stället registreringen, med vårt namn (se [specen för registrering och sandlåda](registrering.md)), och på vår adminvys adress vår personals granskning av firmor (se [specen för granskning och avstängning](granskning.md)). Besluten finns i [ADR 0014](../adr/0014-vitmarkt-portal-pa-firmans-adress.md) och [ADR 0017](../adr/0017-firmor-registrerar-sig-sjalva.md). Kontona och regelmotorn beskrivs i [specen för propfirm-tjänsten](propfirm-tjanst.md).

## Sidor

| Sida | För | Innehåll |
|---|---|---|
| `/login` | Traders | Inloggning med e-post och lösenord, och en länk till butiken när firman säljer i portalen. |
| `/buy` | Alla | Challengerna till salu med regler och pris. Köparen anger e-post, eller köper som inloggad trader, godkänner firmans villkor och skickas till betalningen. Se [specen för köp i portalen](kop.md). |
| `/orders/{id}?token=` | Köparen | Ordern efter betalningen. Väntar på att leverantören bekräftar, och berättar sedan hur tradern kommer in i portalen. |
| `/checkout/test?order=&token=` | Köparen | Testbetalning utan pengar, för firmor i sandlådan. |
| `/invite?token=` | Traders | Tradern väljer lösenord med firmans inbjudan och loggas in. |
| `/` | Traders | Traderns startsida: det som behöver tradern först, ett kort för varje konto som handlas eller är på väg, och en tabell med konton som har slutat. Se Traderns översikt nedan. En gammal länk med `?account=` öppnar kontots sida. |
| `/accounts/{id}` | Traders | Ett konto: faserna, för ett funded-konto nästa utbetalning, saldo, equity, resultatet i dag och i fasen, målen och gränserna, utbetalningarna, historiken per fas med graf, dag för dag, statistik och stängda affärer, och challengens regler. Se Traderns översikt nedan. |
| `/payouts` | Traders | Traderns utbetalningar från alla konton med hur långt de har kommit, och per valuta vad firman har betalat, vad som är på väg och vad som kan begäras nu. |
| `/admin/login` | Administratörer | Inloggning för firmans administratörer. |
| `/admin` | Administratörer | Översikten. En firma i sandlådan ser stegen till live, och en firma som är live det som väntar på den, fyra nyckeltal, försäljning och utbetalningar per vecka, platserna och de senaste händelserna. Medan firmans server skapas visas det i stället. Se Adminpanelen nedan. |
| `/admin/accounts` | Administratörer | Firmans konton, det nyaste först, i grupper med antalet i varje: alla, i utvärdering, väntar på firman, funded och avslutade. Sökning på en del av e-postadressen, kontonumret eller en del av firmans referens, och val av challenge. `?group=` och `?search=` väljer gruppen och sökningen, och adressen följer med när de ändras. 50 konton åt gången med knappen "Show more". Knappen "Start a challenge" öppnar en panel från sidan. |
| `/admin/accounts/{id}` | Administratörer | Ett konto: beslutet som väntar, varför kontot har stannat, och flikarna Overview (faserna, siffrorna, målen och gränserna, tradern och reglerna tradern köpte), Trading (historiken per fas med graf, dag för dag, statistik och affärer), Payouts (utbetalningarna med firmans beslut) och Rule log (varje indata och beslut). `?emailed=` säger hur mejlet till tradern gick efter att challengen startades. |
| `/admin/orders` | Administratörer | Ordrar från portalen: köpare, challenge, belopp, leverantör, status och återbetalningar eller bestridanden. Ordrar från firmans egen betalsida markeras som betalda härifrån, och ordrar som inte gick genom Stripe som återbetalda, med en referens i en dialog. |
| `/admin/payouts` | Administratörer | Summorna att godkänna och att betala, det som betalats de senaste 30 dagarna och hur många dagar det i snitt tog. Utbetalningarna att godkänna och att betala, den äldsta först, och de betalda, de nekade och alla, den nyaste först, var och en med hur mycket kontot fått utbetalt innan. Firman godkänner, markerar som betald med en valfri referens, eller nekar med en orsak som tradern ser, i en dialog. `?view=to-pay` öppnar de att betala. |
| `/admin/challenges` | Administratörer | Firmans challenges: faserna med mål och vinstandel, reglerna, priset i butiken och om den säljs, och hur den går: öppna konton, startade de senaste 30 dagarna och andelen som klarade utvärderingen de senaste 90. Varje challenge kan kopieras och ändras. |
| `/admin/challenges/edit` | Administratörer | En challenge att ändra (`?id=`), en kopia av en annan (`?from=`) eller en ny från mallen: storlek, handelsdagens start och tidszon, dagar utan en ny affär, ett kort per fas med mål, förlustgränser, handelsdagar, tidsgräns och vinstandel, en till tre faser före funded, och priset i butiken i en valuta som kan vara en annan än kontots. Beloppen står bredvid procenten, och det tradern ser står bredvid formuläret. |
| `/admin/design` | Administratörer | Portalens utseende: loggan, ett tema att börja från (mörkt, ljust eller marinblått), varumärkesfärgen och texten på knapparna med kontrasten mellan dem, alla färger var för sig och en förhandsvisning av traderns startsida. |
| `/admin/checkout` | Administratörer | Hur portalen tar betalt: ingen försäljning, testbetalning, Stripe med firmans nycklar och adressen för Stripes webhook, eller firmans egen betalsida, och villkoren för köpare. |
| `/admin/integrations` | Administratörer | Firmans korta namn, portalens adress, servern på handelsplattformen och kontonas valuta, nyckel för firmans API och webhookens adress och hemlighet. Nycklar och hemligheter visas bara en gång. |
| `/admin/billing` | Administratörer | Plan and billing: stegen till live, gå live genom att betala, platserna, fler eller färre platser, automatisk utökning, kortet, obetalda månader och debiteringarna. Se [specen för platser och betalning](platser-och-betalning.md). |
| `/admin/billing/checkout/{id}` | Administratörer | Testsidan för betalningar till plattformen och kort, i utveckling. Skickar tillbaka till sidan betalningen startade från. |
| `/admin/verification` | Administratörer | Vår granskning av firman: status och vårt meddelande, ansökan, dokument och knappen som skickar med handpenningen. Syns i menyn tills firman är live. Se [specen för granskning och avstängning](granskning.md). |
| `/admin/team` | Administratörer | Administratörerna, inbjudningar som väntar, en ny inbjudan med e-post, och borttagning av andra än sig själv. |
| `/admin/settings` | Administratörer | Skickar till `/admin/design`, sedan inställningarna delades i portalens utseende, betalningen och integrationerna. |
| `/admin/welcome?token=` | Alla | Engångslänken efter registreringen. Loggar in administratören och öppnar adminpanelen. |
| `/admin/invite?token=` | Alla | En inbjuden administratör väljer lösenord och loggas in. |
| `/signup`, `/verify?token=` | Plattformen | Registreringen och bekräftelsen av e-postadressen. Se [specen för registrering och sandlåda](registrering.md). |
| `/ops/login`, `/ops`, `/ops/firms`, `/ops/firms/{id}`, `/ops/billing` | Vår personal | Vår adminvy (ADR 0024): inloggning, översikten med det som väntar på oss, alla firmor med sökning och grupper, en firma med granskning och kontroller, siffror, betalningar, team och historik, och vad firmorna betalar. Se [specen för granskning och avstängning](granskning.md). |

- Den som inte är inloggad skickas till rätt inloggning, och den som är inloggad med den andra rollen till sin egen startsida.
- Kontots siffror uppdateras var femte sekund. Grafen, statistiken och affärerna hämtas igen bara när kontots historik har ändrats.
- Traderns sidhuvud har länkarna Accounts och Payouts, knappen "Buy a challenge" när firman säljer i portalen, och en meny bakom traderns initialer med e-postadressen och utloggningen. På en telefon ligger länkarna i menyn. En trader utan konton får en knapp till butiken.
- Adminpanelen har en meny till vänster: Overview, Accounts, Payouts, Orders och Challenges, och under Your firm Portal design, Checkout, Integrations, Team, Plan and billing och, tills firman är live, Verification. Vid Payouts står hur många utbetalningar som väntar på godkännande och vid Accounts hur många konton som väntar på ett funded-konto. Längst ned finns länken till portalen som traders ser den, administratörens e-postadress och utloggningen. På en telefon ligger menyn bakom en knapp överst.
- Knappen "Request payout" fungerar när regelmotorn säger att en utbetalning kan begäras. Annars visas orsaken. Tradern bekräftar först, eftersom hela vinsten tas från handelskontot direkt.
- Knappen "Open terminal" fungerar när kontot är aktivt. Den hämtar en engångslänk och öppnar terminalen inloggad på fasens konto.
- På en adress som ingen firma har visar portalen bara att ingen portal finns där. På plattformens adress skickas firmans sidor till registreringen, och på vår adminvys adress till `/ops`. På en firmas adress finns varken plattformens sidor eller vår adminvy.
- En firma i sandlådan visar en rad om att det är en testmiljö på alla sidor, så att ingen trader tror att den är på riktigt.
- Adminpanelen visar en rad på alla sidor när vi har stängt av firman, när firmans månad är obetald, när en betalning nekats eller när platserna är slut eller nästan slut.
- Ett pausat konto kan öppnas i terminalen, där tradern kan stänga sina positioner men inte öppna nya.

## Traderns översikt

Beslutet om historiken bakom översikten finns i [ADR 0022](../adr/0022-handelshistorik-for-traderns-oversikt.md). Propfirm-tjänsten räknar alla belopp. Portalen formaterar dem och väljer färger.

**Startsidan** (`/`):

- Överst det som behöver tradern, det mest brådskande först: ett konto nära en förlustgräns, en utbetalning som kan begäras, fem dagar eller mindre kvar att öppna en ny affär eller att klara fasen, ett pausat konto och ett konto som klarat alla faser och granskas av firman.
- Ett kort per konto, det nyaste först: challengens namn, kontonummer och storlek, status, faserna som steg, equity eller saldot, resultatet i fasen eller för ett funded-konto traderns andel nu, förloppet mot vinstmålet eller handelsdagarna mot nästa utbetalning, hur mycket equity kan falla innan den dagliga och den totala gränsen, handelsdagar eller utbetalt, den första sista dagen att handla, och knapparna Details och "Open terminal".
- En tabell med konton som har slutat: utfallet och orsaken, när och slutsaldot.

**Kontots sida** (`/accounts/{id}`), i den här ordningen:

1. Varför kontot har stannat eller vad som händer med det: underkänt med beviset, slut på tid, annullerat, kontot öppnas, under granskning, pausat eller inte värderat just nu.
2. Faserna som rutor: när en klarad fas klarades, med resultat och handelsdagar, när den aktuella startade och vad funded ger.
3. För ett funded-konto nästa utbetalning: traderns andel med knappen "Request payout" när den kan begäras, annars vad som saknas, och medan en utbetalning är på väg hur långt den kommit. Tradern bekräftar först, eftersom hela vinsten tas från handelskontot direkt.
4. Saldo, equity med öppna positioner och deras resultat, resultatet i dag från saldot när handelsdagen började, och resultatet i fasen eller det som betalats ut.
5. Målen och gränserna för den aktuella fasen: vinstmålet med förlopp, de två förlustgränserna med hur mycket equity kan falla och när den dagliga börjar om, handelsdagarna som steg, och dagarna kvar till tidsgränsen och till att en ny affär måste öppnas. En gräns som bröts visas med beviset.
6. Utbetalningarna med status, steg och firmans anteckning.
7. Historiken för en fas, vald i en rad ovanför allt den gäller, den aktuella först: grafen, dag för dag, statistik och stängda affärer med knappen "Show more" och en länk till CSV-filen.
8. Challengens regler för varje fas som den köptes, med belopp, handelsdagens start och tidszon och dagarna utan ny affär.

**Färger.** Resultat över noll är gröna och under noll röda. En förlustgräns där mindre än 25 % av dess avstånd är kvar är gul och under 10 % röd, som i terminalen. Avståndet är hela förlusten gränsen tillåter enligt reglerna.

**Grafen** visar saldot efter varje saldoändring som en steglinje, equity nu som en punkt medan fasen handlas, vinstmålet, det dagliga golvet som ändras varje dag och det totala golvet. Det totala golvet ritas när det inte gör grafen mer än 60 % högre, annars står det under grafen. Tidsaxeln går från första ändringen till nu, minst en minut, och visar sekunder, klockslag, dag och tid eller dag efter hur lång tiden är. Muspekaren eller piltangenterna visar varje ändring: tid, saldo, vad som ändrade det och det dagliga golvet då. Samma ändringar finns i en tabell under grafen.

**Förberett för firmans egna designval.** Firmorna ska senare kunna välja mer av hur portalen ser ut. Därför:

- använder sidorna bara färgvariablerna, också för text på accentfärgen (`--accent-foreground`), och ljusare toner görs från dem
- finns kort, märken, förloppsstaplar och val i `src/components/ui.tsx`, och färgerna för resultat och gränser i `src/lib/dashboard.ts`
- är kontosidans delar en lista i `TraderAccount.tsx`, så att en firma senare kan välja vilka delar dess traders ser och i vilken ordning.

## Adminpanelen

Besluten bakom adminpanelens siffror, sökning och logga finns i [ADR 0023](../adr/0023-adminpanelens-oversikt-och-firmans-logga.md). Propfirm-tjänsten räknar alla antal och belopp, per valuta. Portalen formulerar och ordnar dem.

**Översikten för en firma som är live** (`/admin`), uppdaterad var 30:e sekund:

1. **Needs you**, det mest brådskande först: utbetalningar att godkänna med summan och hur länge den äldsta har väntat, godkända utbetalningar att betala, de tre nyaste konton som klarat alla faser och väntar på ett funded-konto (och hur många fler som väntar), och platser som snart eller redan är slut. Varje rad har en knapp dit det görs. Problem med betalningen till oss står i stället på varje sida.
2. Fyra nyckeltal: konton som handlas nu (i utvärdering och funded), försäljningen de senaste 30 dagarna med antalet köp, utbetalningarna de senaste 30 dagarna med det som väntar, och andelen som klarade utvärderingen de senaste 90 dagarna. Andelen är de klarade delat med de klarade och de underkända. Annullerade räknas inte.
3. Försäljning och utbetalningar per vecka i tolv veckor, från måndag i UTC, i firmans valuta. Belopp i andra valutor ritas inte, och det står under diagrammet när det finns sådana. Samma siffror finns i en tabell för skärmläsare. På en telefon behåller diagrammet en läsbar bredd och går att skrolla i sidled.
4. Platserna: tagna av öppna challenges, hållna för ordrar som väntar på betalning och lediga, planen, nästa betalning och om fler köps när de tar slut.
5. De senaste händelserna, högst tolv från de senaste 30 dagarna: challenges som startats eller köpts, klarade faser, alla faser klarade, funded-konton som startat, challenges som underkänts, tagit slut på tid eller annullerats, och utbetalningar som begärts, betalats eller nekats, var och en med tradern, kontot och tiden.

**Översikten för en firma i sandlådan** visar stegen till live, med hur många som är klara: servern, challenges, priser, hur traders betalar (testbetalning räknas inte), loggan eller färgerna, att prova som trader med ett konto, vår granskning och att gå live. Stegen kan tas i vilken ordning som helst, och det första som återstår är markerat med en knapp dit det görs. Granskningen väntar medan vi granskar, och att gå live går först när vi har godkänt firman. Bredvid står hur många testkonton som är öppna av hur många, vad sandlådan inte gör och vad live kostar.

**Starta en challenge** öppnar en panel från sidan med traderns e-post, challengerna med storlek, faser, vinstandel och pris, firmans referens och valet att mejla tradern, och hur många platser som är lediga. Är e-postadressen redan en traders står kontona tradern har. Mejlet går i firmans namn: en inbjudan att välja lösenord, eller att challengen har startat för en trader som redan har ett. Kontots sida berättar sedan hur mejlet gick.

**Kontot** (`/admin/accounts/{id}`):

- Rubriken har challengen, status, kontonumret, traderns e-post som länk till traderns konton, referensen, handelskontot och när kontot startade, och knappen "Cancel account". En annullering frågar först i en dialog, med en valfri orsak.
- Ett konto som klarat alla faser har överst beslutet om funded-kontot, med storleken och vinstandelen. Godkännandet bekräftas i en dialog.
- Fliken Overview har faserna, saldo, equity, resultatet i dag och i fasen eller det som betalats ut, målen och gränserna, traderns kort och reglerna tradern köpte. Traderns kort har sedan när tradern finns, vad tradern köpt i portalen och fått utbetalt, om tradern valt lösenord, knapparna som mejlar tradern eller skapar en inbjudningslänk, traderns konton hos firman och vad som startade kontot.
- Fliken Trading har samma historik som tradern ser, med graf, dag för dag, statistik, affärer och CSV-fil.
- Fliken Payouts har kontots utbetalningar med firmans beslut, och fliken Rule log varje indata och beslut.

**Utbetalningarna** godkänns direkt. Att markera som betald frågar efter firmans referens, och att neka efter orsaken som tradern ser och påminner om att vinsten inte går tillbaka till kontot. En utbetalning som väntat mer än två dagar på godkännande är markerad.

**Challenge-redigeraren** har ett kort per fas. Beloppen bredvid procenten räknas i portalen som regelmotorn gör, avrundade till hela cent, eftersom challengen inte är sparad än. Kontona får sina belopp från propfirm-tjänsten. Priset sparas med challengen, och en challenge utan pris säljs inte men kan startas av firman.

**Portalens utseende** sparar loggan direkt när den laddas upp, och färgerna när firman sparar. Ett tema sätter ytornas, textens och signalernas färger och behåller varumärkesfärgen och texten på knapparna. Kontrasten mellan varumärkesfärgen och texten på knapparna visas, med en varning under 4,5:1. Förhandsvisningen visar traderns startsida med färgerna som inte är sparade än.

## Utseende

Utseendet hämtas på servern med `GET /api/portal/branding` innan sidan visas, tillsammans med firmans status. Färgerna blir CSS-variabler och kontrolleras en gång till i portalen innan de används. En registrerad firma laddar upp sin logga och ändrar färgerna under Portal design, och sidan ritas om med det nya utseendet när det sparas. En uppladdad logga visas från portalens egen adress, `/api/portal/logo/{sha256}`, och går före en adress från konfigurationen.

| Färg | Används till |
|---|---|
| `background`, `panel`, `border` | Bakgrund, paneler och kanter |
| `foreground`, `muted` | Text och dämpad text |
| `accent` | Knappar, länkar och markeringar |
| `accent-foreground` | Text på accentfärgen, som på knappar. Vit om firman inte väljer annat. |
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
| `GET /accounts` | Trader | Traderns konton som i `GET /accounts/{id}`, värderade just nu. |
| `GET /accounts/{id}` | Trader | Kontot med `challenge` (definitionen det köptes med), `live` (saldo, equity och golv med marginal och golvets hela avstånd från handelsplattformen), `stages` (varje fas med läge, konto, start, när den klarades, resultat, handelsdagar och reglerna som belopp), `results` (saldo, equity, öppna positioners resultat, resultatet i fasen i belopp och procent, resultatet i dag med saldot och tiden när dagen började och när nästa börjar, vinstmålets förlopp och vad som betalats ut), `breach` (tid, golv, nivå, equity och orsak) när ett golv bröts, `expiry` (tid, orsak och dag) när tiden tog slut, `endedAt`, `payouts`, kontots utbetalningar, och `historyVersion`, som ändras med historiken. Andras konton svarar 404. |
| `GET /accounts/{id}/performance?stage=` | Trader | Hur fasen har gått: `balance` (varje saldoändring med tid, slag, belopp och saldot efter), `dailyFloor` och `maxLossFloor` (golvens nivåer över tid), `days` (handelsdagar med stängda affärer, lots, resultat och om dagen räknades) och `statistics`. Utan `stage` den senaste fasen som har startat. 404 för en fas som inte har startat. |
| `GET /accounts/{id}/trades?stage=&before=&limit=` | Trader | Fasens stängda affärer, nyaste först, högst 200 åt gången (standard 50). `next` är värdet för `before` till nästa sida. Varje affär har symbol, sida, volym, tider, priser, vinst, provision, resultat och orsak. |
| `GET /accounts/{id}/trades.csv?stage=` | Trader | Fasens stängda affärer som CSV-fil, äldsta först, med tider i UTC. |
| `GET /logo/{sha256}` | Alla | Loggan firman har laddat upp, med `Cache-Control: immutable`, `nosniff` och `Content-Security-Policy: sandbox`. 404 för en annan logga än firmans nuvarande. |
| `GET /payouts` | Trader | Traderns utbetalningar från alla konton, nyaste först, och `totals` per valuta: betalt, på väg och vad som kan begäras nu. |
| `POST /accounts/{id}/terminal-link` | Trader | En engångslänk till terminalen. 409 när kontot inte är aktivt. |
| `POST /accounts/{id}/payouts` | Trader | Begär en utbetalning av funded-kontots vinst. 201 med utbetalningen, eller 409 med orsaken. |
| `GET /admin/overview` | Admin | Översikten: `accounts` (antalet i varje grupp), `payouts` (som `GET /admin/payouts/summary`), `sales` (betalda ordrar de senaste 30 dagarna utan de återbetalda, antal och summa per valuta), `passRate` (klarade och avslutade utvärderingar de senaste 90 dagarna, utan annullerade), `weeks` (försäljning och betalda utbetalningar per valuta för var och en av de senaste tolv veckorna, från måndag i UTC, den äldsta först) och `activity` (högst tolv händelser från de senaste 30 dagarna, den nyaste först). |
| `GET /admin/challenges` | Admin | Firmans challenges. |
| `GET /admin/challenges/figures` | Admin | Varje challenges öppna konton (`trading`), konton som startat de senaste 30 dagarna och `passRate` de senaste 90. |
| `GET /admin/accounts?search=&group=&challengeId=&before=&limit=` | Admin | En sida av firmans konton, den nyaste först, högst 200 (standard 50): de som `search` hittar i `group` (`All`, `Evaluation`, `AwaitingFunding`, `Funded` eller `Ended`) och challengen. `search` är en del av e-postadressen, kontonumret med eller utan # eller en del av firmans referens. `counts` har antalet i varje grupp för samma sökning, och `next` är värdet för `before` till nästa sida. |
| `POST /admin/accounts` | Admin | Startar en challenge, som i firmans API. |
| `GET /admin/accounts/{id}` | Admin | Kontot som i traderns `GET /accounts/{id}`. |
| `GET /admin/accounts/{id}/history` | Admin | Varje indata och beslut. |
| `POST /admin/accounts/{id}/approve-funding` | Admin | Godkänner funded-kontot. 409 innan faserna är klara. |
| `POST /admin/accounts/{id}/cancel` | Admin | Avbryter med `{ "reason" }`. |
| `POST /admin/accounts/{id}/invite` | Admin | En inbjudningslänk till portalen för kontots trader. |
| `POST /admin/accounts/{id}/email-trader` | Admin | Mejlar kontots trader i firmans namn: en inbjudan att välja lösenord (`Invitation`), eller att challengen har startat för en trader som redan har ett (`Notice`). 503 när mejlet inte går iväg. |
| `GET /admin/accounts/{id}/trader` | Admin | Kontots trader: e-post, sedan när, om tradern valt lösenord, traderns konton hos firman, den nyaste först, antalet betalda ordrar och vad de kostade utan de återbetalda, det som betalats ut per valuta, och `order`, den betalda ordern som startade kontot. |
| `GET /admin/accounts/{id}/performance`, `/trades`, `/trades.csv` | Admin | Kontots historik per fas som i traderns vägar, för alla firmans konton. |
| `GET /admin/payouts?status=&oldestFirst=&limit=` | Admin | Firmans utbetalningar, högst 500, valfritt bara de med vissa statusar: den nyaste först, eller med `oldestFirst` den äldsta först. Varje utbetalning har challengens namn och hur många utbetalningar kontot fått betalda innan den begärdes, och hur mycket (`paidBefore`, `paidBeforeAmount`). |
| `GET /admin/payouts/summary` | Admin | Utbetalningarna att godkänna och att betala med antal, summa per valuta och sedan när den äldsta väntat, de betalda de senaste 30 dagarna, och `averageDaysToPay`, dagarna från begäran till betald i snitt för dem. |
| `POST /admin/payouts/{payoutId}/approve` | Admin | Godkänner en utbetalning som väntar. |
| `POST /admin/payouts/{payoutId}/mark-paid` | Admin | Markerar en godkänd utbetalning som betald, med `{ "reference" }`. |
| `POST /admin/payouts/{payoutId}/reject` | Admin | Nekar en utbetalning som väntar eller är godkänd, med `{ "reason" }`. |
| `POST /admin/welcome`, `POST /admin/invites/accept` | Alla | Inloggning med länken efter registreringen, och en inbjuden administratörs val av lösenord. |
| `GET /admin/firm` och vägarna under den, `GET /admin/challenge-templates`, `PUT /admin/challenges/{challengeId}`, `/admin/admins` | Admin | Firmans inställningar, loggan och färgerna, challenges och administratörer. Se [specen för registrering och sandlåda](registrering.md). |
| `GET /shop`, `/orders` och vägarna under den | Alla | Butiken och köparens order. Se [specen för köp i portalen](kop.md). |
| `/admin/orders`, `/admin/prices`, `PUT /admin/challenges/{challengeId}/price`, `PUT /admin/firm/payments` | Admin | Ordrar, priser och betalningar. Se [specen för köp i portalen](kop.md). |
| `/admin/billing` och vägarna under den | Admin | Platserna och vad firman betalar oss. Se [specen för platser och betalning](platser-och-betalning.md). |
| `/admin/verification` och vägarna under den | Admin | Ansökan, dokumenten och att skicka den. Se [specen för granskning och avstängning](granskning.md). |
| `/ops` och vägarna under den | Vår personal | Vår adminvy, bara på `Platform:OpsUrl`. Se [specen för granskning och avstängning](granskning.md). |

Inloggningarna och inbjudningar har gemensamt en gräns på `Login:AttemptsPerMinute` försök i minuten per IP-adress, som standard 10. Svaret är då 429. Webbläsarens adress och protokoll tas från `X-Forwarded-For` och `X-Forwarded-Proto` när anropet kommer från en betrodd proxy, lokalt bara loopback. Annars skulle alla som når tjänsten genom portalen dela samma gräns.

## Sessioner och lösenord

- Traders och administratörer har var sin session, i cookien `prop_trader` respektive `prop_admin`. Vår personal har sin egen, `prop_ops`, på vår adminvys adress. Samma webbläsare kan alltså vara inloggad som trader och som administratör samtidigt, och en inbjudan som används där loggar inte ut administratören.
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
- Plattformen mejlar bekräftelser och inbjudningar till administratörer, en inbjudan till köpare som inte har något lösenord, och traders när firman ber om det i adminpanelen. Traders vars challenge startas via firmans API mejlar firman själv.
- Firman skickar pengarna till tradern själv. Portalen markerar bara utbetalningen som betald.
- Sessioner återkallas inte när ett lösenord byts. En borttagen administratör loggas ändå ut direkt.
- Egen domän med TLS-certifikat sätts upp för hand.
- Equity sparas inte över tid, så grafen visar saldot och bara equity just nu. Historiken kan ligga någon sekund efter kontots siffror.
- Adminpanelens siffror räknas när de läses och jämförs inte med perioden innan.
- Vår personal läggs in i konfigurationen och har ingen tvåstegsinloggning än.

## Tester

- `prop/tests/Prop.Api.Tests/PortalApiTests`: utseende per värdnamn, inbjudan och inloggning, att inbjudan bara fungerar en gång, inte efter 7 dagar och ersätts av en ny, fel lösenord, att traders bara ser sina konton, siffror i realtid och när handelsplattformen inte svarar, bevis vid brott, att sessioner och inbjudningar bara gäller hos sin firma, att adminpanelen bara är för administratörer, adminpanelens flöden, att administratörer bara ser sin firma, att en session överlever en omstart, att gränsen för inloggning räknas per webbläsare bakom portalen, att en administratör och en trader kan vara inloggade samtidigt i samma webbläsare, att konfigurerade traders loggar in med sitt lösenord efter varje start och att reglerna för inloggning går att stänga av.
- `prop/tests/Prop.Api.Tests/PayoutFlowTests`: traderns begäran och administratörens beslut i portalen, och att en trader inte når andras konton eller adminpanelens beslut.
- `prop/tests/Prop.Api.Tests/AdminPanelTests`: översikten med konton per grupp, utbetalningar, försäljning, andelen som klarar, veckor och händelser, sökningen bland kontona på e-post, nummer, referens, challenge och grupp sida för sida, kontots trader med köp, utbetalningar och ordern bakom kontot, mejlet till tradern som inbjudan, som besked och när det inte går iväg, att firman ser historiken för sina egna konton men inte andras, utbetalningskön med den äldsta först och det som betalats innan, och varje challenges siffror utan annullerade konton.
- `prop/tests/Prop.Api.Tests/AdminSettingsTests`: färgerna med texten på accentfärgen, en logga som laddas upp, visas från portalens adress med sina huvuden och tas bort, och loggor som nekas: annat än bilder, SVG med skript, händelser, javascript-länkar eller entiteter, och filer över 1 MB.
- `prop/tests/Prop.Api.Tests/TraderDashboardTests` och `AccountPerformanceTests`: traderns översikt och historik. Se [specen för propfirm-tjänsten](propfirm-tjanst.md).
- `prop/tests/Prop.Api.Tests/OrderFlowTests`: köp i portalen. Se [specen för köp i portalen](kop.md).
- `prop/portal/src/lib/*.test.ts`: traderns översikt (färger efter resultat och golvens avstånd, vad som behöver tradern och i vilken ordning, målen och gränserna, dagar kvar i challengens tidszon, faserna och statusen), grafen (axlarnas värden och tider, steglinjen, närmaste ändring, golvets nivå vid en tid och när det totala golvet ritas), formateringen, platserna, prisstegen och raden om betalningen i adminpanelen, sista dagen att handla och att klara fasen, vilka ordrar firman kan markera och vad köparens ordersida säger, förlopp mot vinstmålet, vilka golv som visas, när knapparna fungerar, vilka beslut som går att fatta om en utbetalning, utbetalningarnas texter, färgerna och att standardfärgerna är de i `globals.css`, förslaget på kort namn och kontrollen av det, challenge-redigeraren fram och tillbaka med tidsgräns och dagar utan en ny affär och beloppen bredvid procenten, adminpanelens texter (det som väntar på firman och i vilken ordning, kontonas status, händelserna, hur länge sedan och när, andelen som klarar och summor per valuta), veckodiagrammets staplar och axel, stegen till live, kontrasten mellan färger och teman, ansökans formulär, ägarnas andelar, granskningens texter, händelserna i historiken, filstorlekar och länderna, och vår adminvys texter (det som väntar på oss, firmornas steg och challenges, händelserna, sena utbetalningar och mejl till administratörerna).
- `prop/portal/e2e`: hela kedjan med handelsplattformen, propfirm-tjänsten och portalen. En konfigurerad trader loggar in med sitt korta lösenord. Administratören startar en challenge från panelen i adminpanelen och skapar en inbjudan på traderns kort, tradern väljer lösenord i samma webbläsare, ser kontot och öppnar terminalen med en länk som handelsplattformen godtar, och administratören avbryter kontot efter dialogen. Tradern ser kontots kort, öppnar kontots sida med målen och reglerna och kommer in i terminalen därifrån. En trader klarar challengen `quick-test-100k` med handel och insättningar på handelsplattformen, ser funded-fasens affär och laddar ner den som CSV-fil, begär en utbetalning i portalen, administratören godkänner funded-kontot och utbetalningen i kön och markerar den som betald med en referens i dialogen, och tradern ser den betald på kontot och på sidan med utbetalningar. En firma registrerar sig på `app.localhost`, hamnar i sin adminpanel på sin egen adress, ser att den är i sandlådan, får sin server, ser stegen till live och startar en challenge, laddar upp en logga, byter varumärkesfärg och gör en egen challenge med en fas i redigeraren. Registreringen finns bara på plattformens adress, och tagna och reserverade korta namn visas medan de skrivs. En besökare köper en challenge med testbetalning och firman ser den betalda ordern, och en inloggad trader köper och kommer direkt till sitt nya kontos sida. En godkänd firma går live med testbetalning efter ett kort som nekas, ser sina platser och sitt kort och köper fler platser. En ny firma fyller i sin ansökan, lägger till ett dokument och betalar handpenningen, vår personal loggar in på vår adminvy, hittar firman bland dem att granska, bockar i en kontroll och ber om en ändring med en färdig mening, firman skickar igen utan ny handpenning och godkänns, går live med handpenningen avdragen, syns bland firmorna som betalar och ser när vi stänger av den. Vår adminvy finns bara på sin egen adress.
