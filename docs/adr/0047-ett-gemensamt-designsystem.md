# 0047. Ett gemensamt designsystem med djup och rörelse

- Status: Föreslagen
- Datum: 2026-10-06

## Sammanhang

Genomgången av UI och UX (`reports/Genomgång av UI och UX.md`) fann att portalen och terminalen ser ut som tusen andra mörka Next.js-appar: typsnittet Geist, Tailwinds standardblå, platta kort utan skuggor, belopp i monospace med överstrukna nollor, nästan ingen rörelse, egenritade ikoner och ingen logga för Kronant. Portalen och terminalen hade dessutom var sina färger, rundningar och knappar.

Portalen är white label: en firmas traders ska se firmans färger och logga ([ADR 0014](0014-vitmarkt-portal-pa-firmans-adress.md)). Terminalen, plattformens egna sidor och vår adminvy är våra ([ADR 0046](0046-namn-pa-bolaget-och-produkterna.md)).

## Beslut

- **Kronants utseende** är grafit, varm benvit text och mässing (`#c9a35b`), "nordisk privatbank". Plattformens sidor, vår adminvy (med en egen turkos accent, så att den aldrig tas för en firmas) och terminalen har det. Märket är en krona på ett mynt i mässing, kronan som Kronant är uppkallat efter.
- **En firmas portal** behåller firmans färger. Standardtemat för en firma som inte valt egna är neutral grafit med den blå varumärkesfärgen.
- **Typsnitt**, laddade med `next/font`:
  - Familjen Grotesk, från en byrå i Stockholm, för text och siffror. Siffrorna är lika breda som varandra, så belopp står i kolumner utan monospace, och punkt och komma förblir smala. Schibsted Grotesk prövades först, men dess tabellsiffror gör även punkt och komma breda ("100,000 . 00").
  - Instrument Serif för sidornas rubriker och de stora ögonblicken.
  - JetBrains Mono för kod, nycklar, adresser och terminalens priser.
- **Ett gemensamt paket** i `shared/web/design` (`@kronant/design`) har tokens som båda apparna importerar i sin `globals.css`: Kronants färger, skuggor i tre nivåer med en ljus kant överst, ett fint brus, easing och tider, animationerna, `stagger` och `skeleton`. Skuggorna följer temats `color-scheme` med `light-dark()`.
- **Rörelse** är kort och lugn, avstannande, och står helt still för den som valt minskad rörelse i systemet: sidor kommer in i en kort kaskad, staplar fylls, flikarnas markering glider, siffror rullar till sina nya värden, laddning visas som skelett, och det som sparas bekräftas med en kort notis.
- **Paket**:
  - `motion` för flikarnas markering och det som byter plats.
  - `@number-flow/react` för siffror som rullar.
  - `@phosphor-icons/react` för ikonerna: duotone för saker och platser, fetare för små symboler.
  - `sonner` för notiserna.
  - `canvas-confetti` för firandet vid milstolpar.
  - `cmdk` för sökningen med Ctrl+K i adminpanelen.
- **Våra mejl** till firmor och vår personal har Kronants utseende som HTML, med ordmärket i text, mässing på knappen och samma text som ren text. Det ändrar [ADR 0033](0033-mejl-till-traders-i-firmans-namn.md), där de var ren text. Mejlen till traders har som förut firmans utseende, och samma byggstenar.
- **Traderns kontosida är en cockpit**: equity stort, en mätare för utrymmet till varje förlustgräns som byter färg som i terminalen, och nästa steg i klartext. Milstolpar firas en gång, med konfetti i firmans färger, och ett underkänt konto förklaras som en lugn tidslinje i stället för en röd text.
- **Bilderna av produkten** på plattformens förstasida byggs med portalens egna delar, i stället för skärmbilder, så att de alltid ser ut som det firman får och kan ticka.
- **Saldodiagrammet** i portalen förblir vårt eget, i stället för terminalens `lightweight-charts`, eftersom det går att läsa med tangentbordet, har en tabell för skärmläsare och visar golven som linjer över tid. Skalan följer saldot, och golv och mål som ligger långt bort står som etiketter i kanten.

## Konsekvenser

- Byggstenarna i `prop/portal/src/components/ui.tsx` och `Choice.tsx` använder bara temats färgvariabler, så att firmans färger går igenom och firmorna senare kan välja mer, till exempel typsnitt och rundning.
- Ett ljust tema fungerar som förut. Skuggorna blir svagare där av sig själva.
- Paketen ökar sidornas storlek något. `motion` används bara där något flyttas, inte för enkla övergångar, som görs med CSS.
- Typsnitten hämtas från Google Fonts när appen byggs och serveras sedan från vår egen adress.
