# 0008. Journal av indata med ögonblicksbilder

- Status: Föreslagen
- Datum: 2026-10-02

## Sammanhang

Handelstjänsten måste kunna startas om, efter en uppdatering eller en krasch, utan att något går förlorat. En trader som har fått besked om en fyllning ska aldrig se den försvinna. Produktplanen kräver också att varje prisuppdatering och fyllning sparas, för tvister och förtroende.

Motorn är deterministisk (ADR 0005): samma indata ger alltid samma händelser.

## Beslut

- **Indata sparas, inte bara resultat.** Varje indata (priser och kommandon) får ett löpnummer utan luckor och sparas i Postgres tillsammans med händelserna det orsakade. Tillståndet kan alltid byggas upp igen genom att spela upp indata.
- **Inget släpps innan det är sparat.** Motorn tillämpar indata direkt, och en separat skrivare sparar dem i batcher. Svaret på ett kommando, dess händelser och svaret på en fråga som såg indatan släpps först när batchen är sparad.
- **Batcher växer under last.** Medan en batch skrivs samlas nya indata i nästa. Vid låg last blir fördröjningen en skrivning, och vid hög last delar många indata på samma skrivning.
- **Ögonblicksbilder** av hela motorns tillstånd sparas efter ett antal indata (standard 10 000), vid varje start och vid avstängning. En omstart läser den senaste och spelar upp indata efter den.
- **Kontroll vid omstart.** Antalet händelser som uppspelningen ger måste stämma med journalen, annars vägrar tjänsten starta. Det fångar både skadade journaler och motorkod som inte längre är deterministisk.
- **Konfigurationens fingeravtryck** sparas i varje ögonblicksbild. Om konfigurationen har ändrats och det finns indata att spela upp efter den senaste ögonblicksbilden vägrar tjänsten starta, eftersom uppspelningen annars skulle ge andra resultat än originalet.
- **Fel stoppar tjänsten.** Om journalen inte kan skrivas efter tre försök stoppas tjänsten. Annars skulle motorns minne och journalen glida isär.
- **Tider sparas i mikrosekunder**, som Postgres lagrar dem. Tjänsten avrundar varje tidsstämpel innan motorn ser den, så att en uppspelning ser exakt samma tider.
- **Priser lagras i egna kolumner** (symbol, bid, ask), eftersom de är de flesta raderna. Kommandon lagras som JSON.

## Konsekvenser

- En krasch förlorar inget som en klient har sett. Indata som tillämpats men inte hunnit sparas försvinner, men deras resultat har aldrig visats.
- Varje kommando väntar på en skrivning till databasen, vanligen några millisekunder.
- Journalen växer med alla priser. Rutiner för arkivering behövs senare.
- En ändring i motorns beteende ändrar resultatet av en uppspelning. Uppdateringar ska därför göras med en vanlig avstängning, som tar en ögonblicksbild, så att inga indata behöver spelas upp med ny kod.
- Diagrammens historik byggs om från sparade priser vid start.
