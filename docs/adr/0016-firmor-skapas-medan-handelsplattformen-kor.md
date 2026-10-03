# 0016. Firmor och deras grupper skapas medan handelsplattformen kör

- Status: Föreslagen
- Datum: 2026-10-03

## Sammanhang

Firmor och handelsgrupper finns i dag bara i handelstjänstens konfiguration. Gruppernas villkor (valuta, stop out, hävstång, påslag och provision) ingår i motorns konfiguration, så en ny grupp kräver att tjänsten startas om. Varje grupp hör till en firma, och det är grupperna som håller isär firmornas konton, händelser och traders (ADR 0009).

En firma som registrerar sig själv ska få en server på handelsplattformen direkt (ADR 0017). ADR 0009 sa att firmor flyttas till databasen när de kan registrera sig själva, och ADR 0012 att ett API för att skapa firmor blir ett eget beslut.

## Beslut

- **Firmor sparas i databasen**, i tabellerna `tenants` och `tenant_groups`. Firmor i konfigurationen skrivs dit vid varje start, för utveckling och tester. Tjänsten håller alla firmor i minnet, eftersom den ändå bara kan köras som en instans.
- **Partner-API.** Ett system som får skapa firmor åt andra, till exempel vår propfirm-plattform, är en partner med en egen nyckel i konfigurationen (`Partners`, bara nyckelns SHA-256). Vägarna ligger under `/api/partner/v1` och versioneras som admin-API:t.
- **En firma skapas** med server och namn. Svaret har firmans nyckel till admin-API:t, som bara visas den gången. En partner kan be om en ny nyckel, och då slutar den gamla fungera. Det är också vägen tillbaka om svaret gick förlorat. En partner ser bara de firmor den har skapat.
- **Grupper skapas med ett indata till motorn**, `CreateGroup`. Det sparas i journalen som andra kommandon, och ögonblicksbilderna innehåller grupperna. Uppspelning ger därför samma grupper efter en omstart, oavsett hur konfigurationen ser ut då.
- **En ny firma får en kopia av mallgrupperna** i `Tenancy:NewTenantGroups`, som pekar på grupper i konfigurationen. Kopian får id `{server}-{mall}`, till exempel `acme-standard`. En grupp hör fortfarande till en enda firma, så isoleringen mellan firmor fungerar som förut.
- **En grupps villkor ändras inte** efter att den har skapats. Att ändra villkor för konton med öppna positioner blir ett eget beslut.
- **Nya firmor syns inte i listan över servrar** förrän de går live. Inloggning med servern fungerar ändå, och tradern kommer in med inloggningslänkar från firmans portal.

## Konsekvenser

- En ny firma kan börja handla direkt, utan omstart och utan att någon ändrar konfigurationen.
- Motorns tillstånd växer med en grupp per firma, även för firmor som aldrig går live. Propfirm-plattformen skapar därför servern först när e-postadressen är bekräftad, och registreringen har en gräns per IP-adress.
- Konfigurerade grupper finns kvar. De används av utvecklingsfirman och testerna och som mallar för nya firmor. En konfigurerad grupp och en skapad grupp kan inte ha samma id.
- Firmans namn på servern ändras inte än. Det kommer när firmor kan byta namn.
- Tjänsten kan fortfarande bara köras som en instans, eftersom motorn ligger i minnet.
