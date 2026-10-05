# 0042. ID-kontroll av traders med en extern tjänst

- Status: Föreslagen
- Datum: 2026-10-05

## Sammanhang

Firmor måste veta vem de betalar ut till: att tradern är den den säger, har ett konto per person och inte bor i ett land firman inte får ha kunder i. I dag bockar firman för "ID checked" och "Address checked" för hand på traderns kort ([ADR 0037](0037-firmans-kontroller-och-utbetalningsbeslut.md)), men plattformen har inget sätt att faktiskt kontrollera ett ID. Den enda vägen för firman blir att be om en bild på passet i ett supportärende eller per mejl. Det ser ut som bedrägeri för tradern, och passbilden blir sedan liggande hos firman och oss utan att någon kontrollerat att den är äkta.

Vi vill inte ta emot eller spara ID-handlingar själva. En tjänst som är byggd för det kontrollerar dokumentet och att personen är levande och samma som på bilden, och sparar bilderna så länge som behövs.

## Alternativ

Priserna är tjänsternas egna, kontrollerade 2026-10-05, i USD. En kontroll är dokument, liveness (att personen är levande framför kameran) och ansiktsjämförelse.

| | Didit | Veriff | Sumsub |
|---|---|---|---|
| Pris per kontroll | 0,30 (0,15 dokument, 0,10 liveness, 0,05 ansikte), 500 gratis per månad | 0,80 (Essential) | 1,35 (Basic) |
| Minsta kostnad per månad | Ingen | 49 | 149 |
| Adress (proof of address) | 0,20 | Ingår inte i planerna man tecknar själv | Ingår i Compliance, 1,85 per kontroll och minst 299 per månad |
| Sanktions- och PEP-listor | 0,20 | 0,64 | Ingår i Compliance |
| Var bilderna sparas | EU (AWS) som standard | Fråga Veriff | EU kan väljas (AWS) |
| Hur länge | Väljs, 1 månad till 10 år, och radering med API | 2 år kostar 0,30 extra | Fråga Sumsub |
| Firmans utseende | White label | Bara i Premium, 1,89 och minst 209 per månad | Teman i deras SDK |
| Certifieringar | ISO 27001, SOC 2 typ 2, iBeta nivå 1 | Etablerad sedan länge | Etablerad, standard hos mäklare och kryptobörser |

Vad det kostar oss i månaden:

| Kontroller per månad | Didit, bara ID | Didit, med adress och listor | Veriff, med listor | Sumsub Basic | Sumsub Compliance |
|---|---|---|---|---|---|
| 100 | 0 | 40 | 144 | 149 | 299 |
| 1 000 | 150 | 550 | 1 440 | 1 350 | 1 850 |

Det blir få kontroller i början: bara traders som når ett funded-konto eller sin första utbetalning kontrolleras, och bara en gång.

## Beslut

- **Det heter KYC för firman och vår personal**, i adminpanelen och i vår adminvy, eftersom det är ordet firmor känner igen och som banker och betalningsleverantörer frågar efter. Sidan förklarar kort varför KYC är viktigt. Traderna ser vanliga ord, "Verify your identity", eftersom KYC inte säger alla traders något.
- **Firman väljer hur traders kontrolleras**, under KYC i adminpanelen: med vår inbyggda kontroll genom Didit eller med sin egen tjänst. Inget är förvalt, så firman tar ställning själv. Valet krävs innan firman går live, men inte för vår granskning, så att firman kan välja medan vi granskar eller efter att vi godkänt den. Att kontrollera för hand är inget sätt längre, eftersom firman inte har något sätt att faktiskt kontrollera ett ID, vilket är skälet till hela beslutet. En bock för "ID checked" för hand finns kvar som ett undantag.
- **Den inbyggda kontrollen använder Didit**, bakom ett eget gränssnitt i propfirm-tjänsten (`IIdentityChecker`, som `IBillingGateway`), så att vi kan byta till Sumsub om en firma, en bank eller en betalningsleverantör kräver det. Didit kostar inget förrän vi har volym, sparar bilderna i EU och låter oss välja hur länge, och har samma mönster som Stripe-betalningen vi redan har: en länk till deras sida och en signerad webhook tillbaka. Beslutet läses alltid från Didits API, aldrig från webhookens kropp. Firman kan slå på adresskontroll och kontroll mot sanktions- och PEP-listor.
- **Den inbyggda kontrollen kostar firman 15 USD i månaden med 25 kontroller**, sedan 0,80 USD per kontroll, och 0,30 USD för adress och för listor per kontroll. Varje månad debiteras som en egen debitering när nästa månad debiteras: med månadens pris när firman gjorde kontroller i den eller hade kontrollen påslagen hela månaden, så att den inte går att slå av precis innan månaden debiteras för att slippa betala. En månad firman slår på den utan kontroller kostar inget, och en obetald debitering pausar inga challenges. Priserna är förslag i `Billing:IdentityChecks`.
- **Firmans egen tjänst**: tradern skickas till firmans adress med sitt id och sin e-post, och firmans system berättar resultatet med `PUT /api/firm/v1/traders/identity`. Vi bygger inga kopplingar till enskilda tjänster, så det fungerar med vilken tjänst som helst, som firmans egen betalsida för köp.
- **Firmans egen tjänst måste fungera hela vägen** innan firman går live: en trader har skickats från portalen till firmans adress, och firmans tjänst har rapporterat ett beslut om den tradern. Firman prövar det i sandlådan, och en ny adress prövas igen. Annars kunde en firma gå live med en tjänst som aldrig svarar, och traderna skulle fastna före sin första utbetalning.
- **Firman väljer vad som väntar**: utbetalningar (standard) eller funded-kontot. En bock för "ID checked" för hand räknas alltid som kontrollerad, som ett undantag.
- **Tradern** ser "Verify your identity" på startsidan när ett funded-konto är nära, under Payouts och på `/identity`, med var kontrollen är och orsaken vid ett nej, och kan försöka igen.
- **Firman** ser på traderns kort att ID är kontrollerat, när och av vem, med namnet, födelsedatumet och landet från handlingen. "ID checked" och "Address checked" bockas för automatiskt. Utbetalningskön varnar när namnet på ID:t inte är kontoinnehavaren i traderns utbetalningsmetod.
- **Vi sparar bara resultatet**: status, Didits id för kontrollen, när beslutet togs, namn, land och födelsedatum från handlingen och orsaken vid ett nej. Bilderna och dokumentnumret finns bara hos Didit. Didit är vårt personuppgiftsbiträde.
- **Sandlådan och utveckling** får en testkontroll med en egen sida som godkänner eller nekar, utan kostnad, som testbetalningarna.
- **Ärenden är inte till för ID** ([ADR 0041](0041-supportarenden-mellan-traders-och-firman.md)). Formuläret där firman skriver först säger det.

## Konsekvenser

- Vi betalar Didit per kontroll och tar betalt av firmorna. Med 500 gratis kontroller i månaden hos Didit, för alla firmor tillsammans, kostar det oss i början inget.
- Didit är ett yngre bolag än Sumsub och Veriff, med certifieringar från 2026. Gränssnittet gör att vi kan byta utan att ändra portalen. Traderna ser firmans namn i portalen.
- Innan riktiga kontroller körs behöver vi ett konto hos Didit med nyckel, webhook och arbetsflöden, och få bekräftat av Didit: om utseendet kan sättas per firma och inte bara en gång för hela kontot, och deras personuppgiftsbiträdesavtal och lista över underbiträden.
- Hur länge bilderna sparas ställs in hos Didit och bestäms tillsammans med villkoren för firmor och traders. Ett förslag är 12 månader efter kontrollen.
- Färdiga kopplingar till en firmas eget konto hos till exempel Sumsub kan komma senare, om firmor ber om det.
- Ingen firma går live utan en ID-kontroll som fungerar. En firma som hade valt att kontrollera för hand har inget val och väljer igen, och en firma som redan är live utan val har inget som väntar på en kontroll förrän den väljer.

## Källor

- Didit, priser: https://docs.didit.me/getting-started/pricing
- Didit, säkerhet, lagring och certifieringar: https://docs.didit.me/getting-started/security-compliance
- Didit, sessioner, webhooks och white label: https://docs.didit.me/sessions-api/create-session, https://docs.didit.me/integration/webhooks, https://didit.me/en/products/white-label
- Veriff, planer: https://www.veriff.com/plans/self-serve
- Veriff, sessioner och webhooks: https://devdocs.veriff.com/docs/webhooks-guide
- Sumsub, priser: https://sumsub.com/pricing/
- Sumsub, lokal lagring: https://docs.sumsub.com/docs/local-data-processing
