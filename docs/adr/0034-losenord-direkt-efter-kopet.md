# 0034. Lösenordet väljs direkt efter köpet, och e-posten bekräftas efteråt

- Status: Föreslagen
- Datum: 2026-10-04

## Sammanhang

Efter betalningen i butiken fick köparen gå till sin mejl och följa en länk för att välja lösenord innan den såg sin challenge. Det är ett avbrott precis när köparen är som mest intresserad, och mejlet kan hamna i skräpposten. Inbjudan per e-post hade ett syfte: den visar att köparen äger e-postadressen, så att ingen kan köpa en billig challenge med någon annans adress och komma åt deras konton.

## Beslut

- **En ny köpare väljer lösenord direkt på orderns sida** och kommer till sitt konto. Det går bara när ordern startade traderns enda konto och tradern inte har något lösenord. Den som skriver någon annans adress kommer då bara åt kontot den själv betalade för, aldrig konton från förut.
- **E-posten bekräftas efteråt** med länken i mejlet efter köpet, samma länk som låter en köpare som gick därifrån välja lösenordet. En trader som har öppnat en länk från ett mejl till sin adress, en inbjudan eller en länk för nytt lösenord, har bekräftat den.
- **Utbetalningar kräver bekräftad e-post**, eftersom det är där pengar lämnar firman. Portalen visar en ruta med en knapp som skickar länken igen tills e-posten är bekräftad.
- **En trader med obekräftad e-post får en inbjudan** när firman mejlar om en ny challenge från adminpanelen, inte bara ett besked om att logga in, så att den som äger adressen alltid kommer in. Ett nytt lösenord med länken loggar ut alla andra sessioner.
- Traders från förut med lösenord, och de som konfigurerats för utveckling, räknas som bekräftade.

## Konsekvenser

- Köparen ser sin challenge direkt efter betalningen och får ett mejl i stället för att behöva ett.
- Den som skriver någon annans adress kan se och handla det konto den själv köpte tills ägaren av adressen tar över med en länk. Den kan inte ta ut pengar.
- En trader som inte bekräftar e-posten kan handla men inte få utbetalningar.
