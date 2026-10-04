# 0027. Firmans handelsvillkor, listning i terminalen och inloggning genom portalen

- Status: Föreslagen
- Datum: 2026-10-04

## Sammanhang

Genomgången som ny firma visade tre brister i hur firmans traders når handelsplattformen.

- Firman kunde inte välja sina handelsvillkor. Varje ny firma fick en kopia av mallgruppen med fyra instrument, och villkoren kunde inte ändras efter att gruppen skapats ([ADR 0016](0016-firmor-skapas-medan-handelsplattformen-kor.md)).
- En firma som registrerat sig listades aldrig i terminalens serverlista, inte heller efter att den gått live, fast ADR 0016 säger att den ska listas då.
- Firmans traders har ett slumpat lösenord till terminalen som aldrig visas. De kom bara in med knappen i portalen, och när sessionen i terminalen gick ut stod de på en inloggning de inte kunde använda.

## Beslut

- **Grupper som skapats med `CreateGroup` kan få nya symboler och villkor med motorns indata `ChangeGroupSymbols`**, och händelsen `GroupSymbolsChanged`. Valutan och nivån för stop out ändras inte. Grupper i konfigurationen avvisas med `GroupNotChangeable`, eftersom konfigurationen bestämmer dem. En symbol som tas bort medan ett konto har en position eller en väntande order i den avvisas med `SymbolInUse`.
- **Nya villkor gäller direkt**, också öppna positioner och väntande ordrar. Hävstången ändrar marginalen och påslaget priset de värderas till, och kontona kontrolleras mot golv och stop out med en gång. Det är enklare att förklara än villkor som följer med varje position, och samma som när en mäklare ändrar sina villkor.
- **Händelserna för öppnade och stängda positioner ändras inte**, så att journalen och facit är oförändrade. Terminalen räknar positionens hela provision från öppningens och stängningens händelser.
- **Handelsplattformens admin-API får `GET /instruments`, `GET /groups` och `PUT /groups/{id}/symbols`**, och propfirm-tjänsten visar dem som Trading conditions i adminpanelen. Firman väljer bland alla instrument på plattformen, som nu är tolv: sju valutapar till och silver.
- **Firmor har en `LoginUrl`** på handelsplattformen, där deras traders loggar in. Partner-API:t sätter den och om servern listas med `PATCH /tenants/{id}`. Propfirm-tjänsten sätter `LoginUrl` till portalens `/terminal` när firman har sin server, och listar servern när firman är live. Det som senast skickats sparas, så att det skickas igen efter ett avbrott och bara när något ändrats.
- **Terminalen leder firmans traders till portalen** när servern har en `LoginUrl`, med kontot som senast var öppet. Portalens `/terminal` öppnar terminalen med en engångslänk, eller visar portalens inloggning först. När sessionen i terminalen går ut skickas tradern direkt dit, så att den kommer tillbaka utan att märka något om den fortfarande är inloggad i portalen. En trader med ett eget lösenord kan logga in i terminalen som förut.
- **En ändrad konfiguration godtas efter en krasch** när uppspelningen efter den senaste ögonblicksbilden ger exakt samma händelser som journalen har. Det gör att instrument kan läggas till utan att tjänsten först måste startas med den gamla konfigurationen. En ändring som ger andra händelser stoppar starten som förut.

## Konsekvenser

- En ändring av villkoren påverkar traders mitt i en affär. Firman ser det i adminpanelen innan den sparar, men traderna får inget besked.
- Konfigurerade firmor, som `demo-firm` i utveckling, ändrar villkoren bara i konfigurationen.
- Alla firmors traders handlar fortfarande samma instrument från samma prisflöde. En firma kan välja bland dem, inte lägga till egna.
- Terminalen litar på adressen den får av handelsplattformen. Bara partnern som skapat firman, eller konfigurationen, kan sätta den.
