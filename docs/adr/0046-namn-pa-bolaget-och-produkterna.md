# 0046. Namn på bolaget och produkterna

- Status: Beslutad
- Datum: 2026-10-06

## Sammanhang

Produkterna hade arbetsnamn: "Prop platform" på registreringen, i vår adminvy och i mejlen, "Trading terminal" i terminalen, och platshållarna `propbrand` och `tradebrand` för domänerna i [ADR 0018](0018-domaner-och-underdomaner.md). Namnen behövs innan domänerna köps, varumärket registreras och första servern sätts upp.

Handelsplattformen är vårt varumärke mot traders ([ADR 0009](0009-inloggning-och-firmor.md)). Propfirm-plattformen är white label mot traders, men vårt varumärke mot firmorna som köper den.

## Beslut

- **Bolaget heter Ludware.** Namnet är neutralt, så att bolaget kan ha fler mjukvaruprojekt än de här. Bolaget äger produkternas varumärken och domäner och står som säljare på fakturorna (`Billing:Seller`, [ADR 0032](0032-moms-och-fakturor.md)).
- **Produkterna delar namnet Kronant.**
  - **Kronant Trader** är handelsplattformen med terminalen (`productName` i `trading/terminal/src/lib/config.ts`).
  - **Kronant Prop** är propfirm-plattformen (`Platform:Name`). Firmorna ser namnet när de registrerar sig och i mejlen från oss, och vår personal i adminvyn. Traders ser det inte, eftersom portalen har firmans namn.
- **Ett gemensamt namn** ger ett varumärke att bygga i stället för två. Traders som känner igen Kronant Trader gör det lättare att sälja Kronant Prop till firmor. Match-Trade gör likadant med Match-Trader och Match-Prop.
- **Kronant** kommer från krona, valutan i Sverige, Norge, Danmark och Island, och kronan som vinnaren får. Slutet är detsamma som i engelskans pennant, vimpeln man vinner. Det uttalas "KROH-nant".
- **Domänerna** är `kronant.com`, `kronant.app` och `kronanttrader.com` ([ADR 0018](0018-domaner-och-underdomaner.md)). `kronant.se` och felstavningen `cronant.com` köps som skydd.
- **TXT-posten för en firmas egen domän** heter `_kronant.{domän}` ([ADR 0039](0039-egen-doman-for-firmans-portal.md)).

## Kontroller den 2026-10-06

- Alla domänerna ovan var lediga. De är inte köpta än.
- Inget varumärke innehåller "Kronant" i TMview, som samlar registren i EU, Storbritannien, Sverige, USA och WIPO. De närmaste är KRONAN (Kronan Mobility AB, cyklar, klass 9 i EU och Storbritannien) och KRONA (Krona Varv AB, klass 9 och 42 i EU). KRONOS används av många, bland annat av Danmarks Nationalbank i klass 36.
- Inget bolag eller varumärke heter Ludware.
- TMview är inget officiellt besked och hittar bara namn som innehåller ordet, inte namn som låter likadant.

## Konsekvenser

- Domänerna ska köpas, och Kronant ska registreras som EU-varumärke i klass 9 (mjukvara), 36 (finans) och 42 (mjukvara som tjänst) innan lansering. En varumärkesbyrå bör först bedöma KRONAN och KRONA.
- Ludware registreras hos Bolagsverket, som har sista ordet om bolagsnamnet.
- `Platform:Name` är kvar som inställning, med Kronant Prop som standard. `Email:FromName` sätts till samma namn.
- TXT-posten hette tidigare `_prop-platform.{domän}`. En firma som lagt in den gamla posten i utveckling får lägga in den nya.
- Interna namn som ingen kund ser behålls: förrådet `propfirm-solution`, partnern `prop-platform` på handelsplattformen och paketens namn.
