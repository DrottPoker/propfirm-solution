# 0017. Firmor registrerar sig själva och börjar i en sandlåda

- Status: Föreslagen
- Datum: 2026-10-03

## Sammanhang

Att kunden kommer igång själv, utan säljsamtal, är det som ska skilja oss från konkurrenterna (se produktplanen). I dag finns varje firma bara i propfirm-tjänstens konfiguration, och vi lägger in den för hand. Firman ska i stället kunna registrera sig, få en egen portal och en egen server på handelsplattformen och prova hela kedjan i en sandlåda inom några minuter. Kontrollen av bolag och ägare innan firman går live kommer i en senare fas.

## Beslut

- **Firmor sparas i databasen.** Firmor i konfigurationen skrivs dit vid varje start, för utveckling och tester, och räknas som live. Tjänsten håller alla firmor i minnet och uppdaterar dem när de ändras, eftersom den bara körs som en instans.
- **Registrering på plattformens adress.** Samma portal visar vårt eget namn på plattformens adress (`Platform:Url`) och firmans utseende på firmans adress. Firman anger firmanamn, ett kort namn, e-post och lösenord och godkänner villkoren och personuppgiftsbiträdesavtalet. Versionen av villkoren och tiden sparas.
- **Det korta namnet** är firmans id, dess underdomän och dess server på handelsplattformen, till exempel `acme` med portalen på `acme.<vår domän>`. Det är 2 till 40 tecken, små bokstäver, siffror och bindestreck, och vissa namn är reserverade.
- **E-postadressen bekräftas först.** Bekräftelselänken gäller en gång i 24 timmar. Firman skapas först när länken används, så obekräftade registreringar kostar ingenting. Kravet är en inställning som är avstängd i utveckling.
- **När firman skapas** sparas firman, dess adress, administratören och en tvåstegs-challenge på 100 000 USD i en transaktion. Administratören loggas in på firmans adress med en engångslänk, så att sessionen hör till firmans domän (ADR 0014).
- **Servern på handelsplattformen skapas i bakgrunden** genom partner-API:t (ADR 0016). Ett jobb försöker igen tills det lyckas, och firman är `Provisioning` tills dess. Har servern redan skapats men svaret gått förlorat, ber jobbet om en ny nyckel.
- **Sandlåda.** En ny firma har status `Sandbox`: allt fungerar, men den kan ha högst ett visst antal öppna challenge-konton, och portalen visar att det är en testmiljö. Firman går live när kontrollen av bolag och ägare finns.
- **Hemligheter krypteras i databasen.** Handelsplattformens nyckel och webhook-hemligheten krypteras med AES-GCM och en nyckel som ligger utanför databasen (`Secrets:Key`). Firmans egen API-nyckel sparas bara som SHA-256, och visas en gång när firman skapar den.
- **E-post skickas med SMTP** från plattformen: bekräftelser och inbjudningar till administratörer. Lokalt fångas allt av Mailpit. Mejl till traders skickas fortfarande av firman själv, eftersom de ska komma från firmans egen domän.
- **Adminpanelen** får utseende (logga och färger), challenges från mall, administratörer (bjuda in och ta bort) och integration (API-nyckel och webhook). En borttagen administratörs session slutar fungera direkt.

## Konsekvenser

- En firma kommer från registrering till en fungerande sandlåda på några minuter, utan att vi gör något.
- Bakgrundsjobben per firma startar när en ny firma blir klar, inte bara vid start.
- Utan `Secrets:Key` startar inte tjänsten. Nyckeln måste hanteras som en hemlighet i produktion och får aldrig bytas utan att hemligheterna krypteras om.
- Plattformens adress, mallen för firmornas adresser och e-postservern är inställningar. Lokalt nås nya firmor på `{id}.localhost`, som webbläsare skickar till den egna datorn.
- Egen domän med automatiskt certifikat, betalning till oss och kontrollen innan live kommer i senare faser.
- Firman kan inte byta sitt korta namn, eftersom det ingår i adressen, servern och kontonas id.
