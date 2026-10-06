# 0050. Öppettider per instrument

- Status: Beslutad
- Datum: 2026-10-06

## Sammanhang

Motorn hade inga handelstider. Med riktiga prisflöden slutar priserna komma när marknaden stänger, och efter fem sekunder avvisades ordrar med `StalePrice`, "the price is too old, wait for the next one" ([ADR 0010](0010-prisflode-for-utveckling.md), [ADR 0049](0049-capital-com-och-fler-instrument.md)). En trader som försökte handla på helgen eller i en daglig paus fick alltså ett tekniskt fel i stället för beskedet att marknaden är stängd, och terminalen kunde inte säga när den öppnar igen.

Jämförelsen med TradeLocker, cTrader och TopstepX ([rapporten](../../reports/TradeLocker%20cTrader%20och%20TopstepX%20mot%20Kronant.md)) lade öppettider per instrument först bland det som behövs innan terminalen går i drift, eftersom avslag som traders inte förstår kostar förtroende. Licensierade priser och drift på en server väntar tills en prisleverantör är vald och servern köpt.

Börsernas tider räcker inte. Capital.com säljer CFD-kontrakt på index nästan dygnet runt och räknar fram egna priser när börsen är stängd ([Capital.com](https://capital.com/en-int/ways-to-trade/24h-trading)). Med bara börsens tider visade terminalen UK100 som stängd klockan 22 svensk tid medan Capital.com:s priser fortsatte att komma. Tradern handlar på flödets priser, så det är flödets tider som gäller.

## Beslut

- **Öppettider är en del av instrumentet** i motorns konfiguration (`Instrument.TradingHours`): marknadens tidszon, öppna perioder varje vecka och stängda perioder, till exempel helgdagar. Ett instrument utan öppettider är alltid öppet, som krypto.
- **Tiderna följer marknadens egen klocka.** En period som öppnar 17:00 i New York gör det hela året, också de veckor när USA och Europa byter till eller från sommartid vid olika datum. Tidszonen anges som en IANA-tidszon, till exempel `America/New_York`. En tid i timmen som klockan hoppar över flyttas till den första tiden som finns, och en tid i timmen som upprepas är den första av de två, som för handelsdagen i portalen.
- **Motorn avvisar med `MarketClosed`** order, stängningar och ändringar av stop loss och take profit medan marknaden är stängd. Det kontrolleras före priset, så en stängd marknad med gamla priser ger `MarketClosed` och inte `StalePrice`. Väntande ordrar går att ta bort även när marknaden är stängd, eftersom det inte kräver något pris.
- **Priser räknas även när marknaden är stängd.** Ett pris som kommer efter stängningen är riktigt, så det flyttar equity och kan utlösa stop loss, take profit, väntande ordrar, golv och stop out. Stänger firman ett konto medan marknaden är stängd stängs positionerna till senaste priset, som förut.
- **Konfigurationen** har namngivna öppettider i `Trading:TradingHours`, och varje instrument pekar på en av dem med `TradingHours`. Perioder skrivs som `Sun 17:00 - Fri 17:00` och stängda dagar som `2026-12-25`, eller som en del av en dag som `2026-12-24 13:15 - 2026-12-27 18:00`, i marknadens tidszon. Tjänsten vägrar starta om något inte går att läsa, om tidszonen är okänd, om perioderna överlappar, om marknaden aldrig stänger eller om ett instrument pekar på öppettider som saknas. Det kontrolleras med alla prisflöden, så att ett fel syns innan ett riktigt flöde används.
- **Tiderna följer prisflödet.** Varje instrument har börsens tider som standard, och ett prisflöde kan ha egna för vissa symboler i `Trading:TradingHoursByFeed`, till exempel `CapitalCom:UK100 = CapitalComIndices`. Ett tomt namn betyder att flödet ger priser dygnet runt för symbolen. Ett okänt flöde, en okänd symbol eller okända öppettider stoppar starten. När en prisleverantör för produktionen är vald läggs dess tider in på samma sätt.
- **Börsernas tider är standard:**

  | Öppettider | Instrument | Tider |
  |---|---|---|
  | `Forex` | Valutaparen | Söndag 17:00 till fredag 17:00 New York-tid |
  | `Globex` | XAUUSD, XAGUSD, US100, US500, US30, JP225, USOIL, NATGAS | CME Globex: 18:00 till 17:00 nästa dag New York-tid, söndag till fredag, alltså en timmes paus varje dag |
  | `IceBrent` | UKOIL | ICE Brent: söndag 23:00 till måndag 23:00, och tisdag till fredag 01:00 till 23:00 London-tid |
  | `IceFtse` | UK100 | ICE FTSE 100: måndag till fredag 01:00 till 21:00 London-tid |
  | `EurexDax` | DE40 | Eurex DAX: måndag till fredag 01:10 till 22:00 Berlin-tid |
  | Inga | BTCUSD, ETHUSD | Alltid öppna |

  Juldagen 2026 och nyårsdagen 2027 är stängda för alla marknader med öppettider, och för DE40 även julafton och nyårsafton, när Eurex är stängt.
- **Capital.com:s egna tider**, hämtade från deras API (`/api/v1/markets`, `openingHours`) 2026-10-06 och omräknade till New York-tid, eftersom de flyttas med USA:s sommartid. Sekunder avrundas till hela minuter, så 16:59:50 blir 17:00.

  | Öppettider | Instrument | Tider i New York-tid |
  |---|---|---|
  | `CapitalComForex` | Valutaparen | Söndag 17:00 till fredag 17:00, med en paus 17:00 till 17:05 varje dag |
  | `CapitalComMetals` | XAUUSD, XAGUSD | Söndag 18:00 till fredag 16:59, med en paus 16:59 till 18:00 varje dag |
  | `CapitalComIndices` | US100, US500, US30, DE40, UK100, JP225 | Söndag 18:00 till fredag 17:00, med en paus 17:00 till 17:05 varje dag |
  | `CapitalComBrent` | UKOIL | Söndag 18:00 till måndag 18:00, och måndag till torsdag 20:00 till 18:00 nästa dag, med fredagens stängning 17:00 |
  | `CapitalComCrypto` | BTCUSD, ETHUSD | Hela veckan, med en paus 17:00 till 17:05 varje dag och 01:00 till 03:00 på lördagar |
  | `Globex` | USOIL, NATGAS | Samma som CME, och samma som Capital.com |

  Capital.com:s helgdagar syns inte i API:t, som bara visar den aktuella veckan. Juldagen och nyårsdagen antas vara stängda för allt utom krypto.
- **Påhittade priser är alltid öppna.** Det syntetiska flödet går dygnet runt, så med det får motorn inga öppettider. Varje prisflöde säger själv om det följer öppettider (`IPriceFeed.FollowsTradingHours`). Tiingo och Capital.com gör det. Därför fungerar det syntetiska flödet på helgen som förut, och end-to-end-testerna, som kör med det, beror inte på veckodagen.
- **Terminalen hämtar öppettiderna** från `GET /api/accounts/{accountId}/market-hours`: för varje symbol om marknaden är öppen, när den stänger eller öppnar nästa gång, och de öppna perioderna en vecka framåt i UTC. Terminalen hämtar dem igen en sekund efter nästa ändring, minst varje timme och efter en återanslutning, också när fliken ligger i bakgrunden. Terminalen avgör aldrig själv om en marknad är öppen. Den visar vad tjänsten sa, och motorn bestämmer.
- **I terminalen** står "Closed" vid symbolen i bevakningslistan och i grafens huvud, och när marknaden öppnar står när muspekaren vilar där. Orderpanelen säger att marknaden är stängd och när den öppnar, och köp och sälj går inte att trycka på. En halvtimme innan marknaden stänger varnar orderpanelen. Raden Trading hours visar om marknaden är öppen, och med ett klick perioderna den här veckan i kontots tidszon. Positioner i en stängd marknad kan inte stängas eller ändras, och deras stopplinjer i grafen går inte att dra.

## Konsekvenser

- Traders får beskedet att marknaden är stängd och när den öppnar, i stället för ett tekniskt fel. Avvisningen säger "Refused: the market is closed".
- Öppettiderna ingår i konfigurationens fingeravtryck ([ADR 0008](0008-journal-av-indata.md)). Ett byte mellan prisflöden med olika tider, till exempel mellan det syntetiska flödet och Capital.com, ändrar därför konfigurationen. Efter en vanlig avstängning finns inget att spela upp. Efter en krasch startar tjänsten bara om uppspelningen med de nya öppettiderna ger samma händelser, annars ska den startas en gång med det gamla flödet först.
- Tidszonernas regler kommer från operativsystemet. Ändras reglerna för ett datum som redan har passerat kan en uppspelning ge andra händelser. Sådana ändringar gäller nästan alltid framtida datum.
- Helgdagarna måste läggas in för varje år, och listan har bara jul och nyår 2026-2027. Innan terminalen går i drift ska hela kalendern läggas in, från den licensierade prisleverantören eller börserna, med till exempel långfredagen, amerikanska helgdagar och dagar med tidig stängning. Tiderna ska också stämmas av mot leverantörens egna öppettider.
- Varje ny prisleverantör behöver sina tider inlagda, annars gäller börsernas. Kommer inga priser när öppettiderna säger öppet avvisas ordrar som förut med `StalePrice`, och kommer priser när de säger stängt nekas order fast priserna rör sig.
- Capital.com:s tider ändras inte automatiskt. Ändrar Capital.com dem måste konfigurationen ändras för hand.
- Firmor kan inte ändra öppettiderna. De beskriver marknaden, inte firmans villkor.
- Positioner ligger kvar över helgen och nätterna. Terminalen varnar en halvtimme innan marknaden stänger, men en regel som förbjuder positioner över helgen finns inte.
