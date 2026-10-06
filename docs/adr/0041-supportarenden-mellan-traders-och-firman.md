# 0041. Supportärenden mellan traders och firman

- Status: Föreslagen
- Datum: 2026-10-05

## Sammanhang

En trader hade inget sätt att nå sin firma från portalen. Den som svarade på våra mejl kom till firmans supportadress, om firman hade angett en ([ADR 0033](0033-mejl-till-traders-i-firmans-namn.md)), men frågan hamnade då utanför plattformen, utan koppling till kontot den gällde, och firmans administratörer såg inte vad någon annan redan svarat. Firmor behöver svara på frågor om konton, utbetalningar och regler där de redan arbetar, och traders behöver se svaren där de följer sina konton.

## Beslut

- **En fråga är ett ärende i portalen.** Tradern öppnar det under Support med en rubrik, ett meddelande, valfritt ett av sina konton och upp till tre filer. Kontots sida har knappen "Ask about this account", som väljer kontot.
- **Ärendet är en konversation** som tradern och firmans administratörer skriver i tills någon av dem stänger det. Det har tre lägen: `Open` (väntar på firman), `Answered` (väntar på tradern) och `Closed`. Ett nytt meddelande öppnar ett stängt ärende igen, så det behövs ingen egen knapp för det. Firman kan svara och stänga i samma steg.
- **Firman svarar under Support i adminpanelen**, där ärendena som väntar visas med det som väntat längst först, antalet i menyn och en rad i Needs you på översikten. Svaren går i firmans namn. Tradern ser aldrig vilken administratör som svarade, men firman gör det.
- **Firman kan också skriva först**, till en av sina traders med traderns e-post, valfritt om ett av traderns konton, till exempel med en fråga om en affär. Traderns kort på kontots sida har länken "Write to the trader". Ärendet väntar då på tradern från början, tradern ser det som ett meddelande från firman och svarar i det som i andra ärenden. Ärendet minns vem som öppnade det (`opened_by`).
- **Mejlen går genom utkorgen** ([ADR 0025](0025-e-post-genom-en-utkorg-och-notiser.md)) som två nya notisslag som firman kan stänga av. `firmSupport` går till administratörerna när ett ärende börjar vänta på firman: när det öppnas, eller när tradern skriver i ett besvarat eller stängt ärende. Fler meddelanden medan ärendet redan väntar ger inga fler mejl, så en trader som skriver flera gånger i rad ger ett mejl. `traderSupportAnswers` går till tradern med firmans meddelande när firman svarar eller skriver först. Mejlet ber tradern svara i portalen, men ett svar på mejlet går ändå till firmans supportadress.
- **Filerna är PDF, PNG eller JPEG**, kända från innehållet, högst 5 MB och tre per meddelande. De sparas krypterade med AES-GCM i databasen som firmornas dokument, eftersom en skärmbild eller ett dokument kan vara personligt, och hämtas bara från portalens egen adress, som nedladdning med `nosniff` och `Content-Security-Policy: sandbox`. Bilder visas små i konversationen.
- **Ett meddelande och dess filer skickas som ett formulär i ett anrop**, så att de sparas tillsammans eller inte alls.
- **En trader har högst tio ärenden som tradern själv öppnat och som inte är stängda**, så att en trader inte kan översvämma firman. Ärenden som firman öppnat räknas inte, så att firman inte kan stänga ute en trader.
- **Ärendena numreras per firma från 1.** Listorna läses en sida i taget med en markör av tid och id, eftersom kön sorteras på när ärendet började vänta och de andra listorna på när det senast skrevs i.
- **Vår personal ser inte ärendena.** De är mellan firman och dess traders.
- **Firman har sparade svar.** Administratörerna sparar svar de ofta ger, med en kort rubrik och en text, och lägger in ett i svarsrutan i ett ärende för att ändra det innan det skickas. Svaren hör till firman, så alla dess administratörer delar dem. Texten kan ha `{trader}` och `{firm}`, som portalen fyller i med traderns namn, eller e-post när namn saknas, och firmans namn när svaret läggs in, så tjänsten sparar texten som den skrevs. En firma har högst 100 sparade svar, med olika rubriker. Bredvid ärendet visas också var kontot det gäller är nu, till exempel Phase 1, Funded, Failed eller pausat, så att firman inte behöver öppna kontot för att svara.

## Konsekvenser

- En firma med ett eget supportsystem, till exempel Zendesk eller Freshdesk, kan inte koppla det än, och inte stänga av Support i portalen. Webhooks för ärenden kan komma senare.
- Firman kan skriva till en trader som finns hos den, men inte till flera på en gång. Mejlen till tradern från traderns kort finns kvar som förut.
- Filerna sparas så länge ärendet finns. Inget rensas än. Därför är ärenden inte till för ID-handlingar: formuläret där firman skriver först säger att ID och lösenord aldrig ska begäras i ett ärende, och kontrollen av traderns ID ska vara ett eget steg ([ADR 0042](0042-id-kontroll-med-en-extern-tjanst.md)).
- Alla administratörer ser och svarar på alla ärenden. Tilldelning och interna anteckningar kan komma med roller.
- Texterna i portalen och mejlen är på engelska, som resten av portalen.
