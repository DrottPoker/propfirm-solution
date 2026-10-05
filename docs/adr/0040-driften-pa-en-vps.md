# 0040. Driften på en VPS i Sverige med Postgres i Docker

- Status: Föreslagen
- Datum: 2026-10-05

## Sammanhang

Allt körs i dag bara lokalt, och båda tjänsterna vägrar starta utanför miljön Development tills produktionen är på plats (steg 10). Innan första servern sätts upp behöver vi bestämma var och hur allt körs.

Det som ska köras:

- **Handelstjänsten** (`Trading.Service`): handelsmotorn, prisflödet och realtid till terminalen. Den kan bara köras som en instans, eftersom motorn ligger i minnet (ADR 0016). Varje indata sparas i journalen i Postgres (ADR 0008), så tiden till databasen påverkar varje order.
- **Propfirm-tjänsten** (`Prop.Api`): regelmotorn, portal-API:t, firmans API, betalningar, mejl och bakgrundsjobben. Den körs som en instans tills läsningen av händelseströmmen får lås mellan instanser (ADR 0013).
- **Terminalen och portalen**: två Next.js-appar.
- **Postgres** med två databaser, `trading` och `prop`, med var sin roll så att ingen produkt kan läsa den andras data. Tjänsterna kör sina migreringar själva när de startar.
- **En proxy** framför allt, som gör certifikaten, också för firmornas egna domäner (ADR 0039).

NATS startas i den lokala miljön (ADR 0003), men ingen tjänst använder det. Händelserna mellan produkterna går genom händelseströmmen i handelsplattformens admin-API (ADR 0012).

Kraven:

- Uppe dygnet runt under handelsveckan, med larm och en öppen statussida (produktplanen).
- Ingen data får gå förlorad. Journalerna är sanningen om konton, pengar och beslut.
- Postgres och de interna vägarna nås inte från internet (ADR 0018).
- Data om firmor och traders stannar i EU. Bolaget är svenskt.
- Låg kostnad tills firmorna betalar. Vi har inga kunder än.

Alternativ som vägdes, med listpriser i oktober 2026 utan moms:

- **En stor molnleverantör (AWS, Azure eller Google Cloud)** med hanterad Postgres, reserv i en annan zon och återställning till valfri tidpunkt. Omkring 600 USD i månaden för test och produktion. Mest driftsäkert, men dyrare än intäkten från en firma innan vi har någon.
- **Supabase för databasen och servrarna hos någon annan.** Det Supabase tillför utöver Postgres (inloggning, lagring, färdiga API:er) har vi redan byggt själva. Databasen hamnar i ett annat nätverk än tjänsterna och får en publik adress.
- **En VPS där allt körs i containrar.** Omkring 90 kr i månaden per server. Vi sköter själva backup, uppdateringar och återställning, och det finns ingen reserv som tar över automatiskt.

## Beslut

- **En VPS per miljö hos Hostup i Stockholm**, med Ubuntu 24.04 LTS. Vi börjar med planen VPS SM (4 vCPU, 8 GB minne, 100 GB NVMe) och uppgraderar när det behövs.
  - Den första servern är testmiljön och följer `main`.
  - Produktionen får en egen server när vi lanserar, så att test och produktion aldrig delar maskin. Den uppdateras när vi bestämmer det.
  - Värdnamnen följer ADR 0018.
- **Allt körs i containrar med Docker Compose:** Postgres, handelstjänsten, propfirm-tjänsten, terminalen, portalen och Caddy. Postgres har samma huvudversion som lokalt, med båda databaserna och var sin roll.
- **Bara Caddy nås från internet.** Brandväggen släpper bara in port 22, 80 och 443, och bara Caddy publicerar portar från Docker. Postgres, `/api/tls/allowed` och portal-API:t nås bara inifrån servern. SSH tar bara nycklar, inte lösenord.
- **Caddy gör certifikaten.**
  - Vanliga certifikat för våra egna värdnamn.
  - Wildcard-certifikat för `*.propbrand.app` med en DNS-utmaning mot Cloudflare.
  - Certifikat på begäran för firmornas domäner, efter att ha frågat `/api/tls/allowed` (ADR 0039).
  - Bara de vägar som ADR 0018 tillåter släpps igenom.
  - Certifikaten sparas på en egen volym, så att de inte hämtas om vid varje uppdatering.
- **DNS hos Cloudflare**, utan Cloudflares proxy, så att Caddy sköter certifikaten för både våra och firmornas domäner. API-nyckeln till Caddy får bara ändra DNS-poster.
- **Backup med pgBackRest till en lagring utanför Hostup** (Backblaze B2 i EU).
  - Postgres skickar sina ändringar dit löpande, minst en gång i minuten. Databasen kan därför återställas till valfri tidpunkt, och vi förlorar högst omkring en minut om servern försvinner.
  - En hel backup per vecka och en differentiell per dygn. De sparas i 35 dagar.
  - Backupen är krypterad. Nyckeln förvaras offline och i vår lösenordshanterare, inte bara på servern.
  - Vi övar återställning på testservern en gång i kvartalet.
- **Mejl genom Resend** över SMTP, som tjänsten redan använder. Domänen skickar från Irland (`eu-west-1`). `propbrand.com` får SPF, DKIM och DMARC, så att mejlen inte hamnar i skräpposten. Gratisnivån räcker för testmiljön. Produktionen behöver Pro (50 000 mejl i månaden).
- **Hemligheterna ligger i en fil på servern** som bara tjänsterna kan läsa: lösenorden till databasen, `Secrets:Key`, partnernyckeln, våra Stripe-nycklar, prisflödets nyckel, Resends och Cloudflares nycklar. Originalet finns i vår lösenordshanterare. Hemligheterna finns aldrig i repot eller i containrarna. `Secrets:Key` krypterar firmornas betalningsnycklar och traders utbetalningsmetoder (ADR 0026), så utan den går de inte att läsa.
- **Bygg och leverans.** CI bygger en container per tjänst för varje commit på `main` och lägger dem i GitHub Container Registry. Testservern uppdateras direkt över SSH. Produktionen uppdateras med ett manuellt steg som anger vilken version. Gamla versioner rensas, så att vi håller oss inom GitHubs kvot. Servern bygger aldrig något själv.
- **Produktionen uppdateras på helgen**, när marknaden är stängd. Handelstjänsten är nere tills motorn har läst in sitt tillstånd igen. En brådskande rättelse under veckan ger ett kort avbrott, och terminalen ansluter igen av sig själv.
- **Uppdateringar av servern.** Ubuntus säkerhetsuppdateringar installeras automatiskt. Postgres, Caddy och de andra containrarna uppdateras genom att versionen ändras i repot.
- **Övervakning med en extern tjänst.**
  - Den anropar `/health` på båda tjänsterna och laddar sidorna varje minut, driver den öppna statussidan och larmar på telefon dygnet runt under handelsveckan.
  - Servern rapporterar till tjänsten varje minut om disk och minne är under gränsen, och backupen rapporterar när den har lyckats. Uteblir en rapport larmar tjänsten.
- **Konfigurationen ligger i repot** under `deploy/`: Compose-filerna, Caddys konfiguration, pgBackRest och ett skript som gör en ny server klar. En miljö kan byggas upp från grunden på en ny server.
- **NATS körs inte på servern**, eftersom ingen tjänst använder det.

## Konsekvenser

- Kostnaden blir omkring 90 kr i månaden per server, ett par dollar för backuplagringen och 20 USD för Resend i produktionen. Cloudflares DNS är gratis, och domänerna köps ändå.
- **Ingen automatisk reserv.** Går servern sönder ligger allt nere tills den är återställd på en ny server, troligen 30-60 minuter med skriptet och backupen. Vi kan förlora högst omkring en minut av data. När de första firmorna betalar lägger vi till en andra server med en kopia av databasen som kan ta över (ett eget beslut).
- Vi sköter själva det som en hanterad databas annars gör: backup, återställning och uppdateringar. Det mesta är automatiserat, men återställningen måste övas för att vi ska kunna lita på den.
- Servern har delade processorer, så en annan kund på samma maskin kan ibland göra den långsammare. Märks det uppgraderar vi till en plan med egna processorer.
- Resend skickar mejlen från EU, men sparar uppgifter om mejlen, till exempel mottagare och loggar, i USA. Det behöver täckas av Resends personuppgiftsbiträdesavtal.
- Tjänsterna behöver bara Postgres, SMTP och en proxy. Flyttar vi till en annan leverantör eller till en hanterad databas ändras bara konfigurationen i `deploy/` och några inställningar.
- Kvar i koden innan produktionen kan starta (steg 10):
  - Tjänsterna får köras utanför Development.
  - Propfirm-tjänsten litar på Caddys adress för `X-Forwarded-For`, inte bara på loopback.
  - Firmor, konton och personal i konfigurationen finns bara i utvecklingen. Första personalkontot i produktionen skapas på ett säkert sätt.
  - Ett prisflöde med licens att visa priserna för traders (ADR 0010).
  - Dockerfiler för de fyra tjänsterna, Compose-filer, Caddys konfiguration, backupen och skriptet för en ny server.
- ADR 0003 nämner NATS, men det används inte. Den lokala `docker-compose.yml` kan sluta starta det i ett eget steg.
