# 0056. Graferna fyller glapp från prisflödets historik

- Status: Föreslagen
- Datum: 2026-10-07

## Sammanhang

Graferna byggs av flödets historik, som laddas när tjänsten byter flöde, och av minutstaplar från livepriserna ([ADR 0048](0048-grafernas-historik-per-prisflode.md)). Medan tjänsten är avstängd kommer inga priser, och samma sak gäller när anslutningen till prisleverantören bryts en stund medan tjänsten kör. Efter en omstart eller ett avbrott hade graferna därför ett hål, och priset hoppade från där det var när priserna slutade till där det var när de kom igen. Historiken laddades bara om vid nästa byte av flöde.

Graferna är bara för visning. Motorn och journalen ska inte få priser i efterhand, eftersom kontona byggs om från journalen och en stop loss som borde ha nåtts under avbrottet inte kan fyllas i efterhand till ett pris som redan passerat.

## Beslut

- **Ett glapp är en hel minut eller mer utan något pris alls,** från något instrument i flödet. Det gäller efter en omstart, mellan de sparade staplarna och de första priserna, och medan tjänsten kör, när flödet varit tyst. Minuten med det sista priset före glappet och minuten med det första efter det har livepriser, så det är minuterna mellan dem som fylls.
- **Glappet fylls från flödets historik** när priserna kommer igen, med samma `GetHistoryAsync` som vid ett byte: minutstaplar för de senaste dagarna, staplar på 15 minuter och en timme längre bak, högst så långt som `Charts:History` når. Staplarna sparas i `chart_bars`, så att nästa start har dem.
- **Motorn väntar aldrig.** Priserna går till motorn direkt. Graferna håller tillbaka sina priser medan glappet hämtas, så att staplarna kommer i tidsordning, och lägger in dem efter glappets staplar. Under tiden sparar inspelaren inga minuter, och en omstart spelar upp dem från journalen.
- **Terminalerna laddar om graferna** när ett glapp fyllts, på SignalR-meddelandet `Charts` till alla terminaler, så att hålet försvinner utan att tradern laddar om sidan.
- **Ett glapp som flödet inte har historik för står kvar.** Säger leverantören nej, till exempel för att det blivit för många anrop, försöker tjänsten igen efter 2, 10 och 30 sekunder. Går den inte att nå då heller, eller har den inga staplar, till exempel för en stängd marknad, läggs priserna efter glappet in ändå och felet loggas.
- **Capital.com:s sessioner startar minst 1,2 sekunder isär.** Capital.com tillåter en ny session i sekunden, och historiken öppnar sin egen direkt efter strömmens när ett glapp fylls efter en omstart. Utan spärren svarade Capital.com 429 på historikens session.
- **Det påhittade flödet fyller inga glapp.** Det fortsätter från de senaste priserna efter en omstart, så graferna hoppar inte, och dess historik är påhittad.

## Konsekvenser

- Efter en omstart eller ett avbrott i flödet visar graferna leverantörens priser för tiden emellan, oftast några sekunder efter att priserna kommit igen.
- Varje glapp kostar anrop till leverantören, för Capital.com ett per instrument och 950 staplar. Ett glapp när alla marknader varit stängda, som en helg med bara valutor, ger tomma svar.
- Staplarna i glappet har ingen tickvolym, som all historik från leverantörerna.
- Ett glapp äldre än de senaste dagarna fylls i längre staplar, och den första av dem kan saknas om glappet inte börjar på hel kvart eller timme.
- Vad som kom av varje glapp sparas, och vår personal kan be flödet om ett glapp igen eller ladda om hela historiken i personalpanelen ([ADR 0057](0057-personalpanel-for-handelsplattformen.md)).
