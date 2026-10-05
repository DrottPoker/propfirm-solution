# 0039. Egen domän för firmans portal

- Status: Föreslagen
- Datum: 2026-10-05

## Sammanhang

En firmas portal ligger på vår underdomän, till exempel `acme.kronant.app` (ADR 0018). Firmorna vill att traders hittar dem på sin egen adress, till exempel `portal.acme.com`, och ADR 0018 förutsåg det: en DNS-post pekar hit och domänen får ett eget certifikat. Vi måste veta att domänen är firmans innan den blir portalens adress, och bara göra certifikat för domäner som hör till en firma.

## Beslut

- **Firman lägger till en underdomän** under Your domain i adminpanelen. En domän utan underdomän kan inte peka hit med en CNAME-post, och våra egna domäner går inte att ta. En domän hör till en firma, och en firma har en domän åt gången.
- **Två DNS-poster:** en CNAME-post från domänen till `Domains:CnameTarget`, som visar att den pekar hit, och en TXT-post `_kronant.{domän}` med en slumpad token, som visar att den är firmans. En domän utan CNAME godtas om den har samma adresser som målet, för leverantörer som gör om CNAME till adresser.
- **Vi slår upp posterna** med DNS över HTTPS, var femte minut för domäner som väntar och direkt när firman lägger till domänen eller klickar på "Check now". Inget DNS-bibliotek behövs, och testerna byter uppslagningen mot en låtsad.
- **När båda posterna finns** blir domänen ett av firmans värdnamn och portalens adress i samma transaktion. Mejl, Stripes återvändsadresser och terminalens länk tillbaka använder den nya adressen. Adressen hos oss fortsätter att fungera.
- **Certifikaten görs av vår proxy** (Caddy med certifikat på begäran), som frågar `GET /api/tls/allowed?domain=` innan den hämtar ett. Bara firmors värdnamn svarar 200. Vägen nås inte från internet, som resten av tjänsten utom firmans API och betalningarnas webhooks (ADR 0018).
- **Firman tar bort domänen** när den vill, och portalen går tillbaka till adressen hos oss. Firmor från konfigurationen har sina värdnamn där och kan inte byta här.

## Konsekvenser

- En firma kan ha portalen på sin egen domän utan att vi gör något för hand.
- Proxyn och dess certifikat sätts upp med produktionen (steg 10). Tills dess fungerar en egen domän bara utan https, och lokalt blir ingen domän aktiv eftersom målet är ett exempel.
- Sessionens cookie gäller per värdnamn, så en trader som byter mellan adressen hos oss och den egna domänen loggar in igen.
- Ändras DNS efter att domänen blivit aktiv märker vi det inte, men certifikatet kan inte förnyas, och portalen slutar svara på den adressen.
