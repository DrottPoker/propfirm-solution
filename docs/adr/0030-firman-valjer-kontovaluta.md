# 0030. Firman väljer kontonas valuta, och motorn räknar om genom USD

- Status: Föreslagen
- Datum: 2026-10-04

## Sammanhang

Alla firmor som registrerade sig fick konton i USD, eftersom mallgruppen på handelsplattformen är i USD. Många firmor i Europa vill erbjuda konton i EUR eller GBP. Motorn räknade bara om med ett instrument som har valutaparet, så ett konto i EUR kunde inte handla till exempel guld eller AUDUSD, där inget instrument har paret mot EUR.

## Beslut

- **Firman väljer kontonas valuta när den registrerar sig**, bland `Signup:Currencies` (USD, EUR och GBP). Valet sparas med registreringen och firman, och kan inte ändras efteråt, eftersom alla konton och challenges är i den.
- **Handelsplattformens partner-API tar en valuta när firman skapas.** Kopiorna av mallgrupperna får den i stället för mallens, och bara valutorna i `Tenancy:Currencies` godtas. Starten stoppas om någon av dem saknar ett instrument mot USD.
- **Motorn räknar om genom USD när inget instrument har paret.** Kursen är kursen till USD gånger kursen från USD, med mittpriserna som förut. Ett direkt par går alltid först, så konton i USD räknas som förut och facit är oförändrat.
- **Den första challengen och mallarna är i firmans valuta.** Priserna i butiken kan vara i vilken valuta som helst, som förut.
- **Vad firmorna betalar oss är kvar i USD** (`Billing:Currency`).

## Konsekvenser

- En omräkning genom USD använder två priser, så den följer båda. Det är vad mäklare gör, men kan skilja sig lite från ett direkt par.
- Fler kontovalutor kräver bara konfiguration, så länge de har ett instrument mot USD.
- En firma som vill byta valuta får registrera en ny firma. Det kan bli ett eget flöde senare.
