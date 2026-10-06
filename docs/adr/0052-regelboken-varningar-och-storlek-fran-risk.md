# 0052. Regelboken i terminalen, varningar, storlek från risk och inställningar på kontot

- Status: Beslutad
- Datum: 2026-10-07

## Sammanhang

Punkt 3 och 4 i jämförelsen med TradeLocker, cTrader och TopstepX ([rapporten](../../reports/TradeLocker%20cTrader%20och%20TopstepX%20mot%20Kronant.md)). Terminalen visade vinstmålet och förlustgränserna, men handelsdagarna, tidsgränsen, sista dagen att öppna en position och konsekvensregeln fanns bara i portalen. Ingen sa till när en gräns eller en tidsgräns kom nära. Tradern räknade själv ut volymen för en viss risk, och terminalen visade bara risken för vald volym. Favoriter, volymer, indikatorer och ritningar sparades i webbläsaren på enheten ([ADR 0051](0051-orderverktyg-och-grafens-verktyg.md)), så de saknades på en annan dator eller telefon.

## Beslut

### Regelboken kommer från firmans system

- Propfirm-plattformen räknar fram reglerna ur regelmotorns tillstånd (`TerminalRules`) och berättar dem för handelsplattformen med `PUT /api/admin/v1/accounts/{id}/rules`: om kontot är finansierat, hur många handelsdagar som krävs och hur många som räknats, när steget måste vara klart (`passBy`), när en ny position senast måste öppnas (`openPositionBy`), konsekvensregeln och bästa dagens andel av vinsten. Tiderna är början på handelsdagen i UTC och finns bara medan steget handlas.
- Reglerna köas som ett kommando (`DescribeTradingRules`) efter varje steg i regelmotorn, men bara när de ändrats. Det som senast berättades sparas på kontot (`challenge_accounts.described_rules`). Konton som öppnades innan detta byggdes får sina regler vid start, som namnen i [ADR 0035](0035-terminalen-visar-kontot-som-portalen.md).
- Handelstjänsten sparar reglerna per konto (`account_rules`), skickar dem direkt till terminalen över SignalR (`Rules`) och svarar med dem på `GET /api/accounts/{id}/rules`. Varje fält ersätts, och en firma som inte berättar några regler har alla tomma. Reglerna ligger utanför motorn och journalen, eftersom de bara är för visning och varningar.
- Terminalen visar hela regelboken bakom knappen Rules i kontoraden: vinstmålet, förlustgränserna med utrymmet kvar, handelsdagarna, sista dagen att klara steget eller att steget saknar tidsgräns, sista dagen att öppna en position och bästa dagens andel mot konsekvensregeln. Varje rad har ett läge (ok, nära, akut eller klar), och knappen får en prick när något behöver uppmärksamhet. Det som firmans system inte har berättat visas inte.

### Varningar i terminalen, med ljud

- Terminalen varnar med en notis i hörnet och ett ljud när en förlustgräns har under 25 % kvar av sitt avstånd och igen under 10 %, när en gräns bryts, när vinstmålet nås, när en tidsgräns är mindre än tre dagar bort och igen när den är mindre än en dag bort, och när bästa dagen går över konsekvensregeln.
- En gräns räknas som längre bort igen först 10 procentenheter över tröskeln, så att equity som pendlar kring en tröskel bara varnar en gång. Gränserna och färgerna i kontoraden följer samma trösklar.
- Det som redan gäller när terminalen öppnas visas utan ljud, utom förlustgränserna, som kontoraden redan färgar. En nyare notis om samma regel ersätter den förra, och notiserna stängs när tradern öppnar regelboken.
- Ljudet är två fallande toner och går att stänga av med Sound on warnings i menyn bakom initialerna. Det är på från början. Webbläsarens egna notiser används inte.
- Varningarna räknas i terminalen från kontot och reglerna, inte på servern.

### Storlek från risk

- Orderbiljetten väljer storlek i Lots, i kontots valuta, i procent av saldot (% balance) eller i procent av utrymmet till närmaste förlustgräns (% room), oftast dagens.
- Med en risk räknar terminalen fram den största volymen i instrumentets steg som förlorar högst risken vid stop lossen. Avståndet räknas från där ordern öppnar, ask för ett köp, bid för en sälj och orderpriset för en väntande order, och värdet av en punkt kommer från motorn. En volym över instrumentets största sänks till den, och en risk som är för liten för den minsta volymen sägs ifrån.
- Med risk skrivs stop loss och take profit som priser, eftersom ett belopp för stop lossen redan vore risken.
- Volymen och risken är uppskattningar före provision, som beloppen för stoppar.

### Inställningarna följer tradern

- Handelstjänsten sparar traderns inställningar per inloggning: `GET /api/me/settings`, `PUT /api/me/settings/{key}` med vilket JSON-värde som helst och `DELETE /api/me/settings/{key}`. Högst 200 nycklar och 64 KB per värde, och nycklarna har bokstäver, siffror, punkter, bindestreck och understreck. Terminalen bestämmer vad varje nyckel innehåller.
- Terminalen sparar som förut i webbläsaren, så att allt finns direkt, och skickar varje ändring till inloggningen efter en sekund. Innan terminalen visas hämtas inloggningens inställningar, och de vinner över webbläsarens, utom över ändringar som inte hunnit skickas. De sparas som osända i webbläsaren tills tjänsten har tagit emot dem, så att en omladdning eller ett avbrott inte tappar dem. Första gången en webbläsare används skickas det den redan har. En webbläsare som har en annan traders inställningar tar den inloggades.
- Med följer favoriterna, volymen per symbol, volymen under grafen, båda ljudvalen, indikatorerna, ritningarna per symbol och valet av storlek. Servern och kontot som senast användes stannar på enheten.

## Konsekvenser

- Tradern ser hela regelboken och blir varnad där handeln sker, utan att byta till portalen. Det utökar försprånget mot de tre, där regelboken är svår att hitta eller saknas.
- Varningarna kräver att terminalen är öppen. Pushnotiser när den är stängd hör till punkt 7 i rapporten. Skydden ligger som förut i motorn.
- Reglerna i terminalen är de som propfirm-plattformen senast berättade. Står kommandokön still, till exempel när handelsplattformen inte svarar, syns de gamla tills kön kommit ikapp. En firmas eget system kan berätta reglerna med samma anrop, men måste då hålla dem aktuella själv.
- Ändras samma inställning på två enheter samtidigt vinner den som skickas sist, utan sammanslagning. Ritningar på samma symbol från två enheter kan alltså skriva över varandra.
- ADR 0051:s konsekvens att indikatorer och ritningar följer enheten gäller inte längre.
- Storleken från risk är en uppskattning. Växelkursen när positionen stänger, provisionen och en fyllning en bit från priset gör förlusten något annorlunda.
