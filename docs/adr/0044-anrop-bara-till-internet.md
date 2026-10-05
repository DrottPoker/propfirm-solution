# 0044. Anrop till firmans adresser når bara internet

- Status: Föreslagen
- Datum: 2026-10-05

## Sammanhang

Firman väljer själv adressen dit dess webhooks skickas ([ADR 0013](0013-journal-och-utkorg-i-propfirm-tjansten.md)). Vår server anropar den, och adminpanelen visar firman statuskoden och felet för varje leverans. Hittills krävdes bara https. En firma, också en gratis firma i sandlådan, kunde därför låta vår server anropa adresser i vårt eget nät eller molnets, till exempel `https://10.0.0.5/` eller `https://localhost:5432/`, och av svaren och felen se vilka tjänster som finns där. Det kallas server-side request forgery.

Ett namn kan peka på en publik adress när det sparas och på en intern när det anropas, så det räcker inte att kontrollera adressen när den sparas.

## Beslut

- **Webhooks anropas bara på publika adresser.** HTTP-klienten för webhooks ansluter genom `PublicAddresses`, som slår upp namnet när den ansluter och bara ansluter till adresser på internet: inte loopback, privata nät, länklokala adresser (där molnens metadata finns), delade nät, reserverade nät eller multicast, och inte heller IPv6-adresser som bär en sådan IPv4-adress. Finns ingen publik adress blir det ett fel som säger att adressen inte är på internet, och inget anrop görs.
- **Omdirigeringar följs inte,** eftersom de kunde leda vart som helst. En webhook som svarar med en omdirigering räknas som misslyckad, som andra svar utanför 2xx.
- **Adressen kontrolleras också när den sparas.** En IP-adress som inte är publik, och namn som `localhost` eller som slutar på `.localhost`, `.local` eller `.internal`, nekas med 422, så att firman ser felet direkt.
- Andra adresser firman väljer, som dess betalsida och dess KYC-sida, öppnas av traderns webbläsare och anropas aldrig av vår server. Kommer fler anrop från servern till adresser som firman väljer, går de genom `PublicAddresses` på samma sätt.

## Konsekvenser

- En firma kan inte använda webhooks för att se in i eller anropa vårt nät.
- En firma kan inte ta emot webhooks på en dator i sitt eget lokala nät under utvecklingen. Den använder en tunnel med en publik adress, som de flesta tjänster med webhooks kräver.
- Felen som visas för firman avslöjar inget om vårt nät, eftersom inget internt anrop görs.
