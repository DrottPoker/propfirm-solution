# @kronant/design

De designtokens som portalen (`prop/portal`) och terminalen (`trading/terminal`) delar ([ADR 0047](../../../docs/adr/0047-ett-gemensamt-designsystem.md)):

| Del | Innehåll |
|---|---|
| Kronants färger | `--kronant-*`: grafit, varm benvit text och mässing. Våra egna sidor, vår adminvy och terminalen. En firmas portal har firmans färger. |
| Djup | `--shadow-card`, `--shadow-raised`, `--shadow-float` med en ljus kant överst, och `--grain`, ett fint brus över stora ytor. Skuggorna följer `color-scheme` med `light-dark()`. |
| Rörelse | Easing och tider, animationerna `animate-enter`, `-fade`, `-pop`, `-slide-in`, `-grow-x`, `-draw` (en linje som ritas, som bocken efter ett köp), `-ring-fill`, `-shimmer`, `-pulse-dot` (pricken för terminalens anslutning), `-flash-up` och `-flash-down`, klassen `stagger` och `skeleton`. Allt står still för den som valt minskad rörelse. |

Varje app importerar filen efter Tailwind, i sin `globals.css`:

```css
@import "tailwindcss";
@import "@kronant/design/tokens.css";
```

Typsnitten laddas av varje app med `next/font`: Familjen Grotesk för text och siffror (siffrorna är lika breda, så belopp står i kolumner), Instrument Serif för rubriker och JetBrains Mono för kod och terminalens priser.
