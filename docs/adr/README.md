# Arkitekturbeslut (ADR)

Varje viktigt tekniskt beslut får en egen fil. Filen beskriver sammanhanget, beslutet och konsekvenserna, så att det går att förstå i efterhand varför något är som det är.

## Beslut

| Nr | Beslut | Status |
|---|---|---|
| [0001](0001-monorepo-med-produktgranser.md) | Monorepo med hårda gränser mellan produkterna | Beslutad |
| [0002](0002-csharp-backend-typescript-webb.md) | C# i backend och TypeScript i webben | Beslutad |
| [0003](0003-postgres-och-nats.md) | Postgres och NATS JetStream, inget Redis i början | Föreslagen |
| [0004](0004-kontrakt-mellan-produkterna.md) | Protobuf för kontraktet mellan produkterna | Ersatt av 0012 |
| [0005](0005-deterministisk-handelsmotor.md) | Deterministisk handelsmotor | Föreslagen |
| [0006](0006-api-mellan-terminal-och-tjanst.md) | REST och SignalR mellan terminalen och handelstjänsten | Föreslagen |
| [0007](0007-graf-lightweight-charts.md) | TradingView Lightweight Charts för grafen | Beslutad |
| [0008](0008-journal-av-indata.md) | Journal av indata med ögonblicksbilder | Föreslagen |
| [0009](0009-inloggning-och-firmor.md) | Inloggning och firmor i handelsplattformen | Föreslagen |
| [0010](0010-prisflode-for-utveckling.md) | Tiingo som riktigt prisflöde under utvecklingen | Beslutad |
| [0011](0011-regelmotorn-satter-golv.md) | Regelmotorn sätter golv och avgör faserna | Föreslagen |
| [0012](0012-publikt-admin-api-som-kontrakt.md) | Handelsplattformens publika admin-API är kontraktet mellan produkterna | Föreslagen |
| [0013](0013-journal-och-utkorg-i-propfirm-tjansten.md) | Journal och utkorg i propfirm-tjänsten | Föreslagen |
| [0014](0014-vitmarkt-portal-pa-firmans-adress.md) | Vitmärkt portal på firmans adress med sessioner i propfirm-tjänsten | Föreslagen |
| [0015](0015-utbetalningar-tar-ut-vinsten-direkt.md) | Utbetalningar tar ut vinsten direkt | Föreslagen |
| [0016](0016-firmor-skapas-medan-handelsplattformen-kor.md) | Firmor och deras grupper skapas medan handelsplattformen kör | Föreslagen |
| [0017](0017-firmor-registrerar-sig-sjalva.md) | Firmor registrerar sig själva och börjar i en sandlåda | Föreslagen |
| [0018](0018-domaner-och-underdomaner.md) | Domäner och underdomäner för produkterna | Föreslagen |
| [0019](0019-kop-i-portalen-med-firmans-betalningsleverantor.md) | Köp i portalen med firmans egen betalningsleverantör | Föreslagen |
| [0020](0020-forbetalda-platser-for-aktiva-challenges.md) | Förbetalda platser för aktiva challenges | Föreslagen |
| [0021](0021-vi-granskar-firmor-innan-de-gar-live.md) | Vi granskar firmor själva innan de går live | Föreslagen |
| [0022](0022-handelshistorik-for-traderns-oversikt.md) | Handelshistorik för traderns översikt | Föreslagen |
| [0023](0023-adminpanelens-oversikt-och-firmans-logga.md) | Adminpanelens översikt, sökning och firmans egen logga | Föreslagen |
| [0024](0024-var-adminvy-over-alla-firmor.md) | Vår adminvy över alla firmor: översikt, kontroller och betalningar | Föreslagen |
| [0025](0025-e-post-genom-en-utkorg-och-notiser.md) | E-post genom en utkorg, och notiser som firman kan stänga av | Föreslagen |
| [0026](0026-traderns-utbetalningsmetod.md) | Traderns utbetalningsmetod sparas krypterad och följer med utbetalningen | Föreslagen |
| [0027](0027-handelsvillkor-och-inloggning-genom-portalen.md) | Firmans handelsvillkor, listning i terminalen och inloggning genom portalen | Föreslagen |
| [0028](0028-glomt-losenord-med-engangslank.md) | Glömt lösenord med en engångslänk som loggar ut andra sessioner | Föreslagen |
| [0029](0029-butiken-tar-riktiga-pengar-fran-forsta-dagen-live.md) | Butiken tar riktiga pengar från första dagen live | Föreslagen |
| [0030](0030-firman-valjer-kontovaluta.md) | Firman väljer kontonas valuta, och motorn räknar om genom USD | Föreslagen |
| [0031](0031-mallar-och-direkt-funded.md) | Fyra mallar för challenges, och direkt funded utan utvärdering | Föreslagen |
| [0032](0032-moms-och-fakturor.md) | Moms efter firmans land, och en faktura för varje betald debitering | Föreslagen |
| [0033](0033-mejl-till-traders-i-firmans-namn.md) | Mejl till traders i firmans utseende, med svar till firmans support | Föreslagen |
| [0034](0034-losenord-direkt-efter-kopet.md) | Lösenordet väljs direkt efter köpet, och e-posten bekräftas efteråt | Föreslagen |
| [0035](0035-terminalen-visar-kontot-som-portalen.md) | Terminalen visar kontot som firmans portal | Föreslagen |
| [0036](0036-rabattkoder-i-butiken.md) | Rabattkoder i butiken, och koder för nya försök | Föreslagen |
| [0037](0037-firmans-kontroller-och-utbetalningsbeslut.md) | Firmans kontroller av traders, nej med återförd vinst och en konsistensregel | Föreslagen |
| [0038](0038-en-tidszon-for-kontots-tider.md) | Ett kontos tider visas i challengens tidszon | Föreslagen |
| [0039](0039-egen-doman-for-firmans-portal.md) | Egen domän för firmans portal | Föreslagen |
| [0040](0040-driften-pa-en-vps.md) | Driften på en VPS i Sverige med Postgres i Docker | Föreslagen |
| [0041](0041-supportarenden-mellan-traders-och-firman.md) | Supportärenden mellan traders och firman i portalen | Föreslagen |
| [0042](0042-id-kontroll-med-en-extern-tjanst.md) | ID-kontroll av traders med Didit, eller firmans egen tjänst | Föreslagen |
| [0043](0043-sparrar-innan-vi-godkant-firman.md) | Firman når bara sitt eget team innan vi har godkänt den | Föreslagen |
| [0044](0044-anrop-bara-till-internet.md) | Anrop till firmans adresser når bara internet | Föreslagen |
| [0045](0045-skydd-mot-missbruk-av-gratis-sandlador.md) | Skydd mot missbruk av gratis sandlådor | Föreslagen |
| [0046](0046-namn-pa-bolaget-och-produkterna.md) | Bolaget heter Ludware och produkterna Kronant Trader och Kronant Prop | Beslutad |

## Så skriver du en ny ADR

1. Kopiera strukturen nedan till en ny fil med nästa lediga nummer.
2. Sätt status till "Föreslagen". Ändra till "Beslutad" när beslutet är taget.
3. Ändra aldrig ett beslutat ADR i efterhand. Skriv ett nytt som ersätter det och sätt det gamla till "Ersatt av NNNN".

```markdown
# NNNN. Rubrik

- Status: Föreslagen | Beslutad | Ersatt av NNNN
- Datum: ÅÅÅÅ-MM-DD

## Sammanhang
## Beslut
## Konsekvenser
```
