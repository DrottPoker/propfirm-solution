# 0002. C# i backend och TypeScript i webben

- Status: Beslutad
- Datum: 2026-10-02

## Sammanhang

Handelsmotorn räknar pris, vinst, swap, avgifter, marginal och valutaomräkning vid varje prisuppdatering. Fel i de beräkningarna kostar kunderna pengar och förtroende. Motorn har också många tillstånd: ordertyper, positioner och kontots livscykel.

Vi jämförde TypeScript, C# och Go:

- **TypeScript** saknar inbyggda decimaltal. Pengar kräver bigint eller ett bibliotek och mycket disciplin. Typerna finns inte när programmet körs.
- **Go** saknar inbyggda decimaltal och unionstyper, vilket gör domänen svårare att modellera.
- **C#** har inbyggt `decimal` som räknar exakt i bas 10, bra stöd för att modellera domänen (records och mönstermatchning), hög prestanda och ett moget ekosystem för tjänster. Steget från TypeScript är litet.

## Beslut

- **Backend:** C# på .NET 10 (LTS) med ASP.NET Core. Gäller handelsmotorn, regelmotorn och API:erna.
- **Pengar och priser:** alltid `decimal`, aldrig `double` eller `float`.
- **Tester:** xUnit v3 på Microsoft.Testing.Platform.
- **Webb:** Next.js 16 med TypeScript, React och Tailwind CSS. Paket hanteras med pnpm.
- **Kodkvalitet:** varningar räknas som fel, .NET-analysatorerna är påslagna, kodstilen kontrolleras med `dotnet format` och ESLint. Paketversioner styrs centralt i `Directory.Packages.props`, och låsfiler gör återställningen reproducerbar.

## Konsekvenser

- Två språk i projektet. Webben pratar med backend genom genererade klienter, så att typerna inte skrivs två gånger.
- Den som underhåller backend behöver kunna C#.
- .NET 10 har stöd till november 2028. Uppgradera till nästa LTS-version innan dess.
