# 0035. Terminalen visar kontot som firmans portal

- Status: Föreslagen
- Datum: 2026-10-05

## Sammanhang

I genomgången som ny firma hette terminalen "Trading terminal", kontot visades som `nordic-edge-1001-1` medan portalen sa #1001, firman syntes bara längst ned och det fanns ingen väg tillbaka till portalen. Vinstmålet syntes inte, gränserna hette "Daily floor" i terminalen och "Daily loss room" i portalen, "Live" kunde läsas som ett riktigt konto, och när en gräns bröts stod bara ett litet "Disabled" kvar med "-49.00 left" och händelser med kodord som "(EquityFloor)".

Terminalen är vårt varumärke och gemensam för alla firmor (ADR 0009), och handelsmotorn vet inget om challenges. Namnet på kontot, vinstmålet och portalens adress finns i propfirm-plattformen.

## Beslut

- **Kontots uppgifter för terminalen ligger i handelstjänsten, utanför motorn.** Propfirm-tjänsten sätter dem med admin-API:t `PUT /api/admin/v1/accounts/{id}/details` direkt efter att ett konto öppnats: namnet som i portalen ("#1001 Two-step 100K, Phase 1"), vinstmålet som saldo, handelsdagens tidszon och adressen till kontot i portalen. De sparas i en egen tabell och kommer till terminalen med `GET /api/auth/me`. Motorn och journalen påverkas inte, eftersom uppgifterna bara är för visning.
- **Konton som öppnades innan** får sina uppgifter en gång när propfirm-tjänsten startar, och vilket konto som senast fick dem sparas, så att inget beskrivs två gånger.
- **Firmans logga** skickas med listningen genom partner-API:t, som `loginUrl` (ADR 0027), och visas i kontoraden med firmans namn när loggan saknas. Vårt namn finns kvar på inloggningssidan.
- **Kontoraden** har länken "Back to {firma}" till kontot i portalen, vinstmålet med hur mycket som är kvar, gränserna med portalens namn (Daily loss limit, Max loss limit) och "X left". Anslutningen står i statusraden längst ner, som Live eller Offline ([ADR 0047](0047-ett-gemensamt-designsystem.md)).
- **Ett avslutat konto** får en tydlig ruta med vilken gräns som bröts, när och vid vilken equity, och en knapp till kontot i portalen. Negativa belopp visas aldrig: en bruten gräns visar "Broken". Ett tidigare "Buy filled." försvinner.
- **Händelser och avvisningar står i vanliga ord**, till exempel "closed by the loss limit" och "Refused: not enough free margin". Ett skäl terminalen inte känner till visas som motorn skrev det.

## Konsekvenser

- Terminalen och portalen använder samma namn för kontot och gränserna, och tradern hittar tillbaka.
- Handelstjänsten har en ny tabell och en ny väg i admin-API:t, båda en del av kontraktet för andra plattformar som vill använda terminalen.
- Ett konto som öppnas medan terminalen är öppen visas med sitt id tills sidan laddas om.
- En firma som byter till en egen domän (ADR 0039) får den nya adressen i terminalens länk tillbaka för konton som öppnas efteråt. Äldre länkar går till adressen hos oss, som fortsätter att fungera.
