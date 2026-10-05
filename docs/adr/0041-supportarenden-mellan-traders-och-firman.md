# 0041. Supportärenden mellan traders och firman

- Status: Föreslagen
- Datum: 2026-10-05

## Sammanhang

En trader hade inget sätt att nå sin firma från portalen. Den som svarade på våra mejl kom till firmans supportadress, om firman hade angett en ([ADR 0033](0033-mejl-till-traders-i-firmans-namn.md)), men frågan hamnade då utanför plattformen, utan koppling till kontot den gällde, och firmans administratörer såg inte vad någon annan redan svarat. Firmor behöver svara på frågor om konton, utbetalningar och regler där de redan arbetar, och traders behöver se svaren där de följer sina konton.

## Beslut

- **En fråga är ett ärende i portalen.** Tradern öppnar det under Support med en rubrik, ett meddelande, valfritt ett av sina konton och upp till tre filer. Kontots sida har knappen "Ask about this account", som väljer kontot.
- **Ärendet är en konversation** som tradern och firmans administratörer skriver i tills någon av dem stänger det. Det har tre lägen: `Open` (väntar på firman), `Answered` (väntar på tradern) och `Closed`. Ett nytt meddelande öppnar ett stängt ärende igen, så det behövs ingen egen knapp för det. Firman kan svara och stänga i samma steg.
- **Firman svarar under Support i adminpanelen**, där ärendena som väntar visas med det som väntat längst först, antalet i menyn och en rad i Needs you på översikten. Svaren går i firmans namn. Tradern ser aldrig vilken administratör som svarade, men firman gör det.
- **Mejlen går genom utkorgen** ([ADR 0025](0025-e-post-genom-en-utkorg-och-notiser.md)) som två nya notisslag som firman kan stänga av. `firmSupport` går till administratörerna när ett ärende börjar vänta på firman: när det öppnas, eller när tradern skriver i ett besvarat eller stängt ärende. Fler meddelanden medan ärendet redan väntar ger inga fler mejl, så en trader som skriver flera gånger i rad ger ett mejl. `traderSupportAnswers` går till tradern med svaret när firman svarar. Mejlet ber tradern svara i portalen, men ett svar på mejlet går ändå till firmans supportadress.
- **Filerna är PDF, PNG eller JPEG**, kända från innehållet, högst 5 MB och tre per meddelande. De sparas krypterade med AES-GCM i databasen som firmornas dokument, eftersom en skärmbild eller ett dokument kan vara personligt, och hämtas bara från portalens egen adress, som nedladdning med `nosniff` och `Content-Security-Policy: sandbox`. Bilder visas små i konversationen.
- **Ett meddelande och dess filer skickas som ett formulär i ett anrop**, så att de sparas tillsammans eller inte alls.
- **En trader har högst tio ärenden som inte är stängda**, så att en trader inte kan översvämma firman.
- **Ärendena numreras per firma från 1.** Listorna läses en sida i taget med en markör av tid och id, eftersom kön sorteras på när ärendet började vänta och de andra listorna på när det senast skrevs i.
- **Vår personal ser inte ärendena.** De är mellan firman och dess traders.

## Konsekvenser

- En firma med ett eget supportsystem, till exempel Zendesk eller Freshdesk, kan inte koppla det än, och inte stänga av Support i portalen. Webhooks för ärenden kan komma senare.
- Firman kan bara svara, inte starta ett ärende till en trader. Mejlen till tradern från traderns kort finns kvar som förut.
- Filerna sparas så länge ärendet finns. Inget rensas än.
- Alla administratörer ser och svarar på alla ärenden. Tilldelning och interna anteckningar kan komma med roller.
- Texterna i portalen och mejlen är på engelska, som resten av portalen.
