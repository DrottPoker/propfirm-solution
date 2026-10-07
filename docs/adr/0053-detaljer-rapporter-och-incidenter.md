# 0053. Detaljer per affär, rapport per regelbrott och incidenter med statussida

- Status: Beslutad
- Datum: 2026-10-07

## Sammanhang

Punkt 5 i jämförelsen med TradeLocker, cTrader och TopstepX ([rapporten](../../reports/TradeLocker%20cTrader%20och%20TopstepX%20mot%20Kronant.md)). Det vanligaste klagomålet mot propfirmor är att en trader inte kan visa varför en affär fylldes där den gjorde, eller varför en gräns bröts, särskilt när priserna stannade en stund. Firmorna å sin sida får veta om ett avbrott hos plattformen genom sina traders, och har inget sätt att se vilka konton det nådde eller att göra något åt det. Journalen ([ADR 0008](0008-journal-av-indata.md)) sparar redan varje rått pris och varje händelse, men inget knöt ihop en händelse med priset bakom den, och inget visade det för tradern eller firman.

Designen godkändes som en duk med sju vyer: ett kvitto för en affär i terminalen, terminalen när priser saknas, rapporten i portalen, firmans incident, dialogen för att återställa, firmans statussida och vår vy för att deklarera en incident.

## Beslut

### Journalen knyter varje händelse till priset bakom den

- `engine_events` får `input_sequence`, löpnumret på indatan som orsakade händelsen. En fyllning, en stop loss och ett brutet golv pekar så på det råa priset i `engine_inputs`, som sparas med flödets namn.
- Händelser från före detta saknar löpnumret. För dem söks priset på tiden, och det godtas bara när skillnaden mot fyllningen är ett helt antal punkter, det vill säga påslaget. Annars står priset bakom som okänt.

### Detaljer per affär

- Handelstjänsten svarar med detaljerna för en position (`receipt` i API:t), till tradern och till firmans system: ordern den kom från, öppningen och varje stängning med tid, volym, pris och provision, flödets bid och ask bakom fyllningen med löpnumret i journalen, påslaget i punkter på den sida som fylldes, och flödets priser en minut före och efter, högst 240 punkter, för firmans egna system. En stängning med stop loss eller take profit har nivån den stod på, utom med trailing stop, där nivån flyttades.
- Terminalen och portalen visar detaljerna i en panel från sidan, Trade details, med knappen Details på varje stängd affär, för tradern och firmans administratörer. Båda förklarar en stop som stängde förbi sin nivå och kan skrivas ut, och terminalens kan kopieras som text. De visar ingen graf över priserna runt fyllningen, eftersom den inte sa mer än priserna själva. Designen kallade dem kvitto och portalen hade en egen sida för dem, men namnet Details säger bättre vad de är, och en panel räcker.

### Rapport per regelbrott

- Handelstjänsten svarar med rapporten för kontots senaste brutna golv: golvet, nivån, equity och saldot i ögonblicket, saldot efter att positionerna stängdes, priserna som bröt det med flödets priser och påslaget, de öppna positionerna, kontots händelser och equity pris för pris före brottet.
- Equity räknas om med motorns egen värdering (`Revaluation`) från de sparade priserna, så siffrorna är motorns och inte en uppskattning. Perioden börjar när den äldsta öppna positionen öppnades, men högst 6 timmar och minst 15 minuter före brottet. Kurvan har högst 600 punkter, med lägsta och högsta värdet i varje del, och slutar på equity i brottet. Perioder utan priser på minst en minut markeras. Rapporten sparas i minnet i 30 minuter.
- Terminalen och portalen visar den för tradern, och portalen för firman. Texterna säger nu att positionerna stängdes till priserna som bröt gränsen, som motorn gör, och inte till nästa pris.

### Terminalen när priser saknas

- Terminalen mäter hur gammalt varje pris är från när det kom fram, med webbläsarens klocka, så en felställd klocka spelar ingen roll. Ett pris som är äldre än en minut på en öppen marknad märks, och orderpanelen tar inte emot order på det, eftersom motorn ändå avvisar dem (`StalePrice`). Har inget pris kommit på en minut fast en marknad är öppen visas en gul ruta under kontoraden. Hur gammalt ett pris får vara kommer från tjänsten (`maxPriceAgeSeconds`).

### Incidenter

- Handelstjänsten svarar firmans system med vad en period gjorde med firmans konton (`GET /api/admin/v1/impact`): konton med positioner öppna när den började, förfrågningar som avvisades för att priset saknades eller var gammalt, och golv som bröts under perioden eller inom 30 minuter efter, med saldot och equity när den började och kontot nu. Perioden är högst ett dygn och börjar inom de senaste 30 dagarna, eftersom journalen läses för den.
- Partner-API:t säger hur prisflödet mår (`GET /api/partner/v1/price-feed`): när det senaste priset kom, totalt och per symbol, och om marknaden är öppen enligt öppettiderna.
- Propfirm-tjänsten frågar det var 15:e sekund. Har inget pris kommit på en minut fast en marknad är öppen skapas ett utkast till en incident, som börjar vid det senaste priset, och vår personal får ett mejl. När priserna kommer tillbaka får utkastet sitt slut. Vakten ser bara när hela flödet stannar. Att en enstaka symbol saknar priser syns för personalen men larmar inte, eftersom en helgdag annars skulle se ut som ett avbrott.
- Vår personal skriver, ändrar och publicerar incidenter i vår adminvy, för alla firmor eller några. Ett utkast kan avfärdas som falsklarm. Det sparas då som avfärdat, så att samma uppehåll inte hittas igen medan det pågår.
- En publicerad incident syns på firmans statussida och i dess adminpanel, och firmans administratörer får ett mejl. Medan den pågår visar firmans terminaler ett meddelande överst med vad vi senast sa, firmans egna ord och länken till statussidan. Meddelandet läggs som ett kommando för hela firman i samma kö som andra kommandon, och räknas om från alla pågående incidenter som gäller firman, den som började senast först. När den löses tas det bort.
- Firmans statussida är öppen för alla: om handeln fungerar nu, Trading, Prices, Terminal och Portal dag för dag i 30 dagar, och incidenterna de senaste 90 dagarna. Vilka delar en incident gäller följer av dess slag.

### Firmans beslut efter en incident

- Firmans adminpanel visar varje incident med firmans konton som den nådde och vad som hände varje konto. Firman bestämmer själv vad som görs. Inget återställs eller krediteras av sig självt.
- **Återställa:** en fas som ett golv brutet under incidenten avslutade öppnas igen på samma konto på handelsplattformen, så att historiken, affärernas detaljer och rapporten hänger ihop. Motorn får `ReopenAccount`, som öppnar ett avstängt konto med ett saldo och utan golv, och regelmotorn `ReinstateStage`, som gör challengen aktiv igen, behåller handelsdagarna eller börjar om dem, flyttar tidsgränserna lika många dagar som den var slut och sätter golven igen. Saldot är högst det största av saldot och equity när incidenten började och startsaldot. Tradern får ett mejl, som firman kan stänga av, och firman webhooken `account.reinstated`.
- **Kreditera:** ett belopp, högst startsaldot, sätts in en gång på ett konto som incidenten nådde och som fortfarande handlas på samma fas, till exempel för en stängning som avvisades.
- Varje beslut sparas med belopp, skäl, vem och när, och läggs bara till.

## Konsekvenser

- Tradern och firman kan visa exakt vilket pris från flödet som låg bakom varje fyllning och varje brott, med firmans påslag. Ingen av de tre i jämförelsen visar det.
- Firman får veta om ett avbrott från oss, ser vilka konton det nådde och fattar besluten i samma vy. Traderna ser samma sak på statussidan och i terminalen.
- Detaljerna för affärer från före detta kan sakna priset bakom fyllningen.
- Rapporten räknar om från sparade priser, som sparas högst var 250:e millisekund per symbol, så kurvan har den upplösningen. Den sträcker sig högst 6 timmar bakåt.
- Ett avstängt konto är inte längre stängt för gott: det kan öppnas igen när firman återställer en fas. Saldoändringen syns som `Reopened` i historiken.
- En kredit höjer saldot som vilken insättning som helst, och räknas därför mot vinstmålet och ger mer utrymme till gränserna. Det är firmans beslut.
- Påverkan går bara att räkna för incidenter som började de senaste 30 dagarna och för högst ett dygn av dem. Efter det kan firman inte längre återställa eller kreditera genom incidenten.
- Vakten larmar bara när hela flödet stannar. Ett avbrott för en del av symbolerna deklareras av personalen.
