# Spec: personalpanelen för Kronant Trader

- Status: Implementerad i `trading/staff` och handelstjänstens personal-API
- Datum: 2026-10-08
- Beslut: [ADR 0057](../adr/0057-personalpanel-for-handelsplattformen.md)

## Syfte

Vår personal driver handelsplattformen härifrån: ser vad som behöver oss, alla servrar, prisflödet, instrumenten, vad traders håller och motorn, och kan göra servrar, ändra deras listning, stoppa en admin-nyckel som kan ha läckt och be prisflödet om historik igen. Panelen är vår, inte firmornas. Firmorna administrerar sina traders och konton i sina egna system, till exempel Kronant Props adminpanel.

## Inloggning

- Personalen loggar in med e-post och lösenord på `/login`. Ett fel svar säger "Wrong email or password." för både okänd e-post och fel lösenord. Samma begränsning av försök per minut som för traders.
- Sessionen har en egen cookie, `trading_staff_session`, och gäller i `Login:SessionLifetime` med glidande giltighet. Den når bara `/api/staff/v1`, och en traders session når inte dit.
- Varje sida i panelen kräver en inloggad. Utan session skickas man till `/login`, och en session som går ut medan en sida är öppen gör detsamma.
- Personal skapas från `Staff:SeedUsers` vid start. I utveckling finns `ops@test.com` med lösenordet `ops`.

## Sidorna

Varje sida har menyn till vänster (på en telefon en knapp överst), miljöns namn och sökningen överst. Sökningen öppnas med Ctrl+K (Cmd+K på Mac) och hittar servrar på id eller namn, ett konto på hela sitt nummer (med eller utan `#`) och panelens sidor. Ett konto öppnas på sin servers sida.

Menyn visar bredvid platserna det som behöver oss: hur många symboler som är tysta vid Price feed, hur många firmor som är sena vid Servers och att motorn är långsam vid Engine.

| Sida | Väg | Innehåll |
|---|---|---|
| Översikt | `/` | Det som behöver oss, nyckeltal (servrar, traders, konton som handlar, öppna positioner, positioner öppnade i dag med nekade förfrågningar, öppna terminaler), prisflödet med priser per minut den senaste timmen, motorn, den största nettoexponeringen och plattformens logg. |
| Servrar | `/servers` | Alla servrar, flest konton först, med typen av verksamhet (Prop firm, Broker, Practice eller Trading desk), grupperna Alla, Behöver oss, Listade, Inte listade och Från konfigurationen, och sök på id eller namn. Knappen New server. |
| En server | `/servers/{id}` | Nyckeltal, grupperna med villkor och öppna positioner per symbol, terminalen (typen, vilka delar den visar och om ordrar frågar först), inloggning, logga, admin-nyckeln, meddelandet i terminalerna, de senaste händelserna och serverns logg. Med `?account=` kontots siffror och bara dess händelser. Knapparna för listningen och en ny admin-nyckel, och Change the type för en server vi själva bestämmer typen på. |
| Prisflödet | `/price-feed` | Flödet nu, priser per minut, varje symbols råa pris med ålder, priser senaste minuten, marknaden och läget, glappen i graferna med Try again och historiken med Load the history again. |
| Instrumenten | `/instruments` | Alla instrument per kategori med valutor, kontrakt, decimaler, lotter, öppettider och veckan som stapel, öppettiderna och de stängda dagarna framåt. |
| Exponeringen | `/exposure` | Lång, kort, netto, tradernas öppna resultat och marginal i USD, per symbol med en stapel för lång eller kort, de största positionerna och nettot per server. Med `?server=` bara den servern. |
| Motorn | `/engine` | Kön, tid för sparning, inputs per sekund, öppna terminaler, tid för sparning minut för minut, journalen, ögonblicksbilderna, starten, inputs per slag den senaste timmen och partnerna. |

Sidorna frågar igen var 5:e sekund (servrarna var 15:e och instrumenten varje minut). Tider visas i UTC: klockslag i dag, "Yesterday 14:52", veckodag den senaste veckan och datum annars.

### Det som behöver oss

| Slag | När | Text |
|---|---|---|
| `FeedSilent` | Inget pris på en minut från någon symbol vars marknad är öppen | No prices from {flöde} for {tid} while markets are open |
| `SymbolSilent` | En symbol utan pris på en minut medan dess marknad är öppen, när flödet i övrigt ger priser | {symbol} has had no price for {tid} while its market is open |
| `EventsNotRead` | En firmas system har inte frågat efter sina händelser på 5 minuter, och händelser väntar | {server} has not read its events for {tid} |
| `ChartGapNotFilled` | Ett glapp i graferna, funnet de senaste 7 dagarna, som flödet inte gick att fråga om | A chart gap from {från} to {till} could not be filled |
| `QueueBehind` | Minst 1 000 inputs väntar på motorn | {antal} inputs are waiting for the engine |
| `SlowSaves` | En sparning av journalen tog minst en sekund de senaste 5 minuterna | Saving the journal took {tid} at worst in the last 5 minutes |

Varje rad har en förklaring och en länk dit man ser mer. Utan något visas "Nothing needs us now."

### Dialogerna

- **New server**: server (2 till 63 små bokstäver, siffror och bindestreck, kontrolleras medan man skriver), firmans namn, kontovaluta (`Tenancy:Currencies`) och typen av verksamhet med vad den betyder för firmans traders (ADR 0058). Ingen typ är förvald, så Make the server väntar tills en är vald. Serverns traders loggar in i terminalen med lösenord. Efter Make the server visas admin-nyckeln en gång med Copy, och Done öppnar serverns sida.
- **Change the type**: samma val av typ. Terminalens ord, delarna den visar och om ordrar frågar först följer typen, medan startstorleken, inloggningen, firmans sidor och riskvarningen står kvar. Terminalerna får den nya typen nästa gång de öppnas. Bara för servrar vi gjort och konfigurerade servrar vars konfiguration inte sätter terminalen: Kronant Prop håller sina firmor som propfirmor, och en konfiguration som sätter terminalen gör det igen vid varje start, vilket panelen säger i stället för knappen.
- **New admin key**: skäl (sparas i loggen) och serverns id för att bekräfta. För en server en partner gjort förklaras att partnern hämtar en ny nyckel själv, och ingen nyckel visas efteråt. För en server vi gjort visas den nya nyckeln en gång. En konfigurerad servers knapp är avstängd.
- **Stop listing / Put on the list**: förklarar vad traders ser, och att en partner kan lista servern igen.

## Personal-API:t

Alla vägar börjar med `/api/staff/v1`. Utom inloggningen kräver de personalens session. Se [specen för handelstjänsten](handelstjanst.md#personal) för varje väg.

## Konfiguration

| Inställning | Innehåll |
|---|---|
| `NEXT_PUBLIC_TRADING_API_URL` | Handelstjänstens adress. Standard http://localhost:5101. Appens adress måste stå i tjänstens `Cors:AllowedOrigins`. |
| `NEXT_PUBLIC_ENVIRONMENT_NAME` | Miljöns namn överst på varje sida, till exempel Production. Standard Development. |
| `NEXT_PUBLIC_PROP_STAFF_URL` | Vår adminvy i Kronant Prop, som menyn länkar till. Tom döljer länken. Standard http://ops.localhost:3002/ops. |

## Tester

- Enhetstester med Vitest för texterna: det som behöver oss, plattformens logg, händelserna, veckans stapel, stängda dagar och formateringen.
- Playwright (`pnpm e2e`) startar en egen handelstjänst på port 5131 med syntetiska priser och en egen databas, `trading_staff_e2e`, och appen på port 3031. Testerna loggar in och ut, ser översikten med en position, gör en server av typen Practice, byter den till Broker och stoppar dess nyckel, listar en partners server, ser att Kronant Prop bestämmer dess typ och stoppar dess nyckel utan att någon nyckel visas, söker fram ett konto och ser dess händelser, går igenom prisflödet, instrumenten, exponeringen och motorn, och använder menyn på en telefon. Med `STAFF_SCREENSHOTS` satt till en mapp sparas varje sida som bild där.

## Begränsningar

- Personal skapas bara från konfigurationen. Inbjudningar, inloggning i två steg och roller saknas.
- Instrument, öppettider och partners går bara att läsa.
- Incidenter deklareras fortfarande i vår adminvy i Kronant Prop.
