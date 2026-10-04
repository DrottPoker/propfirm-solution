# 0021. Vi granskar firmor själva innan de går live

- Status: Föreslagen
- Datum: 2026-10-03

## Sammanhang

Produktplanen kräver en kontroll av bolag och ägare innan en firma får ta emot riktiga traders. Självbetjäning lockar annars firmor som tar avgifter och aldrig betalar ut, och det skadar vårt rykte. Hittills har en firma bara kunnat gå live i utveckling (ADR 0020). Planen tänkte sig en automatisk kontroll med manuell granskning. Vi börjar med att granska själva, eftersom det går fort att komma igång och vi lär oss vad som behöver kontrolleras innan något automatiseras. Vi behöver också kunna stänga av en firma som redan är live, till exempel om den lurar sina traders.

## Beslut

- **Firman skickar en ansökan** från sin adminpanel: bolagets uppgifter med momsnummer, eller att bolaget saknar ett, för bolag i EU, ägarna med minst 25 procent, länkar till sina villkor mot traders och annat vi kan kontrollera. Dokument som registreringsbevis är frivilliga. Vi ber inte om ID-handlingar.
- **Firman betalar en handpenning när den skickar.** Den sållar bort oseriösa firmor och betalar vår tid. Den dras av från startavgiften när firman går live, och betalas inte tillbaka om firman nekas. Den är en debitering av sorten `Deposit` i samma betalflöde som platserna, på en betalsida som också sparar kortet. Beloppet är en inställning, och 0 stänger av den.
- **Godkänn först, betala sedan.** Firman kan bara starta go-live när den är godkänd, så vi tar aldrig emot startavgiften från en firma vi sedan nekar. `Billing:AllowGoLiveWithoutVerification` tas bort.
- **Fem statusar:** `Draft`, `Submitted`, `ChangesRequested`, `Approved` och `Rejected`. Vi kan be om ändringar, och firman skickar igen utan ny handpenning. Att neka är slutligt.
- **Vår adminvy ligger i portalen på en egen adress** (`Platform:OpsUrl`, till exempel `ops.<vår domän>`), på samma sätt som registreringen ligger på plattformens adress. Vägarna i propfirm-tjänsten finns bara på den adressen, och personalens session har en egen cookie. Firmornas portaler och registreringen kan aldrig nå dem.
- **Personalen är egna användare** i tabellen `staff_users`, skilda från firmornas administratörer. De läggs in i konfigurationen tills inbjudningar och tvåstegsinloggning byggs med härdningen för produktion.
- **Dokumenten sparas krypterade i databasen** med AES-GCM och samma nyckel som firmornas hemligheter (`Secrets:Key`), med ett syfte per dokument. Formatet kontrolleras på innehållet. Filerna är små och få, så en separat fillagring behövs inte än.
- **Allt som händer sparas i `firm_events`**, med vem som gjorde det. Ansökan sparas som den var när den skickades, så ett beslut kan förstås i efterhand.
- **Avstängning pausar firmans challenges** som en obetald månad gör (ADR 0020): inga nya challenges, butiken stänger och kontona pausas på handelsplattformen. Traders kan stänga sina positioner och förlorar inga dagar. Pausen gäller så länge firman är avstängd eller månaden obetald.

## Konsekvenser

- En firma kan gå live i produktion, men först efter att vi har granskat den. Målet i produktplanen är ett dygn.
- Vi behöver bemanna granskningen. Personalen mejlas när en ansökan kommer.
- Kortnamnet `ops` reserveras, eftersom vår adminvy annars kan krocka med en firmas adress lokalt.
- Handpenningen är pengar vi tar emot innan vi levererar något. Villkoren och knappen säger att den inte betalas tillbaka.
- En automatisk kontroll mot bolagsregister eller en leverantör kan läggas till som ett steg före vår granskning, utan att flödet för firman ändras.
- Dokument med personuppgifter ligger i vår databas. De behöver omfattas av personuppgiftsbiträdesavtalet och en regel för hur länge de sparas.
