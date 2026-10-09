# 0059. Tak för anrop, en enda skrivare och mätt omstart

- Status: Accepterad
- Datum: 2026-10-10

## Sammanhang

Översikten över Kronant Trader i drift tog upp tre saker som bör finnas vid lansering: handels- och admin-API:t hade inget tak för hur mycket en klient kan skicka, bara inloggningen hade det. Två handelstjänster mot samma databas stoppades bara av att den andras skrivning krockade med en primärnyckel. Och ingen visste hur lång tid en omstart tar med riktig mängd priser och konton.

Med Capital.com kommer cirka 40 inputs i sekunden när marknaderna är öppna, så 10 000 inputs, intervallet mellan ögonblicksbilder, motsvarar ungefär fyra minuter. Utvecklingsdatabasen startade på under en kvarts sekund, men den har bara några få konton.

Mätningen med många konton visade ett större problem än omstarten. Motorn värderade varje konto på varje pris, upp till tre gånger per konto. Med 1 000 konton med två öppna positioner vardera tog ett pris cirka 3 millisekunder, och en omstart efter en krasch 28 sekunder. Med några tusen konton skulle motorn inte hinna med priserna alls.

## Beslut

**Tak för anrop.** Varje anropare har en egen ranson per minut: en trader från alla sina enheter tillsammans (1 200, för terminalen, inställningarna och realtidshubben), en firmas system med sin nyckel (1 200, admin-API:t), en partner (600) och varje IP-adress som söker servrar utan att vara inloggad (60). En fjärdedel av minutens ranson får användas på en gång, resten kommer tillbaka jämnt över minuten. Den som tagit slut på sin ranson får `429` med `Retry-After` och `reason: "TooManyRequests"`, i samma form som ett nekat kommando, och terminalen säger "too many requests at once, wait a moment and try again". Kronant Prop behandlar redan `429` som att plattformen inte svarar just nu och försöker igen. Ransonerna sätts under `RateLimits`, och 0 stänger av en.

**En enda skrivare.** Handelstjänsten tar ett lås på journalen innan den läser något: ett advisory lock i Postgres som hålls av den anslutning batcharna skrivs på. En andra tjänst mot samma databas väntar upp till `Journal:WriterLockWait` (30 sekunder, så att en tjänst som stängs hinner skriva sin sista ögonblicksbild) och startar sedan inte. Bryts anslutningen följer låset med, och tjänsten tar det igen innan nästa batch. Har en annan tjänst tagit det under tiden skrivs inget, och tjänsten stoppas som vid andra fel i journalen. Låset släpps när den sista batchen är skriven.

**Priset värderar bara de konton det rör.** Ett kontos värde beror bara på de senaste priserna för symbolerna dess positioner prissätts och växlas med, och vilka symboler det är följer av konfigurationen. Ett pris värderar därför de konton det kan ändra och de som ett kommando eller en ny handelsdag ändrat sedan sist. Motorn håller ett register över vilka konton varje symbol rör och går bara igenom alla konton när en ny handelsdag kan börja. Ett konto värderas en gång per pris i stället för tre. Händelserna blir exakt desamma, i samma ordning, som när varje konto värderas på varje pris, så gamla journaler spelas upp som förut. Ett test kör tusentals slumpade priser och kommandon genom motorn och genom en motor som läses in från sitt eget tillstånd före varje input, och därmed värderar alla konton, och kräver samma händelser.

**Ögonblicksbilderna tas som förut.** Efter ändringen tar en omstart efter krasch, med ett helt intervall att spela upp, så här lång tid:

| Konton med två positioner vardera | Ett pris | Omstart | Ögonblicksbild |
|---|---|---|---|
| 1 000 | 0,3 ms | 2,3 s | 1 MB |
| 5 000 | 1,1 ms | 11 s | 5 MB |
| 20 000 | 4,7 ms | 49 s | 19 MB |

Intervallet stannar på 10 000 inputs. Tätare ögonblicksbilder skulle korta omstarten men skriva flera megabyte i minuten, som också går till säkerhetskopiorna. Mätningen finns kvar som test och körs vid varje push med 1 000 konton.

## Konsekvenser

- En klient som spårat ur bromsas utan att andra märker det, och sökningen på servrar går inte att använda för att räkna upp alla firmor.
- En andra handelstjänst kan aldrig skriva i en journal som en annan redan skriver i, också om någon startar den av misstag.
- Motorn klarar många fler konton på samma server. Kostnaden per pris följer nu de konton priset rör, och kostnaden per konto är själva värderingen.
- Blir omstarten för lång när kontona blir fler går `Journal:SnapshotInterval` att sänka. Personalpanelen visar hur lång den senaste starten var.
- Ransonerna för en trader delas av alla dess flikar och enheter. En terminal gör ett sextiotal anrop när den öppnas, så tre omladdningar i rad ryms med marginal.
