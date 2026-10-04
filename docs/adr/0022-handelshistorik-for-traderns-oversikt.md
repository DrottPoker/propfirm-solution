# 0022. Handelshistorik för traderns översikt

- Status: Föreslagen
- Datum: 2026-10-04

## Sammanhang

Traderns portal ska visa mer än kontots siffror just nu: saldot över tid, resultatet i dag och per dag, statistik över affärerna, de stängda affärerna och hur det gick i de faser som redan är klara. Handelsplattformen har varje affär i sin journal, men dess admin-API har ingen väg för ett kontos historik. Propfirm-tjänsten sparade bara saldot efter varje stängd affär, som indata i regelmotorns steg. Faserna och hur en challenge slutade fanns bara i stegen, och att leta i stegen för varje konto var femte sekund blir dyrt när kontona har handlat länge.

Portalen räknar aldrig pengar. Belopp kommer från propfirm-tjänsten och handelsplattformen.

## Beslut

- **Handelshistoriken är en egen läsmodell i propfirm-tjänsten.** `TradingHistoryRecorder` läser firmans händelseström och sparar positioner, saldoändringar och golvens nivåer för de konton som tjänsten har öppnat. Strömmen har redan alla fält som behövs, så handelsplattformen ändras inte.
- **Historiken har en egen läsposition.** Den läser strömmen för sig, skild från regelmotorns läsning. Då fylls historiken i bakåt för konton som fanns innan, och den kan byggas om från början genom att läsa strömmen igen. Varje händelse sparas en gång per löpnummer, så en ny läsning ändrar ingenting.
- **Faserna och slutet sparas när regelmotorn beslutar.** När en fas startar och klaras sparas tiden, saldot och handelsdagarna på fasens rad i `trading_accounts`, och beslutet som avslutade challengen sparas på kontot. Det görs i samma transaktion som steget. Konton som redan fanns fylls i från stegen när databasen uppdateras.
- **Tjänsten räknar och portalen visar.** Resultatet i dag och i fasen, statistiken, resultatet per dag och hur mycket som betalats ut räknas i propfirm-tjänsten med decimaltal. Portalen formaterar och färgar.
- **Equity sparas inte över tid.** Grafen visar saldot efter varje saldoändring och equity just nu.
- **Historiken hämtas när den har ändrats.** Kontot har en version som ändras med historiken och regelmotorns steg. Portalen hämtar grafen, statistiken och affärerna igen bara när versionen ändras, medan kontots siffror hämtas var femte sekund.

## Konsekvenser

- Historiken kan ligga någon sekund efter regelmotorn, eftersom de läser strömmen var för sig.
- Varje firma har två läsningar mot handelsplattformens ström.
- En handelsplattform från en annan leverantör behöver ge samma händelser med samma fält, eller en adapter som gör om dem.
- En kurva för equity kräver att equity sparas över tid, vilket inte görs än.
- Firmans adminpanel kan visa samma historik med samma frågor, men gör det inte än.
