# Genomgång av UI och UX: utseende, tydlighet och känsla

Datum: 2026-10-06. Testat lokalt på `main` (2c87ef5), med tjänsterna som redan körde. Ingen kod är ändrad och inga paket är installerade.

Status: buggarna 11-32 är åtgärdade och committade (`bd593cc`). Resten av punkterna och designförslagen D1-D12 är byggda på grenen `ui-redesign` (inte committat), med Kronants designsystem i `shared/web/design` (ADR 0047). Val som gjordes på vägen: saldodiagrammet behåller vårt eget bibliotek, som går att läsa med tangentbordet, och låter skalan följa saldot med gränserna som etiketter i kanten (8); "Change password" i traderns meny mejlar en länk i stället för ett formulär (96); köpknappen på en telefon är en rad i nederkanten som bara visas när kortets egen knapp inte syns (D6); firmans utbetalningar i butiken är ett val under Checkout som firman sätter på själv, avstängt från början (D6). Inte gjort, eftersom det kräver beslut: en QR-kod som verifierar diplomen (95, kräver en publik sida med traderns namn), flaggor i landvalet (D5, Windows visar inga flagg-emoji), och organisationsnummer och kontaktadress i plattformens sidfot (36).

Fokus: hur det ser ut, om informationen är tydlig och rätt, och hur det kan bli mer inbjudande, med djup och rörelse och utan att se AI-genererat ut.

## Så testade jag

1. Startade firman **Aurora Funded** (`aurora-funded`) från noll på plattformens framsida, gick igenom guiden, varje sida i adminpanelen och Go live-sidan.
2. Köpte en challenge i firmans egen shop som tradern **Maja Lind**, med testbetalning och en felaktig rabattkod, valde lösenord och hamnade på kontot.
3. Öppnade terminalen från portalen, köpte EURUSD med stop loss, sålde XAUUSD, stängde en affär, och tittade på historik, händelser och högerklicksmenyn.
4. Skrev ett supportärende som tradern och svarade som firman.
5. Loggade in som traders hos den live-firman **Nordic Edge** för lägena funded med betald utbetalning (sara), misslyckad (olle) och avbruten av firman (erik), och som Nordic Edges ägare.
6. Läste alla mejl i Mailpit.
7. Allt i datorbredd (1440 px) och mobilbredd (390 px).

Testdata kvar i dev-databasen: firman `aurora-funded` med ägaren `owner@aurorafunded.test` / `aurora`, som också är trader med lösenordet `maja`. Konto #1001 har en öppen säljposition i XAUUSD, en order är obetald och supportärende #1 är besvarat.

## Helhetsintryck

Det som är bra och ska behållas:

- Texterna är ärliga och exakta. Förklaringen när en challenge misslyckas (equity, gränsen, stängningspriset och kommissionen) är bättre än hos de flesta konkurrenter.
- "Steps to live" på översikten, förhandsvisningen med kontrastkontroll på Portal design och fasstegen överst på kontot är genomtänkta.
- Terminalen är den mest färdiga delen: markeringar i diagrammet, stop loss i belopp, högerklicksmenyn och kontoraden med gränserna.
- Bra stöd för skärmläsare nästan överallt.

Varför det ser AI-genererat ut:

- **Typsnittet** Geist och Geist Mono följer med varje nytt Next.js-projekt och varje v0-bygge.
- **Färgen** `#2563eb` är Tailwinds standardblå. Terminalen har en annan, lika vanlig blå och en annan bakgrund.
- **Ytorna** är platta mörkblå kort med 1 px kant och samma rundning, ofta kort i kort. Portalen har inte en enda skugga.
- **Mönstret** "rutnät av likadana kort med fet rubrik och grå text under" (framsidans "What you get" och "How it works" är skolexemplet).
- **Siffrorna** står i monospace med överstrukna nollor, så belopp och räknare ser ut som kod.
- **Bilder** saknas helt: inga produktbilder, illustrationer eller ikoner med karaktär, och Kronant har ingen logga. Terminalen har en vanlig "trending up"-ikon.
- **Rörelse** finns nästan inte: hela portalen har 5 övergångar (bara opacitet) och inga animationer. Laddning är texten "Loading..." på 83 ställen.
- **Varningar** är samma orange ruta överallt, ibland tre på rad.
- **Text** överallt: varje sida börjar med en grå förklarande mening, och små grå versaletiketter ("TRADING", "YOUR FIRM").

## Det viktigaste först

| # | Problem | Förslag |
|---|---|---|
| 1 | Shoppen är en lång tabell, inte en butik. Varje challenge är en hög rad-för-rad-lista med en liten "Buy" längst ner till höger. Inget jämför storlekar, priset syns knappt och inget berättar hur vägen till funded och utbetalning ser ut. Det är firmans viktigaste sida för att tjäna pengar. | Bygg om shoppen som en riktig butik, se D6. |
| 2 | Inget djup och ingen rörelse. Allt ligger på samma platta nivå, inget reagerar när man håller musen över eller när en siffra ändras, och laddning är bara text. | Ett system för ytor och skuggor (D2) och ett för rörelse (D3), som används på alla sidor. |
| 3 | Kronant har inget eget utseende. Ingen logga, standardtypsnitt, standardfärg, och terminalen ser ut som ett annat företag. | En egen identitet för Kronant Prop och Kronant Trader (D1, D4, D5, D11). |
| 4 | Terminalen går inte att använda i mobilen. Diagrammet och handelspanelen hamnar utanför skärmen, kontoraden klipps ("Accou", "#100"). Portalen visar ändå "Open terminal" i mobilen. | En egen mobillayout för terminalen (D10). |
| 5 | Formulärens egna kontroller är ljusa i den mörka portalen: vita kryssrutor, en svart kalenderikon på mörk botten i rabattkodens datumfält, ljusa rullgardinslistor och rullister. | Se bugg 11. |
| 6 | Traderns viktigaste fråga, "hur mycket får jag förlora idag?", visas bara som en mening och ordet "Kept". Vinstmålet har en stapel, men förlustgränserna har ingen. | Mätare för utrymmet kvar till daglig och total förlustgräns, som byter färg när gränsen närmar sig (D7). |
| 7 | De stora ögonblicken firas inte. Betalningen, klarad fas, funded och betald utbetalning är en grön textrad eller en liten etikett. | Riktiga ögonblick med animation och nästa steg (D8). |
| 8 | Saldodiagrammet ser trasigt ut på nya konton: tidsaxeln går i sekunder (02:08:20, 02:08:35), linjen börjar mitt i rutan och halva diagrammet är en stor mörkblå yta. Efter några affärer syns rörelser på ±100 inte alls på en skala från 90 000 till 110 000. | Visa ett tomt läge ("Diagrammet börjar med din första affär") tills det finns affärer, låt skalan följa saldot med gränserna som band i kanten, och använd samma diagrambibliotek som terminalen. |
| 9 | Fel sida och ikon: en okänd adress ger Next.js vita standardsida "404 This page could not be found" utan firmans namn eller väg tillbaka, och varje firmas portal har Next.js standardikon i fliken. | Se buggar 12 och 13. |
| 10 | Go live-formuläret möter firman med sju orange felmeddelanden innan den skrivit något, och samma sju upprepas i en lista längst ner. Det känns som att få skäll innan man börjat. | Se bugg 24. |

## Buggar och fel

| # | Var | Bugg | Förslag |
|---|---|---|---|
| 11 | Hela portalen | `color-scheme: dark` saknas i portalens `globals.css` (terminalen har det). Därför ritar webbläsaren kryssrutor, radioknappar, datumfält, rullgardinslistor och rullister i ljust läge. Datumfältet på Discount codes har en svart ikon på mörk botten. | Sätt `color-scheme` efter temat: `dark` för Dark och Navy, `light` för Light. Byt på sikt till egna kontroller (D2). |
| 12 | Alla adresser som inte finns | Ingen `not-found.tsx` och ingen `error.tsx` i portalen. 404 visar Next.js vita standardsida, och ett fel på en sida visar Next.js standardfel. | En egen 404 och felsida i firmans färger, med logga och länk tillbaka. |
| 13 | Fliken i webbläsaren | Portalen och terminalen har Next.js standard-`favicon.ico` (samma fil). Varje firmas traders ser Next.js ikon i stället för firmans. | Använd firmans logga som ikon i portalen och Kronants ikon på plattformen och i terminalen. |
| 14 | Terminalen i mobilen | Layouten är bara byggd för dator, se punkt 4. | D10. |
| 15 | Initialer överallt | Initialerna i rundeln kommer alltid från e-posten (`initials(me.email)`), även när namnet finns. Maja Lind blir "OW" (från owner@) i traderns meny, i adminmenyn och på trader-kortet. | Använd namnet när det finns, annars e-posten. |
| 16 | Mejl till firman | "New support ticket #1 from owner@aurorafunded.test" och "owner@aurorafunded.test bought Two-step 100K" använder e-posten fast namnet Maja Lind finns. | Namn först, e-post inom parentes. |
| 17 | Certifikat | När tradern saknar namn står e-postadressen som namn på certifikatet ("sara@nordicedge.test"), med stor serif-text. Det ser oprofessionellt ut när det delas. | Be om visningsnamn innan första certifikatet, eller visa ett formulär "Ditt namn på certifikatet". |
| 18 | Guiden, steg 1 | Under loggan står "The colors wait for Save design" (text från Portal design), medan samma sida säger att färgen sparas direkt. | Egen text i guiden, utan meningen om Save design. |
| 19 | Regeltabellen | Ett misslyckat konto visar "Phase 1 · now" i regeltabellen, fast kontot är avslutat. | Visa "now" bara när kontot handlas, annars "ended" eller inget. |
| 20 | Terminalen mot portalen | Samma affär har två olika resultat: terminalens historik visar "Profit +8.00" (brutto, kommission 7.00 i egen kolumn), portalen visar "+1.00 after 7.00 commission" (netto). | Visa netto på båda ställena med samma ord, och brutto och kommission som detaljer. |
| 21 | Traderns konto | Vinstmålet räknar saldot och visar "-2.50 of 10,000.00", samtidigt som "Today" och "This stage" visar +13.50 i grönt (equity med öppen affär). Kontot ser både plus och minus ut. | Skriv att målet räknar stängda affärer, och visa equity som en ljus markering på samma stapel. |
| 22 | Sandlådans översikt | "Open test accounts 2 of 10" när bara ett konto finns. Den andra platsen hålls av en obetald order från ett avbrutet köp, men det står inte. Live-översikten förklarar det ("Held for orders waiting for payment"), sandlådans ruta gör det inte. | Samma uppdelning i sandlådan: öppna, väntar på betalning, lediga. |
| 23 | Granskningstid | Framsidan säger "usually within a few working days", översikten och Go live säger "usually within a day". | En och samma tid, helst från en inställning. |
| 24 | Go live | Alla obligatoriska fält visas med orange ram och felmeddelande innan firman skrivit något, och samma lista upprepas längst ner. | Visa fel först när fältet lämnats eller när firman trycker på knappen. Behåll listan "Det här saknas" vid knappen, men bara då. |
| 25 | Go live, stegen | Både "Deposit" och "Our answer" har statusen "Next". | "Next" bara på steget efter det aktuella, "Later" på resten. |
| 26 | Shoppen | Efter "Buy" rullar sidan bara en bit. Formuläret och "Pay" hamnar under kanten, så det ser ut som att inget hände. I mobilen är det värre. | Öppna köpet som ett eget steg eller en sidopanel (D6). Tills dess: rulla till formuläret och sätt fokus på e-postfältet. |
| 27 | Live-översiktens diagram | "Sales and payouts per week" visar tolv veckor tillbaka till 20 juli, långt innan firman fanns, och utbetalningar ritas i grått, som om de vore avstängda. | Börja vid firmans första vecka och ge utbetalningar en egen färg. |
| 28 | Plan and billing | "Next payment 625.00 USD" är troligen 500 USD plus 25 % moms, men det står inte, och resten av sidan säger "Prices are excluding VAT". | Skriv "625.00 USD inkl. moms (500.00 + 125.00)". |
| 29 | Portal design, ljust tema | Med Light valt försvinner Nordic Edges logga (vit text) i förhandsvisningen, och ingen varning syns där. | Varna direkt vid temavalet, och låt firman ladda upp en logga för ljus och en för mörk bakgrund. |
| 30 | Traderns sidor | Sidbredden hoppar: Your accounts och Payouts börjar vid vänsterkanten av sidhuvudet, medan New ticket, ärendet och Your identity är smala och centrerade. Rubriken flyttar sig i sidled mellan sidorna. | En gemensam vänsterkant. Smala formulär får ligga i den vänstra delen av samma bredd. |
| 31 | Terminalen | Konsolen varnar att firmans logga är sidans största bild och borde laddas direkt (`loading="eager"`). | Ladda loggan direkt i terminalens sidhuvud. |
| 32 | Ordning för köp och konto | Ordern heter "#1002" och kontot "#1001". Samma tecken för två olika nummerserier gör att tradern blandar ihop dem. | "Order 1002" och "Konto #1001", eller ett prefix per serie. |

## Plattformens framsida, registrering och inloggning

| # | Problem | Förslag |
|---|---|---|
| 33 | Framsidan är bara text. Högra halvan av första skärmen är tom, och inget visar hur portalen eller terminalen ser ut. | Visa produkten: en levande bild av portalen och terminalen med siffror som tickar, lite vinklad med djup (D12). |
| 34 | Priset "500.00 USD per month" står stort, medan startavgiften på 700 USD står i liten text under. Det kan kännas som att något döljs. | "500 USD i månaden + 700 USD en gång", och en kalkylator där man drar fram antal öppna challenges och ser kostnaden. |
| 35 | Decimaler i marknadsföring ("500.00 USD", "4.00 USD") ser ut som en faktura. | Hela belopp utan decimaler på framsidan, gärna "$500". |
| 36 | Ingen sidfot: inget företagsnamn (Ludware AB), inga villkor, ingen integritetspolicy, ingen kontakt. | Sidfot med företag, organisationsnummer, villkor, integritet och kontakt. |
| 37 | Inget som bygger förtroende: inga vanliga frågor, ingen jämförelse, inget om säkerhet eller var data lagras. | FAQ, en jämförelse med konkurrenternas priser (finns i `reports/Konkurrenters priser och vårt pris.md`), och "EU-data, kryptering, granskning av varje firma". |
| 38 | Sidhuvudet har bara "Log in". | "Start free sandbox" även i sidhuvudet, som följer med när man rullar. |
| 39 | I mobilen har de två knapparna överst olika bredd. | Lika breda knappar i mobilen. |
| 40 | Registreringen säger "Kronant Prop" två gånger ("Kronant Prop" och "Start your prop firm on Kronant Prop") och har ingen logga eller väg tillbaka till framsidan. | Logga överst som länkar hem, en rubrik. |
| 41 | Lösenordsfältet har ingen visa/dölj-knapp. | Ögonknapp på alla lösenordsfält. |
| 42 | Villkoren på registreringen är bara länkar när en adress är inställd. Utan den godkänner firman text den inte kan läsa. | Gör villkorsadressen obligatorisk utanför utveckling. |
| 43 | Inloggningen på plattformen visar en adress med `http://` och port i monospace mitt i texten. | "Varje firma har en egen adress" utan teknisk adress, eller som exempel `dinfirma.kronant.app`. |

## Guiden och adminpanelen

| # | Problem | Förslag |
|---|---|---|
| 44 | Sandlådans orange rad överst talar om firman i tredje person för firmans egen ägare: "This firm is trying the platform, and nothing here is real." | Till administratörer: "Din firma är i sandlådan. Inget här är på riktigt." med länk till Go live. Till traders: nuvarande text. Gör raden diskretare än en varning, se 45. |
| 45 | Orange används både för "sandlåda", "varning" och "fel", så allt känns som larm. | En lugn färg för information och sandlåda, orange bara för det man måste göra något åt, rött för fel. |
| 46 | Guidens stapel med fyra delar har inga namn. | Namn under varje del: Logga och färg, Pris, Betalning, Prova som trader. |
| 47 | Steg 2 kräver "Save price" och sedan "Next". | Låt "Next" spara. |
| 48 | Steg 3 har två blå huvudknappar bredvid varandra, och texten i rutan upprepar meningen under rubriken. | En huvudknapp, kortare text. |
| 49 | Steg 4 skickar firman till shoppen i en ny flik, och sedan händer inget i guiden. | Visa en checklista som bockas av av sig själv: köpt, lösenord valt, terminalen öppnad, första affären. |
| 50 | Färgerna i guiden och på Portal design har inga namn som syns. | Namn när man håller musen över eller under den valda färgen. |
| 51 | Efter uppladdning visas både loggan och en ny stor uppladdningsruta under den. | En ruta: loggan med "Byt" och "Ta bort". |
| 52 | Menyn har 15 punkter, varav 9 är inställningar. | Behåll det dagliga i menyn (Overview, Accounts, Payouts, Support, Orders, Challenges, Discount codes) och samla resten under "Settings" med flikar (Design, Domain, Checkout, Trading conditions, KYC, Notifications, Integrations, Team). |
| 53 | Det finns ingen snabbsökning. En firma med hundratals traders letar via Accounts. | Ctrl+K: sök trader, konto, order eller ärende från vilken sida som helst. |
| 54 | Sandlådans översikt har ingen "Needs you". Det nya supportärendet syns bara som en siffra i menyn. | "Needs you" även i sandlådan. |
| 55 | Klara steg tar lika stor plats som det som ska göras, så nästa steg hamnar längst ner. | Fäll ihop klara steg ("6 klara"), lyft nästa steg överst, gärna med en ring som visar hur långt firman kommit. |
| 56 | Nyckeltalen på live-översikten är bara en siffra. | Liten kurva för de senaste veckorna och förändring mot förra perioden. |
| 57 | Räknarna ("5 of 9 done", "0 of 10", flikarnas 0) står i monospace med överstrukna nollor. | Vanliga siffror i samma typsnitt som texten, med tabellsiffror (D4). |
| 58 | Tomma lägen är en grå mening ("No accounts yet..."). | Ikon, en mening och knapparna (Start a challenge, Open your shop) direkt i det tomma läget. |
| 59 | Kontosidan i admin trycker ihop målen till tre smala kolumner bredvid trader-kortet, så rubriker bryts ("Minimum trading / days", "Stay / active"), och under dem blir en stor tom yta. | Trader-kortet som en rad överst eller en sidopanel, målen i full bredd. |
| 60 | "Cancel account" är en röd knapp högst upp på kontosidan. | Lägg farliga val i en meny "Mer" och kräv bekräftelse med kontonumret. |
| 61 | Challenge-korten tar halva bredden och visar ett tekniskt id (`two-step-100k`) i monospace. | Full bredd eller rutnät, id bara i redigeraren. |
| 62 | Mallarna i "New challenges" är fyra rutor med text full av "percent". | En liten bild av faserna (Phase 1 → Phase 2 → Funded) med mål och gränser som siffror. |
| 63 | Tre ord för samma sak: rubriken "Stages", rutorna heter "EVALUATION", faserna "Phase 1" och förhandsvisningen på Portal design skriver "Evaluation". | Välj ett ord för en fas ("Phase") och ett för hela kontots resa ("Stages" bara internt). |
| 64 | I redigeraren står "No rule %" i ett tomt fält, och förhandsvisningen "What traders see" följer inte med när man rullar. | Platshållaren "Ingen regel", och förhandsvisningen fast vid sidan. |
| 65 | Förhandsvisningen på Portal design följer inte med när man rullar ner till färgerna. | Håll den fast vid sidan. |
| 66 | Trading conditions visar bara koder (XAUUSD, XAGUSD), utan namn, grupper eller sätt att ändra många samtidigt. | Namn (Gold, Silver), grupper (Forex, Metals), flaggor, och "Sätt hävstång för alla forex". |
| 67 | Knappen heter "Save payments" och fältet "Your terms for traders (https), or empty for none". "We read them in your application" är internt språk. | "Save", "Villkor för traders (länk)", och en kort förklaring. |
| 68 | KYC: "Approval ticks ID checked for you" är svårt att förstå, och knappen "Go live" i sidhuvudet ser ut att höra till KYC. | "När kontrollen godkänns bockas ID checked av åt dig." Länka till Go live i texten i stället. |
| 69 | Notifications använder kryssrutor som sparas direkt, men inget säger att det sparats. | Strömbrytare (switch), en kort bekräftelse, och "Visa mejlet" på varje rad. |
| 70 | Integrations: rubrikerna upprepar sig (sidan "Integrations", rutan "Integration", underrubriken "Firm API"). | Rutan heter "Firm API" direkt. |
| 71 | "Saved." är en liten grön text som är lätt att missa, och står på olika ställen på olika sidor. | En gemensam bekräftelse (toast) i hörnet för allt som sparas. |
| 72 | Team visar bara e-post och datum. | Namn, initialer, senaste inloggning. |
| 73 | Go live är ett långt formulär i ett svep. | Spara utkast av sig själv, och visa fyra delar (Bolag, Kontakt, Ägare, Länkar) med bock när de är klara. |
| 74 | Svar på supportärenden skrivs från noll varje gång. | Sparade svar, och kontots status bredvid ärendet. |

## Shoppen och köpet

| # | Problem | Förslag |
|---|---|---|
| 75 | Tre orange rutor ovanför produkten i sandlådan (sandlådan, "This shop does not sell yet", "Test payments"). | En diskret rad som säger allt. |
| 76 | "Log in" är en blå huvudknapp och tävlar med "Buy". | Log in som vanlig länk, Buy som huvudknapp. |
| 77 | Priset står mitt i tabellen i monospace och knappen är liten. | Stort pris, tydlig knapp, storlekar och program som val (D6). |
| 78 | Målen saknar valuta ("10% · 10,000.00"). | "10 % · 10 000 USD", eller bara "$10,000". |
| 79 | Inget berättar vägen: utvärdering, funded, utbetalning, vinstdelning. | En enkel bild av resan i tre steg ovanför produkterna. |
| 80 | Testbetalningssidan har ingen logga eller firmans namn. | Firmans logga och färg även där. |
| 81 | Ordersidan efter betalningen har en grön rad "Payment received. Your challenge has started." Det är köpets största ögonblick. | En bock som ritas, kontots kort i förhandsvisning och tre steg: välj lösenord, öppna terminalen, lägg första affären (D8). |
| 82 | Efter lösenordet landar tradern på kontosidan utan välkomst eller hjälp. | En kort välkomstruta första gången: så läser du sidan, öppna terminalen, var gränserna står. |
| 83 | Köpets mejl saknar kvitto: inget pris, inget ordernummer, ingen moms. | Kvitto i mejlet och som PDF på ordersidan. |

## Traderns portal

| # | Problem | Förslag |
|---|---|---|
| 84 | Med ett konto är översikten ett smalt kort uppe till vänster och resten av skärmen tom. | Ett stort kort i full bredd för det aktiva kontot, rutnät först när det finns flera (D7). |
| 85 | Förlustgränserna på kortet står som "5,000.00 / left" där "left" bryts till en egen rad. | Mätare med belopp kvar, utan radbrytning. |
| 86 | "The figures update every few seconds" står som text, men inget syns när siffrorna ändras. | En pulserande prick för "live" och siffror som rullar till sitt nya värde (D3). |
| 87 | Kontosidan visar "Trading account aurora-funded-1001-1" och "Times in Stockholm time" i underrubriken. | Det tekniska id:t i en detaljruta, och "Tider i Stockholms tid". |
| 88 | Fasstegen överst tar tre höga rutor på höjden i mobilen. | En kompakt rad med prickar och linje i mobilen. |
| 89 | Regeltabellen upprepar "5% · 5,000.00 below the balance when the day starts" tre gånger, och i mobilen klipps tabellen i högerkanten. | Samma värde i alla faser slås ihop till en rad, och i mobilen en lista per fas. |
| 90 | Statistiken skriver ord i monospace ("No losses"). | Ord i vanligt typsnitt. |
| 91 | Payouts ber om bankuppgifter redan i fas 1, och meningen "A payout keeps the details it was asked for with" är svår att förstå. | Fäll ihop formuläret till "Lägg till när du är funded", och skriv "Varje utbetalning skickas till de uppgifter som gällde när du begärde den." |
| 92 | En funded trader måste in på kontosidan för att begära utbetalning. | "Request payout" direkt på översiktens kort när det går. |
| 93 | Funded ser ut som vilket konto som helst, med en grön etikett. | Ett eget utseende för funded (D8). |
| 94 | Ett misslyckat konto möter tradern med en röd textvägg överst. Innehållet är bra men formen skrämmer. | En lugn ruta "Vad hände": tid, gränsen, equity och stängningen som en liten tidslinje, med en markering i diagrammet där det hände, och "Try again" med eventuell rabatt. |
| 95 | Certifikaten ser ut som en mall: tjock blå dubbelram och en liten, blek logga. | Ny design med firmans färg och logga, ett tydligt belopp, en QR-kod som verifierar äktheten, och "Dela på X och LinkedIn". |
| 96 | Rundeln uppe till höger har bara initialer. | En meny med namn, e-post, byt lösenord och logga ut. |

## Terminalen

| # | Problem | Förslag |
|---|---|---|
| 97 | Volymstaplarna under diagrammet är lika höga och knallröda och gröna, så de ser ut som en streckkod och tar uppmärksamhet från priset. | Av som standard, eller låga och dämpade. |
| 98 | Stor tom yta mellan Sell/Buy och villkoren i handelspanelen. | Visa vad affären innebär innan man trycker: marginal, värde per pip, och "Den här affären riskerar 18 USD, 0,4 % av dagens utrymme". Det är unikt för prop och bygger förtroende. |
| 99 | Positionernas första kolumn är ett hex-id ("849ede3a"). | Ta bort, eller visa det i detaljer. |
| 100 | Priser i bevakningslistan byter färg men blinkar inte när de ändras. | En kort grön eller röd blinkning vid varje ändring. |
| 101 | "Buy filled." står som en grön ruta i handelspanelen. | En toast med symbol, lot och pris, och ett valfritt ljud. |
| 102 | Standardvolymen är 1.00 lot även för guld, det vill säga cirka 240 000 USD. | Kom ihåg traderns senaste volym per symbol. |
| 103 | Terminalen har en annan blå och en annan bakgrund än portalen, och en vanlig "trending up"-ikon som logga. | Samma färger och typsnitt som Kronant (D1, D11). |
| 104 | Historikens "Profit" är brutto. | "Net" som huvudkolumn, se bugg 20. |

## Mejl

| # | Problem | Förslag |
|---|---|---|
| 105 | Plattformens mejl till firman (välkomstmejlet, "New sale") är bara text, medan mejlen till traders är snygga med logga och knapp. | Samma mall för plattformens mejl, med Kronants logga. |
| 106 | Mejlet efter köpet säger bara "Confirm your email". | Kvitto, en kort sammanfattning av reglerna och "Så kommer du igång". |
| 107 | Sidfoten i traderns mejl är "Aurora Funded." med punkt och inget mer. | Firmans supportadress, adress och varför mejlet kommer. |
| 108 | Ämnesrader använder e-post i stället för namn. | Se bugg 16. |

## Större designförslag

### D1. En egen identitet för Kronant

Kronant (krona, krona som mynt, nordiskt) har ett bättre namn än utseende. Förslag på riktning, "nordisk privatbank":

- Nästan svart grafit som bakgrund (inte mörkblå), varm benvit text, och en mässingsfärgad accent för Kronant (inte Tailwind-blått).
- En logga: ett enkelt K eller en krona som också fungerar som ikon i fliken och i terminalen.
- Serif för stora rubriker och siffror i viktiga ögonblick, en tydlig grotesk för allt annat (D4).
- Tunna linjer och mycket luft i stället för kort i kort.

Det gäller plattformens sidor, terminalen, våra mejl och vår adminvy. Firmornas portaler behåller sin egen färg men får samma bättre grund.

### D2. Djup: ytor, ljus och skuggor

- Fyra nivåer: bakgrund, yta, upphöjd yta (kort), och svävande (meny, dialog, toast). Varje nivå lite ljusare, med en mjuk skugga som blir större uppåt.
- Kortens överkant får en svag ljus kant (`inset 0 1px 0 rgba(255,255,255,0.05)`), som om ljuset kommer uppifrån. Det är det som ger djup utan att det blir plastigt.
- En mycket svag glöd i firmans färg bakom sidhuvudet och det viktigaste kortet, och ett fint brus över bakgrunden så att mörka ytor inte ser digitala ut.
- Sidhuvudet blir halvgenomskinligt med oskärpa bakom när man rullar.
- Egna kontroller för strömbrytare, kryssrutor, val och datum, så de ser likadana ut överallt och följer temat.
- Allt som tokens i temat, så att firmorna senare kan välja (se "firmans designval").

### D3. Rörelse

Kort och lugnt, aldrig i vägen, och avstängt för den som valt minskad rörelse i systemet.

- Sidor glider in (8 px uppåt och tonas in), med korten i en kort kaskad.
- Siffror rullar till sitt nya värde: saldo, equity, dagens resultat, priser i terminalen.
- Staplar och mätare fylls när sidan laddas, och mätarna ändrar färg mjukt när gränsen närmar sig.
- Flikars markering glider till den valda fliken.
- Dialoger och sidopaneler med en mjuk fjädring.
- Kort lyfter 2 px med större skugga när musen är över dem.
- Laddning som skelett med en svag glans i stället för "Loading...".
- Toasts för sparat, köpt och fyllt.
- Firande vid milstolpar (D8).

### D4. Typsnitt och siffror

- Byt bort Geist. Förslag: Schibsted Grotesk (nordisk, gratis på Google Fonts) för allt i portalen, med tabellsiffror så kolumner står rakt. Kontrollera tabellsiffrorna innan valet.
- En serif för stora ögonblick: Instrument Serif eller Fraunces på framsidans rubriker, certifikaten och funded.
- Monospace bara i terminalens priser och tabeller (JetBrains Mono eller IBM Plex Mono), aldrig för vanliga belopp och räknare i portalen.
- Tusentalsavgränsare och decimaler efter traderns språk på sikt, hela belopp utan decimaler där det passar (kontostorlek, priser i shoppen).

### D5. Ikoner och bilder

- Byt de 40 egenritade ikonerna mot Phosphor Icons. Varianten "duotone" har en ljusare fyllning som ger djup och ser inte ut som standarduppsättningen i AI-byggen (Lucide).
- Flaggor för valutor i terminalen, på Trading conditions och i landvalet.
- Enkla illustrationer för tomma lägen och milstolpar, i samma stil.
- Riktiga skärmbilder av produkten på framsidan.

### D6. Shoppen som en riktig butik

- Överst: firmans namn och ett löfte, och siffror som bygger förtroende när de finns ("1 240 USD utbetalt senaste 30 dagarna", "betalt inom 2 dagar i snitt").
- Val av program (One-step, Two-step, Instant) och storlek (10K, 25K, 50K, 100K) som knappar. Priset och reglerna byts med en mjuk övergång.
- Reglerna som ikoner med siffror: mål, daglig gräns, total gräns, vinstdelning, minsta antal dagar. En "Jämför alla"-tabell för den som vill.
- Resan i tre steg: klara utvärderingen, bli funded, få betalt.
- Köpet i en sidopanel med sammanfattning (challenge, pris, rabattkod, moms) i stället för ett formulär längre ner på sidan.
- Vanliga frågor från firmans regler.
- En mobilversion med köpknappen fast i nederkanten.

### D7. Traderns kontosida som en cockpit

- Överst ett stort kort: equity som rullar, dagens resultat, en levande prick, och resan Phase 1 → Phase 2 → Funded.
- Tre mätare bredvid varandra: "Kvar idag" (daglig gräns), "Kvar totalt" (total gräns) och "Till målet". Grönt, gult när 75 % är använt, rött nära gränsen.
- "Nästa steg"-rad: "Handla minst 3 dagar till", "Öppna en affär före 5 nov", "Du kan begära utbetalning".
- Diagram, dag för dag, statistik och affärer under, som i dag.

### D8. Milstolpar

| Ögonblick | Förslag |
|---|---|
| Betalning klar | Bocken ritas, kontots kort glider fram, tre steg att komma igång. |
| Första affären | En liten toast i portalen: "Första handelsdagen räknad." |
| Klarad fas | Helskärm med konfetti i firmans färg, resultatet, nästa fas, och certifikatet. |
| Funded | Eget utseende på kontot (guld eller firmans färg), "Grattis, du är funded", certifikat att dela. |
| Utbetalning betald | Beloppet rullar upp, certifikat, "Dela". |
| Misslyckad | Ingen konfetti och ingen röd vägg: lugn förklaring, vad som hände och ett erbjudande att försöka igen. |

Firman kan senare slå av firandet om den vill.

### D9. Adminpanelen som kontrollrum

- Menyn delas i dagligt arbete och inställningar (punkt 52).
- Ctrl+K för att hitta allt (punkt 53).
- Nyckeltal med små kurvor och jämförelse (punkt 56).
- Händelseflödet glider in när något nytt händer, utan att sidan laddas om.
- "Needs you" som en inkorg man betar av, med en kort animation när en rad är klar.

### D10. Terminalen i mobilen

- Tre flikar längst ner: Diagram, Handla, Positioner.
- Kontoraden komprimeras till equity och dagens utrymme, resten bakom ett tryck.
- Handelspanelen som ett blad nerifrån, med stora Sell och Buy.
- Bevakningslistan som egen vy med sökning.

### D11. Ett designsystem för båda produkterna

Portalen och terminalen har i dag var sina färger, kanter och knappar. Samla färger, ytor, rundning, skuggor, typsnitt och rörelse som tokens i `shared/`, så att båda använder samma. Bygg vidare på `ui.tsx` och temat så att firmorna senare kan välja typsnitt, rundning och täthet, inte bara färger.

### D12. Framsidan

- Första skärmen: en mening om vad Kronant Prop är, knappen till sandlådan, och en levande bild av portalen och terminalen bredvid, lite vinklad, med siffror som tickar.
- Under: tre stora delar med riktiga bilder i stället för sex likadana textkort: din portal, terminalen, din adminpanel.
- Prisdelen med kalkylator och jämförelse (punkt 34 och 37).
- Vanliga frågor och sidfot.

## Paket jag föreslår (inte installerade)

| Paket | Till vad |
|---|---|
| `motion` (tidigare Framer Motion) | Sidövergångar, kaskader, flikar som glider, dialoger och sidopaneler. |
| `@number-flow/react` | Siffror som rullar till sitt nya värde: saldo, equity, priser. |
| `@phosphor-icons/react` | Ikoner med duotone för djup (D5). |
| `sonner` | Toasts för sparat, köpt, fyllt. |
| `canvas-confetti` | Firande vid klarad fas, funded och utbetalning. |
| `vaul` | Blad nerifrån i mobilen: köpet, terminalens handelspanel. |
| `cmdk` | Ctrl+K-sökningen i adminpanelen. |
| `@radix-ui/react-switch`, `-tooltip`, `-popover`, `-select` | Tillgängliga kontroller som följer temat. |
| `flag-icons` | Flaggor för valutor och länder. |
| Typsnitt via `next/font` (finns redan) | Schibsted Grotesk, en serif och JetBrains Mono (D4). |

Portalens diagram kan bytas till `lightweight-charts`, som terminalen redan har, så att båda ser likadana ut och får zoom och hårkors.

## Förslag på ordning

1. Buggarna 11-32. De flesta är små.
2. Designsystemet: färger, ytor och skuggor, typsnitt, ikoner och rörelse (D1-D5, D11). Allt annat blir bättre av sig självt när det är på plats.
3. Shoppen, köpet och ordersidan (D6, punkt 75-83). Det är där firmorna tjänar pengar.
4. Traderns kontosida och milstolparna (D7, D8).
5. Terminalen i mobilen (D10).
6. Adminpanelen: menyn, Ctrl+K och nyckeltalen (D9).
7. Framsidan och Kronants identitet utåt (D12).

Skärmbilderna från genomgången ligger tillfälligt i sessionens arbetsmapp (`scratchpad/ux/shots`), och raderas när sessionen städas.
