# Genomgång som ny firma: fynd och lösningar

Datum: 2026-10-04. Testat lokalt på `main` (a96a8c2), med tjänsterna som redan körde.

Status: alla 86 punkter är åtgärdade på grenen `walkthrough-fixes` (inte committat än), utom att namnet på produkten (25) och texten i villkoren (21) är våra egna beslut. Det som kommer senare: firmans egen avsändardomän (59), kontrollen av momsnummer mot EU:s register (49), en tjänst för identitetskontroll i stället för firmans checklista (75), roller för administratörer (76) och proxyn som gör certifikat för egna domäner (84). Besluten finns i ADR 0025-0039.

## Så testade jag

Jag startade firman Nordic Edge Capital (`nordic-edge`) från noll och gick hela vägen:

1. Registrering på plattformen och första mötet med adminpanelen.
2. Challenge, pris, betalsätt, logga och färger.
3. Köp i butiken som ny trader, välja lösenord, öppna terminalen och handla.
4. Ansökan med handpenning, granskning och godkännande i vår adminvy.
5. Gå live, starta en challenge åt en trader, klara fasen, godkänna funded-kontot, begära utbetalning, godkänna den och markera den som betald.
6. En trader som bryter mot förlustgränsen.
7. Mobilstorlek, ljust tema och alla mejl i Mailpit.

Helhetsintryck: hela kedjan fungerar, från registrering till utbetalning, och sidorna ser genomarbetade ut. Det som skaver ligger mest mellan stegen: inloggning, mejl, terminalen och att koppla betalning.

## Det viktigaste först

| # | Problem | Lösning |
|---|---|---|
| 1 | Det finns ingen "Glömt lösenord" någonstans. En ägare som glömmer sitt lösenord är utelåst, och en trader måste be firman om en ny länk. | "Glömt lösenord?" på traderns, administratörens och vår inloggning, med en länk på mejlen som gäller en timme. |
| 2 | Terminalens egen inloggning går inte att använda för firmans traders. Portalen skapar deras konto i terminalen med ett slumpat lösenord, så e-post och lösenord fungerar aldrig där. Bokmärker tradern terminalen, eller går sessionen ut, sitter den fast. | Terminalens inloggning säger "Logga in via din firmas portal" med en knapp tillbaka. När sessionen går ut skickas tradern till portalen, som ger en ny inloggningslänk direkt. |
| 3 | Nästan inga mejl om det som händer. Firman får inget mejl när något säljs, när en trader klarat och väntar på godkännande eller när en utbetalning begärs. Tradern får inget när den klarat en fas, fått funded, brutit en regel, fått en utbetalning godkänd eller betald, eller innan kontot stängs för inaktivitet. | Ett grundpaket med mejl, i firmans namn och med dess logga, som firman kan slå av och på under en ny sida "Notiser". |
| 4 | I produktion slutar butiken sälja när firman går live med testbetalning. Steget säger "koppla Stripe innan du går live", men inget stoppar eller varnar när man betalar för att gå live, och översikten säger sedan inget om att butiken inte tar betalt. Lokalt syns det inte, eftersom testbetalning är påslaget för live-firmor i utveckling. | Gå live-sidan visar en kontroll "Butiken tar betalt: nej" med knappen Koppla Stripe, eller valet "Vi säljer inte i portalen". Översikten får en rad under "Needs you" när butiken inte tar betalt. |
| 5 | Tradern kan inte ange vart pengarna ska, och firman ser ingenstans vart den ska skicka en utbetalning. | Tradern sparar utbetalningssätt (bankkonto eller kryptoadress) i portalen, och firman ser det på utbetalningen. |
| 6 | Stripe är krångligt att koppla: hemlig nyckel, webhook-hemlighet och en webhook med sex händelser som firman lägger upp själv i Stripe. | Fråga bara efter den hemliga nyckeln och skapa webhooken åt firman med Stripes API (då får vi hemligheten tillbaka). Bäst på sikt: knappen "Koppla Stripe" med Stripe Connect. Lägg till "Testa kopplingen". |
| 7 | Standardfärgen klarar inte sidans egen kontrastkontroll: en ny firma ser varningen "Button text is hard to read" direkt. Fyra av fem färgförslag klarar inte heller kraven med vit text. | Byt standard till `#2563eb`, välj vit eller mörk text automatiskt efter bäst kontrast, och byt ut förslagen som inte klarar sig. |
| 8 | Siffrorna efter live räknar med testköpet från sandlådan: översikten (och vår adminvy) säger "Sales, last 30 days 499.00 USD" fast inget riktigt sålts. | Räkna inte testbetalningar och köp från sandlådan i firmans siffror. |
| 9 | Plattformens adress går rakt till registreringen. Det finns ingen sida om vad man får eller vad det kostar, och ingen inloggning för en firma som redan finns, eftersom inloggningen ligger på firmans egen adress. | En enkel startsida med vad som ingår, priset och "Starta gratis sandlåda", och "Logga in" som frågar efter e-posten och skickar dig till din firma. |
| 10 | Firman kan inte välja sina handelsvillkor. Hävstång (1:100), spread-påslag, kommission (3,50 per lot) och symboler (bara fyra) kommer från vår konfiguration. | En sida "Handelsvillkor" i adminpanelen, per challenge eller för hela firman, och fler symboler (index, fler valutapar, krypto). Det passar vårt löfte om öppen simulering. |

## Buggar

| # | Var | Bugg | Lösning |
|---|---|---|---|
| 11 | Checkout | "Saved." visas aldrig när man byter betalsätt. Formuläret laddas om när inställningarna kommer tillbaka och tappar meddelandet. | Visa bekräftelsen så att den överlever omladdningen, eller ladda inte om formuläret. |
| 12 | Verification | Felet "Fill in the company's legal name." står kvar i rött efter att fältet är ifyllt och sparat. | Rensa felet när formuläret ändras eller sparas. |
| 13 | Terminalen | Firman syns inte i terminalens serverlista fast den har gått live, trots att ADR 0016 säger att den ska göra det. Inget sätter firman som listad. | Lista firman när den går live, eller ta bort den gemensamma listan helt (se 2). |
| 14 | Översikten | En helt ny firma får knappen "Continue the application" fast inget är påbörjat. | "Start the application" tills något är sparat. |
| 15 | Bannern | Bannern säger "trading server is being set up" medan listan redan säger att servern är klar. Bannern uppdateras först vid omladdning. | Uppdatera bannern medan servern skapas. |
| 16 | Orderns sida | Säger "Your challenge is starting." även när kontot redan handlas. | "Your challenge has started" och en knapp till kontot. |
| 17 | Registreringen | Challengen som skapas heter "Two-step 100000 USD" utan tusentalsavgränsare. | Döp den till "Two-step 100K". |
| 18 | Plan and billing | Med färre än 25 platser visas felet, men samtidigt totalsumman och betalknappen. | Dölj summan och lås knappen medan antalet är fel. |
| 19 | Terminalen | Historiken visar kommission 3,50 per affär, men 7,00 drogs (båda hållen). Portalen visar 7,00. | Visa hela kommissionen per affär. |
| 20 | Inbjudan | En redan använd länk visar lösenordsformuläret, och felet kommer först efter att man skrivit lösenordet två gånger. | Kolla länken när sidan öppnas. Har tradern redan ett lösenord: "Du har redan ett lösenord. Logga in". |

## Registrering och första mötet

| # | Problem | Lösning |
|---|---|---|
| 21 | Villkoren och personuppgiftsbiträdesavtalet är inte länkar, och "(version draft-2026-10-03)" syns för kunden. | Länka till riktiga villkor och dölj versionen. |
| 22 | "It is also your trading server" är internt språk. | "Det här blir din portals adress. Den går inte att byta, men du kan lägga till en egen domän senare." |
| 23 | "That name is taken." utan hjälp. | Föreslå två eller tre lediga varianter. |
| 24 | Inget välkomstmejl efter registreringen. | Ett mejl med länk till adminpanelen och de tre första stegen. |
| 25 | Produkten heter fortfarande "Prop platform" på registreringen, i mejlen och i vår vy. | Bestäm namnet före lansering. |
| 26 | Firman landar på en lista med åtta steg på sex olika sidor. | En kort guide direkt efter registreringen: logga och färg, första challengen med pris, betalsätt. Sedan listan för resten. |

## Challenges

| # | Problem | Lösning |
|---|---|---|
| 27 | Priset ligger längst ner i en lång redigerare, och steget "Set a price" landar på listan. | Pris och "Till salu" direkt på challenge-kortet. |
| 28 | "For sale in your shop" är ikryssat från början, också för en helt ny challenge och utan pris. Min testchallenge låg ute till försäljning direkt. | Nya challenges är utkast tills firman slår på försäljningen, och rutan går bara att kryssa när ett pris finns. |
| 29 | "Days to pass" (en tidsgräns) och "Trading days a payout" är svåra att förstå. Förhandsvisningen har raden "To pass - - -". | "Tidsgräns (dagar)" och "Minsta antal handelsdagar mellan utbetalningar". |
| 30 | En ny challenge utgår alltid från tvåstegsmallen. | Välj mall: ett steg, två steg, tre steg eller direkt funded, och skapa flera kontostorlekar på en gång. |
| 31 | "Copy" kan läsas som kopiera till urklipp. | "Duplicate". |
| 32 | Kopians förhandsvisning skriver "499 USD" medan resten skriver "499.00 USD". | Samma format överallt. |

## Checkout och integrationer

| # | Problem | Lösning |
|---|---|---|
| 33 | Butikens adress står som vanlig text. | Länk och kopieringsknapp. |
| 34 | Villkorens adress frågas på två ställen: "terms for buyers" under Checkout och "terms for traders" i ansökan. | Ett fält, sparat en gång och använt på båda ställena. |
| 35 | Testbetalning räknas aldrig som klart i stegen, och det står inte hur man blir klar i sandlådan utan Stripe. | Säg rakt ut: "Testbetalning räcker för att prova. Innan live behöver du Stripe eller egen betalsida." |
| 36 | API:t visas bara som `/api/firm/v1`, utan full adress, dokumentation eller exempel. | Full adress med kopieringsknapp, länk till dokumentationen och ett färdigt exempel. |
| 37 | Webhooks saknar lista över händelser, testknapp och logg över vad som skickats. | Lista händelserna, knappen "Skicka testhändelse" och de senaste leveranserna med status. |
| 38 | Kontovalutan är alltid USD. | Låt firman välja valuta vid registreringen eller per challenge. |

## Portalens utseende

| # | Problem | Lösning |
|---|---|---|
| 39 | Loggan sparas och syns för traders direkt, medan färgerna kräver "Save design". Sidan säger att man ser ändringarna innan man sparar. | Loggan blir en del av ändringarna som sparas, eller så står det tydligt att den sparas direkt. |
| 40 | En ljus logga försvinner på det ljusa temat (syns i förhandsvisningen), utan varning. | Ladda upp en logga för ljus och en för mörk bakgrund, eller varna när kontrasten är låg. |
| 41 | Med logga trycks "Admin · Sandbox" ihop på två rader i sidomenyn. | Lägg rollen och läget under loggan. |
| 42 | "Back to the portal's own" är oklart. | "Återställ alla färger". |
| 43 | På mobilen ligger förhandsvisningen längst ner, efter alla färger. | En knapp som växlar mellan inställningar och förhandsvisning. |

## Ansökan, granskning och att gå live

| # | Problem | Lösning |
|---|---|---|
| 44 | Samma sak heter Verification (menyn), review (steget) och application (knappen), och sedan finns Plan and billing som en egen sida för samma resa. | En sida "Gå live" i sandlådan med fyra steg: uppgifter, handpenning, vårt svar, platser och betalning. Efter live heter den Plan and billing. |
| 45 | Bara det första fältet som saknas visas, och samma text står två gånger. | Markera alla fält som saknas, och en lista vid knappen. |
| 46 | Dokument laddas upp med webbläsarens egen filknapp. | Samma släppyta som för loggan. |
| 47 | Handpenningen "not paid back, also if your firm is not approved" låter hårt, och inget kvitto kommer. | Förklara varför (vi granskar för hand), skicka kvitto och "vi har fått din ansökan" på mejlen. |
| 48 | Plan and billing visar röd feltext innan firman har gjort något. | En neutral ruta med stegen och var firman är. Rött bara för riktiga fel. |
| 49 | Ingen moms visas, fast bolaget är svenskt (25 %), och inga fakturor eller kvitton finns att ladda ner. | Moms efter land och momsnummer, och en PDF-faktura för varje dragning. |
| 50 | Ingen bekräftelse innan live om att kontona i sandlådan avslutas, och de traders som berörs får inget mejl. | En sista ruta före betalningen: "1 testkonto avslutas", butikens status, design och villkor. |
| 51 | Inget mejl när firman är live: kvitto och vad som händer nu. | Mejl med kvitto och nästa steg. |
| 52 | Automatisk utökning säger inte vad en utökning kostar. | "Lägger till 10 platser för 50 USD i månaden, 16,13 USD för resten av oktober." |
| 53 | Dragningarnas nummer (#1003, #1004) räknas för alla firmor tillsammans. | En nummerserie per firma, vilket fakturorna ändå behöver. |
| 54 | Ansökan försvinner ur menyn efter live, så firman kan inte se sina bolagsuppgifter. | Visa dem under en flik i Plan and billing eller Settings. |

## Butiken och traderns väg in

| # | Problem | Lösning |
|---|---|---|
| 55 | Portalens förstasida är inloggningen för besökare. Butiken nås via en liten länk. | Skicka besökare till butiken, med "Logga in" uppe till höger. |
| 56 | Traderns inloggning visar länken "Admin login" för alla. | Ta bort den. Administratörer går till /admin. |
| 57 | Butiken frågar bara efter e-post. Firman får aldrig namn eller land, och traderkortet visar bara e-posten. | Fråga efter namn och land vid köpet. |
| 58 | Efter betalningen måste köparen gå till sin mejl för att välja lösenord innan den ser något. | Låt köparen välja lösenord direkt på tacksidan och bekräfta e-posten efteråt. |
| 59 | Mejlen till traders är ren text utan logga, skickas från vår adress `no-reply@prop-platform` och svar går ingenstans. | Mejl med firmans logga och färg, firmans supportadress som svarsadress, och senare firmans egen avsändardomän. |
| 60 | Butiken är ett textblock per challenge, som blir svårläst med flera storlekar. | En pristabell med kontostorlekarna som kolumner och reglerna som rader. |
| 61 | Tom översikt: "No account is trading right now. The figures update every few seconds." | "Du har ingen aktiv challenge" och knappen "Köp en challenge". |
| 62 | "Stay active: 31 days left" när regeln säger 30 dagar (dagen i dag räknas med). | "Handla senast 3 nov (30 dagar)". |
| 63 | På mobilen ligger menyn bakom traderns initialer, och tabellen med avslutade konton skärs av. | En tydlig menyknapp och kort i stället för tabell på mobilen. |

## Terminalen

| # | Problem | Lösning |
|---|---|---|
| 64 | Terminalen heter "Trading terminal" och kontot heter `nordic-edge-1001-1`, medan portalen säger #1001. Firman syns bara längst ner. | Ge terminalen sitt riktiga namn, visa firmans namn och logga vid kontot och samma kontonummer som portalen: "Nordic Edge Capital · #1001 Two-step 100K · Phase 1". |
| 65 | Ingen väg tillbaka till portalen. | Länk "Tillbaka till Nordic Edge Capital". |
| 66 | Vinstmålet syns inte, bara golven. | Visa hur långt det är kvar till målet i kontoraden. |
| 67 | Olika ord: "Daily floor" i terminalen, "Daily loss room" och "Daily loss limit" i portalen. | Samma ord på båda ställena. |
| 68 | Märket "Live" betyder att priserna kommer, men kan läsas som ett riktigt konto. | "Priser i realtid" eller en grön prick utan ord. |
| 69 | Tider i olika zoner: historiken i lokal tid, statusraden och grafen i UTC, och handelsdagen i Europe/Stockholm. | En tidszon, handelsdagens, och skriv ut den. |

## Konton, utbetalningar och team i adminpanelen

| # | Problem | Lösning |
|---|---|---|
| 70 | Regelloggen visar motorns interna namn: "ChallengeStarted", "OpenAccountRequested", "FloorRequested", "AccountUpdated", och kolumnen Evidence är tom. | Vanliga meningar, till exempel "Förlustgränserna sattes till 95 000 och 90 000", och de tekniska namnen bakom "Visa detaljer". |
| 71 | "Email about this challenge" skickar direkt med ett klick, utan att visa vad som skickas. | Visa mejlet och be om bekräftelse. |
| 72 | Fyra bekräftelser använder webbläsarens egen ruta (begär utbetalning, ny API-nyckel, ny webhook-hemlighet, ta bort administratör), resten appens egen. | Appens egen dialog överallt. |
| 73 | Referensen vid "Mark as paid" visas för tradern, men det står inte. | "Referens (tradern ser den)". |
| 74 | Avslag på en utbetalning tar alltid vinsten. Firman kan inte avslå för att KYC saknas och låta tradern behålla vinsten. | Två val vid avslag: "Avslå, vinsten är förlorad" och "Avslå, lägg tillbaka vinsten". |
| 75 | Godkännandet av funded och utbetalningar nämner KYC, men plattformen har inget KYC-steg. | Först en checklista per trader (ID kontrollerat, adress kontrollerad), senare en koppling till till exempel Sumsub eller Veriff. |
| 76 | En inbjudan till en administratör går inte att skicka igen eller dra tillbaka, och alla administratörer får göra allt, också betala. | "Skicka igen" och "Dra tillbaka", och senare roller: ägare, administratör och support. |
| 77 | På mobilen visar kontolistan bara nummer och e-post, inte status. | Kort med status och saldo. |
| 78 | Sidorna har två olika bredder: Checkout, Integrations, Team, Billing och Verification är smala och centrerade, de andra breda. Rubriken hoppar när man byter sida. | Samma bredd och vänsterkant på alla sidor. |

## Brott mot regler

| # | Problem | Lösning |
|---|---|---|
| 79 | I terminalen syns brottet bara som en liten röd text "Disabled" under kontonumret. Ingen förklaring, "Buy filled." står kvar och golvet visar "-49.00 left". | En tydlig ruta: "Din challenge är slut. Dagliga förlustgränsen bröts 17:38, equity 9 482,50 under 9 500,00", med länk till kontot i portalen. Dölj negativa värden. |
| 80 | Portalen förklarar brottet bra, men inte varför saldot slutade under gränsen (9 451 mot 9 500). | En mening om att positionen stängdes till nästa pris och att kommissionen för att stänga drogs, och priserna i ögonblicket, som terminalen redan har. |
| 81 | Ett misslyckat konto erbjuder inget nytt försök, och visar "Profit target: -549.00 of 10.00". | Knappen "Försök igen" till butiken, gärna med firmans rabatt för nytt försök. Dölj framsteget mot målet när kontot är avslutat. |
| 82 | Terminalens händelser använder kodord som "(EquityFloor)". | Vanliga ord: "stängd av förlustgränsen". |

## Saknas för en riktig lansering

Inte buggar, men sådant en firma frågar efter innan den vågar starta. Det mesta finns redan i planen.

| # | Saknas | Varför det spelar roll |
|---|---|---|
| 83 | Rabattkoder | Nästan alla propfirms säljer med rabattkoder och kampanjer. Utan dem får firman sköta försäljningen utanför portalen. |
| 84 | Egen domän (fas 9b) | Firmans portal ligger på vår underdomän tills dess. |
| 85 | Konsistensregel för funded-konton | Vanlig regel som firmor räknar med, redan uppskriven som uppföljning. |
| 86 | Diplom för klarad challenge och utbetalning | Firmor använder dem i sin marknadsföring. Enkelt att göra som en bild att ladda ner. |

## Designförslag

Sju ändringar som gör resan enklare. Jag gör gärna en designskiss för dem, som för adminpanelen.

1. **Startsida för plattformen.** "Starta din propfirm i dag", tre steg (registrera, prova i sandlådan, gå live), priset rakt ut, knapparna "Starta gratis" och "Logga in".
2. **Kom igång-guide efter registreringen.** Tre korta skärmar: logga och färg med förhandsvisning, första challengen från en mall med pris, och hur traders betalar. Sist "Prova som trader" med en knapp som öppnar butiken.
3. **En sida för att gå live.** Ersätter Verification och första delen av Plan and billing i sandlådan. Överst en kontrollista (butiken tar betalt, villkor, design, godkänd av oss), sedan uppgifter, handpenning, vårt svar och betalningen i samma flöde.
4. **Challenge-kort med pris och på/av-knapp.** Firman sätter pris och slår på försäljningen direkt i listan. Redigeraren behövs bara för reglerna.
5. **Butiken som pristabell.** Kontostorlekar som kolumner, regler som rader och en köpknapp per storlek, med samma utseende som resten av portalen.
6. **Terminalens kontorad.** Firmans logga och namn, kontonummer och challenge som i portalen, en stapel mot vinstmålet och en länk tillbaka till portalen.
7. **Notiser.** En klocka i adminpanelen med det som väntar, och samma händelser på mejl till firman och tradern, med en sida där firman väljer vilka mejl som skickas.
