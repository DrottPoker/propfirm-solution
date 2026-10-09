# 0057. Vår personalpanel för handelsplattformen

- Status: Föreslagen
- Datum: 2026-10-08

## Sammanhang

Kronant Trader hade ingen vy för oss som driver plattformen. Firmorna sköts genom admin-API:t och Kronant Props adminpanel, och vår adminvy i Kronant Prop ([ADR 0024](0024-var-adminvy-over-alla-firmor.md)) visar bara prisflödet på sidan om incidenter. Vi såg inte alla servrar på plattformen, hur prisflödet mår symbol för symbol, glappen i graferna, vad traders håller eller hur motorn och journalen mår. Vi kunde inte stoppa en admin-nyckel som kan ha läckt, och inte göra en server åt en firma som köper Kronant Trader utan Kronant Prop ([ADR 0009](0009-inloggning-och-firmor.md)).

Designen godkändes som en duk med nio vyer: översikten, listan med servrar, en server, dialogen för en ny admin-nyckel, prisflödet, instrumenten, exponeringen, motorn och översikten på en telefon.

## Beslut

### En egen app med en egen inloggning

- **Panelen är en egen Next.js-app**, `trading/staff`, på en egen adress (lokalt http://localhost:3003, i drift till exempel `ops.kronanttrader.com`). Den hör till handelsplattformen, så Kronant Trader kan drivas utan Kronant Prop ([ADR 0001](0001-monorepo-med-produktgranser.md)).
- **Vår personal finns i handelstjänstens egen tabell** `staff_users`, med en egen cookie (`trading_staff_session`) och ett eget inloggningsschema. En traders session når inte personalens API, och en personalsession når inga traders konton. Ett lösenord som byts avslutar sessionerna. I utveckling skapas `ops@test.com` med lösenordet `ops` från `Staff:SeedUsers`, som i Kronant Prop.
- **Personal-API:t** ligger under `/api/staff/v1` och ingår i kontraktet `contracts/trading/trading-service.json`, som appens typer genereras från.
- **Personalen ser kontonummer och belopp, aldrig vilka traderna är**, som i vår adminvy i Kronant Prop.
- **Utseendet är Kronants med den turkosa accenten** från vår adminvy i Kronant Prop ([ADR 0047](0047-ett-gemensamt-designsystem.md)), så panelen aldrig tas för terminalen. Överst på varje sida står vilken miljö det är (`NEXT_PUBLIC_ENVIRONMENT_NAME`), och alla tider är i UTC.

### Vad panelen visar

- **Översikten** börjar med det som behöver oss: hela prisflödet tyst, en symbol utan pris i en minut medan dess marknad är öppen, en firmas system som inte frågat efter sina händelser på 5 minuter medan händelser väntar, ett glapp i graferna som inte gick att fylla de senaste 7 dagarna, minst 1 000 inputs som väntar på motorn och en sparning av journalen som tagit minst en sekund de senaste 5 minuterna. Sedan nyckeltal för hela plattformen, prisflödet, motorn, den största nettoexponeringen och det senaste i plattformens logg.
- **Servrarna**: alla firmor med vem som gjorde servern (en partner, vi eller konfigurationen), om den listas, traders, konton som handlar, öppna positioner och när firmans system senast läste sina händelser, med grupper och sök.
- **En server**: nyckeltal, grupperna med villkor och öppna positioner per symbol, inloggningen och loggan, admin-nyckeln (vem som har den, när den gjordes och när den senast användes), meddelandet i terminalerna, de senaste händelserna och serverns del av loggen. Ett kontonummer som sökts fram visas med sina siffror och sina händelser.
- **Prisflödet**: varje symbols råa pris före påslag, hur gammalt det är, priserna den senaste minuten, marknaden och läget (live, tyst, stängd). Glappen i graferna och historiken.
- **Instrumenten**: veckan som en stapel per symbol, öppettiderna och de stängda dagar som finns i konfigurationen. Bara att läsa, eftersom de ändras i konfigurationen.
- **Exponeringen**: vad traders håller nu per symbol, lång minus kort, värderat i USD, för alla servrar eller en. De största positionerna och nettot per server. Motorn räknar värdena med sin egen värdering (`GetOpenPositions`).
- **Motorn**: kön, hur lång tid sparningen tar minut för minut, inputs per slag, journalen och ögonblicksbilderna, hur starten gick och partnerna.
- Allt räknas när det läses, och sidorna frågar igen var 5:e sekund.

### Vad personalen kan göra

- **Göra en server** åt en firma som använder Kronant Trader på egen hand, med en kopia av mallgrupperna som när en partner gör en. Nyckeln visas en gång. Servern sparas med vem som gjorde den (`tenants.created_by`), och konfigurationen kan aldrig ta över den.
- **Ta en server av listan eller sätta den på listan.** En partner kan lista den igen, till exempel när firman går live i Kronant Prop.
- **Stoppa en admin-nyckel** som kan ha läckt, med ett skäl som sparas. För en server som en partner gjort blir den nya nyckeln en som ingen ser, och partnern hämtar själv en ny genom partner-API:t när den gamla nekas. Kronant Prop gör det inom en minut och försöker anropet igen, se [specen för propfirm-tjänsten](../spec/propfirm-tjanst.md). För en server vi gjort visas den nya nyckeln en gång. En konfigurerad servers nyckel står i konfigurationen och går inte att stoppa här.
- **Be flödet om ett glapp igen, och ladda om hela historiken.** Graferna byggs då om medan nya priser väntar, och öppna terminaler laddar om sina grafer. Motorn får priserna som vanligt under tiden ([ADR 0056](0056-graferna-fyller-glapp-fran-flodets-historik.md)). Med syntetiska priser går det inte, eftersom de saknar historik.

### Mätningar och logg

- **`EngineMetrics`** räknar i minnet, minut för minut den senaste timmen: inputs per slag, nekade förfrågningar, den längsta kön och hur lång tid sparningarna tog. Priser och inputs per sekund räknas över de senaste 60 sekunderna. Mätningarna börjar om vid en omstart.
- **Plattformens logg** (`platform_log`) sparar servrar som görs, listas och tas av listan, nycklar som byts, starter, symboler och flöden som tystnar och kommer tillbaka, glapp som fylls eller inte fylls och historik som laddas om. Den läggs bara till i, och ett fel i loggen stoppar aldrig det den beskriver.
- **Glappen i graferna** sparas i `chart_gaps` med försök och utfall, så att de syns efter en omstart.
- När en firmas system senast använde sin nyckel och läste sina händelser, och när en partner senast anropade, hålls i minnet.

## Konsekvenser

- Kronant Trader kan drivas och säljas utan Kronant Prop: vi ser hela plattformen och kan göra servrar själva.
- Personalen har två adminvyer, en per produkt, med länk mellan dem, och loggar in i båda.
- Personal skapas bara från konfigurationen än så länge. Inbjudningar, inloggning i två steg och roller behövs innan produktion.
- Exponeringen och översikten läser alla positioner i motorns loop vid varje fråga. Det räcker för tusentals konton. Med många fler behövs ett sammandrag som hålls uppdaterat.
- Det som hålls i minnet är tomt efter en omstart. En firma vars system inte läst sina händelser sedan starten syns därför inte som sen.
- Instrument, öppettider och partners ändras fortfarande i konfigurationen, och kräver en ny release.
