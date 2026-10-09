# 0060. Säkerhetspolicy i webbläsaren för terminalen och personalpanelen

- Status: Accepterad
- Datum: 2026-10-10

## Sammanhang

Översikten över Kronant Trader i drift tog upp en sak till som bör finnas vid lansering: terminalen skyddades bara mot att visas inuti en annan sida (ADR 0058), och personalpanelen inte ens mot det. Sessionerna ligger i cookies som skript inte kan läsa, men ett skript som körs på sidan kan ändå anropa handelstjänsten med sessionen. I terminalen betyder det ordrar och stängda positioner i traderns namn. I personalpanelen betyder det nya admin-nycklar, nya servrar och stoppade nycklar.

Ett sådant skript kan komma in genom en bugg som visar text från en firma eller en trader som HTML, eller genom ett paket som ändrats. Webbläsaren kan stoppa det om sidan säger vilka skript den själv har skickat.

## Beslut

**Bara sidans egna skript körs.** Varje sida får en egen nonce, 128 slumpade bitar, från `src/proxy.ts`, och policyn i `Content-Security-Policy` säger att bara skript med den får köra, och de skript som de i sin tur laddar (`'strict-dynamic'`). Next.js läser nonce ur policyn och sätter den på sina egna skript. I terminalen sätter rotlayouten den också på det lilla skriptet som väljer tema innan sidan ritas. Personalpanelen har inga egna skript i sidan. Skript i attribut som `onclick`, skript från andra adresser och `eval` körs inte. I utveckling tillåts `eval`, eftersom React bygger om felstackar med det där.

**Sidorna ritas för varje anrop.** En nonce måste vara ny varje gång, så ingen sida byggs färdig i förväg. Terminalens layout läser nonce ur anropet och panelens layout väntar på `connection()`. Sidorna är små och hämtar sina data i webbläsaren, så det kostar lite.

**Stilar i sidan tillåts.** React sätter `style`-attribut, och typsnitten och notiserna lägger stilar i sidan. En stil kan inte köra kod, och en nonce för stilar skulle stoppa `style`-attributen.

**Sidan pratar bara med handelstjänsten.** Anrop och realtidshubben får gå till sidans egen adress och till handelstjänsten, över `https` och `wss` (eller `http` och `ws` i utveckling). I utveckling får sidan också sin egen websocket, som laddar om den när koden ändras.

**Bilder från alla säkra adresser.** En firmas logga kan ligga var som helst på webben, så bilder får komma från sidan själv, från `data:` och `blob:` och från alla adresser med `https`. I utveckling också från `localhost`. Typsnitten kommer bara från sidan själv, eftersom `next/font` lägger dem där.

**Resten är stängt.** Inga insticksprogram (`object-src 'none'`), ingen annan basadress (`base-uri 'none'`), formulär bara till sidan själv, inga sidor inuti sidan (`frame-src 'none'`) och sidan aldrig inuti en annan (`frame-ancestors 'none'`). I drift görs adresser med `http` om till `https` (`upgrade-insecure-requests`).

**Fler rubriker på alla svar**, från `next.config.ts`:

| Rubrik | Värde | Varför |
|---|---|---|
| `X-Frame-Options` | `DENY` | Samma skydd mot att visas inuti en annan sida, för äldre webbläsare |
| `X-Content-Type-Options` | `nosniff` | En fil körs bara som det den säger att den är |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Andra webbplatser ser bara adressen till terminalen, aldrig sidan eller dess parametrar |
| `Permissions-Policy` | kamera, mikrofon, plats, betalning och USB av, helskärm bara för sidan själv | Sidan kan aldrig be om något den inte behöver |
| `Cross-Origin-Opener-Policy` | `same-origin` | En sida som öppnat terminalen kan inte styra den |
| `Strict-Transport-Security` | ett år, med underdomäner, bara i drift | Webbläsaren går aldrig till terminalen utan `https` |

Policyn byggs av `contentSecurityPolicy` i `src/lib/securityHeaders.ts`, som finns i båda apparna och är lika i dem. Apparna delar inte kod med varandra utom designsystemet, så en ändring i den ena görs också i den andra. Enhetstester kontrollerar policyn i drift och i utveckling.

**Testerna fångar ett brott.** I båda apparnas tester i webbläsaren fälls ett test av ett fel som sidan inte fångar och av allt som policyn stoppar, även när det testet tittar på ändå fungerar. En regel som är för snäv märks då i det test som använder delen.

## Konsekvenser

- Ett skript som en bugg släpper in i en sida körs inte, och en sida kan inte skicka något någon annanstans än till handelstjänsten.
- Ett skript från en annan adress, till exempel för statistik eller chatt, kräver att policyn ändras, och det beslutet tas i en ny ADR.
- En firmas logga på en adress utan `https` visas inte i drift. Personalpanelen visar samma logga som terminalen, så felet syns där.
- Sidorna går inte att cacha i ett CDN, eftersom varje svar har sin egen nonce. Byggda filer under `/_next/static` cachas som förut.
- Policyn rapporterar inte brott någonstans. Behövs det läggs `report-to` till när det finns en tjänst som tar emot rapporterna.
- Kronant Prop har ännu ingen policy. Det är en egen produkt och får ett eget beslut.
