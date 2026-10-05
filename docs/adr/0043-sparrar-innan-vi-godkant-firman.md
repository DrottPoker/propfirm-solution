# 0043. Firman når bara sitt eget team innan vi har godkänt den

- Status: Föreslagen
- Datum: 2026-10-05

## Sammanhang

En firma som registrerar sig hamnar direkt i sandlådan, där den provar allt med testkonton och testbetalningar ([ADR 0017](0017-firmor-registrerar-sig-sjalva.md)). Vi granskar den först när den vill gå live ([ADR 0021](0021-vi-granskar-firmor-innan-de-gar-live.md)). Sandlådan ska visa vad firman får innan den betalar handpenningen, så det mesta ska fungera där.

Men några saker når riktiga människor i vårt namn, och dem kan en firma vi inte har granskat missbruka:

- **Mejl till vilken adress som helst.** En firma i sandlådan kan starta en challenge, skriva ett ärende eller låta någon köpa i butiken med vilken e-postadress som helst. Mejlet går från vår avsändaradress med firmans namn och logga. Det kan användas för spam eller bedrägeri, och vår e-postleverantör kan då stänga av oss för alla firmor.
- **Inbjudningar till teamet.** De går också till vilken adress som helst, och firman väljer själv namnet som står i dem.
- **En butik och en portal öppen för alla.** Vem som helst kan köpa en challenge gratis med testbetalning, och sedan använda terminalen och våra priser, skicka filer i supportärenden och bli skickad till firmans egen KYC-sida från vår portal, där en oseriös firma kan be om passbilder. En firma kan också bjuda in traders med en länk den skickar själv och driva en liten verksamhet gratis.
- **Firmans egen betalsida.** Den fungerade redan i sandlådan, så butiken skickade besökare till firmans egen sida, där firman kunde ta riktiga pengar innan vi granskat den. Stripe med riktiga nycklar fungerar bara när firman är live, men den egna sidan saknade den spärren.
- **Egen domän.** Portalen på firmans egen domän med ett certifikat från oss ser ut som ett riktigt företag, fast vi inte vet vem som står bakom.

KYC och resten av adminpanelen kostar oss inget i sandlådan och når ingen utanför firman, så de får vara öppna. ID-kontrollerna är testkontroller där ([ADR 0042](0042-id-kontroll-med-en-extern-tjanst.md)).

## Beslut

- **Godkänd** betyder att vår granskning har status `Approved`, eller att firman är live. Firmor från konfigurationen är live. Regeln finns på ett ställe, `FirmApproval`, som SQL-villkor och som anrop.
- **Innan vi har godkänt firman når den bara sina egna administratörer.** Bara de:
  - **får firmans mejl.** Regeln gäller alla mejl om firman i mejlkön, oavsett sort, och de mejl till traders som skickas direkt: firmans mejl till traderns kort och köparens inbjudan. Ett mejl till någon annan sparas i kön med `withheld_at`, så att det syns vad som inte skickades, men skickas aldrig och skickas inte heller senare när firman godkänns, eftersom länkarna i det då kan ha gått ut.
  - **köper i butiken.** En order från någon annan nekas med 403, och butiken säger det i förväg.
  - **loggar in som firmans traders.** Inloggning, inbjudan, länk för nytt lösenord och lösenord på orderns sida nekas för andra med 403, utan att länken förbrukas. En inloggad traders session kontrolleras vid varje anrop, så en session från förut slutar fungera. Firmans inbjudningslänkar och terminallänkar, i adminpanelen och firmans API, görs bara för administratörernas egna konton.

  Firman provar traderns sida med sin egen adress.
- **Firmans egen betalsida tar emot köpare först när firman är live.** Innan dess, också efter vårt godkännande, köper bara firmans administratörer genom den, för att prova den. Det gör regeln från [ADR 0029](0029-butiken-tar-riktiga-pengar-fran-forsta-dagen-live.md) hel: riktiga pengar tas i portalen först när firman är live.
- **Innan vi har godkänt firman skickar den högst `Sandbox:MaxAdminInvites` inbjudningar till teamet på 30 dagar** (standard 10). Inbjudningar som tas tillbaka eller skickas igen räknas också, så att det inte går att komma runt gränsen. De tas därför inte bort längre, utan går ut, och städas bort en månad senare.
- **Firman lägger till sin egen domän först när vi har godkänt den.** En domän som lades till innan regeln fanns blir inte portalens adress förrän vi har godkänt firman, och DNS frågas inte under tiden.
- **Adminpanelen och butiken säger det i förväg:** att firman bara når administratörerna där firman startar en challenge, mejlar en trader, skriver ett ärende och väljer notiser, att butiken bara säljer till firmans team, och att domänen kommer efter vårt godkännande på sidan Your domain.

## Konsekvenser

- En firma vi inte har granskat kan inte använda vår avsändaradress för att nå främlingar, inte ta pengar genom vår portal, inte låta främlingar använda terminalen eller ladda upp filer, och inte låna vårt certifikat till en egen domän.
- En firma som vill att en kollega provar som trader bjuder in kollegan till teamet. En firma som provar med en annan adress ser varför det inte går.
- En trader i sandlådan med en adress som inte är en administratörs kan finnas, till exempel från firmans API, men kan inte logga in. Firman provar inloggning, KYC och utbetalningar med sin egen adress.
- Mejl som hölls kvar räknas inte i några mejlgränser och syns bara i databasen. Vill vi visa dem för firman eller vår personal senare finns de där.
- När firman godkänns gäller inga av spärrarna längre, utom den för egen betalsida, som gäller tills firman är live. En firma vi godkänt men som inte betalat än kan alltså sälja med testbetalningar till vem som helst, men inte ta riktiga pengar.
