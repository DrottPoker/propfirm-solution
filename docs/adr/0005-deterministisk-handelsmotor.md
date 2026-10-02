# 0005. Deterministisk handelsmotor

- Status: Föreslagen
- Datum: 2026-10-02

## Sammanhang

Traders kan ifrågasätta fyllningar och regelbrott. Vi behöver kunna visa exakt vad som hände och varför. Motorn måste också gå att testa mot inspelade priser och kunna byggas om utan att resultaten ändras.

## Beslut

Kärnan i handelsmotorn (`trading/src/Trading.Engine`) är deterministisk: samma händelser in ger alltid samma resultat ut.

- **Ingen I/O.** Kärnan läser inte filer, nätverk eller databas. Tjänsten runt kärnan (`Trading.Service`) sköter det.
- **Ingen systemklocka.** Tiden kommer från händelserna, till exempel prisuppdateringens tidsstämpel.
- **Ingen slump och inga genererade id:n.** Id:n kommer från kommandona.
- **En tråd.** Händelserna behandlas en i taget i tur och ordning. Mer kapacitet fås genom att dela upp kontona på flera instanser.
- **Pengar och priser som `decimal`.**

Reglerna kontrolleras vid bygget med `Microsoft.CodeAnalysis.BannedApiAnalyzers` och listan i `BannedSymbols.txt`. Analysatorn fångar inte `double` i deklarationer. Därför läggs ett test till i fas 1 som kontrollerar att kärnan inte använder flyttal.

## Konsekvenser

- Varje tvist kan spelas upp exakt från loggade priser och ordrar.
- Tester kan köras mot inspelade prisdata och ge samma resultat varje gång.
- Om motorn någon gång skrivs om fungerar uppspelningstesterna som facit.
- Tjänsten runt kärnan måste lägga tidsstämplar och id:n på allt som går in.
