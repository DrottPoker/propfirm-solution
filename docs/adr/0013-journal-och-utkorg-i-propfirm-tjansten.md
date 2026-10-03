# 0013. Journal och utkorg i propfirm-tjänsten

- Status: Föreslagen
- Datum: 2026-10-03

## Sammanhang

Propfirm-tjänsten tar emot händelser från handelsplattformen, låter regelmotorn besluta och utför besluten på handelsplattformen och hos firman. Handelsplattformen och firmans system kan vara otillgängliga en stund, och tjänsten kan stoppas mitt i ett arbete. Ett beslut får varken gå förlorat eller utföras två gånger. Varje beslut ska också gå att spåra i efterhand, med bevisen.

## Beslut

- **Varje challenge-konto har en journal.** Varje indata och regelmotorns beslut sparas som ett steg, med handelsplattformens händelse bakom som bevis.
- **Ett beslut, en transaktion.** Kontot låses, regelmotorn körs, och steget, det nya tillståndet och arbetet som beslutet kräver sparas tillsammans.
- **Utkorg.** Kommandon till handelsplattformen och webhooks till firman köas i samma transaktion som beslutet. Kommandona körs i ordning per firma, så ett konto alltid öppnas innan dess golv sätts. Tillfälliga fel försöks igen. Det handelsplattformen avvisar läggs åt sidan.
- **Händelseströmmen läses exakt en gång.** Löpnumret för en sida med händelser sparas i samma transaktion som besluten de ledde till.
- **Kontot räknas som öppet när händelsen kommer**, inte när anropet svarar. Då kommer alla fakta om ett konto i handelsplattformens ordning.
- **Kontots id bestäms i förväg**: `{firma}-{nummer}-{fas}`. Ett nytt försök att öppna kontot kan då inte skapa ett andra.
- **Webhooks signeras** med HMAC-SHA256 över tidsstämpel och kropp, och skickas igen tills firman svarar med 2xx.
- **Testerna kör mot riktig Postgres**, eftersom korrektheten vilar på transaktioner och lås i databasen.

## Konsekvenser

- Ett avbrott hos handelsplattformen fördröjer bara arbetet. Ingenting tappas, och ordningen behålls.
- Journalen ger revisionsloggen som produktplanen kräver, och bevisen som tradern ska kunna se vid ett brott.
- Firmans system måste känna igen en webhook som kommer två gånger, med hjälp av dess id.
- Tjänsten körs som en instans tills läsningen av strömmen och kommandona får lås mellan instanser.
- Ett kommando som läggs åt sidan behöver en vy i adminpanelen, så att det inte bara syns i loggen.
