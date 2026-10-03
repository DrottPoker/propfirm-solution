# 0014. Vitmärkt portal på firmans adress med sessioner i propfirm-tjänsten

- Status: Föreslagen
- Datum: 2026-10-03

## Sammanhang

Propfirm-plattformen är white label. En trader ska se firmans namn, logga, färger och domän, logga in hos firman och därifrån komma in i vår handelsplattform utan ett lösenord till. Firmans administratörer behöver en adminpanel för samma konton. Firman tar själv betalt och skapar konton via API:t eller adminpanelen, så portalen behöver inget köp i version 1.

Många firmor ska kunna köra på samma installation, var och en på sin egen domän. En inloggning hos en firma får aldrig gälla hos en annan.

## Beslut

- **En portal för alla firmor.** Firman känns igen på adressen portalen öppnas på. Varje firma har sina värdnamn i konfigurationen, och ett värdnamn hör till en enda firma. Utseendet (namn, logga, färger) hämtas på servern innan sidan visas.
- **Samma ursprung.** Webbläsaren pratar bara med portalens egen adress. Portalen skickar `/api/portal/*` vidare till propfirm-tjänsten och anger det ursprungliga värdnamnet i `X-Forwarded-Host`. Sessionens cookie hör därmed till firmans domän, och ingen CORS behövs.
- **Sessioner i propfirm-tjänsten.** Inloggningen är en krypterad cookie (HttpOnly, SameSite Lax) med användarens id, roll och firma. Traders och administratörer har var sin cookie (`prop_trader` och `prop_admin`), så samma webbläsare kan vara inloggad med båda rollerna samtidigt. Nycklarna sparas i Postgres, så sessioner överlever en omstart. En session från en annan firmas adress avvisas med 401.
- **Två roller.** Traders ser bara sina egna konton, och andra konton svarar 404. Administratörer ser hela firmans konton och kan starta, avbryta och godkänna. Rollerna har var sin inloggning.
- **Inbjudan i stället för lösenord från firman.** Firman skapar en inbjudningslänk via API:t eller adminpanelen och skickar den själv. Länken gäller en gång i 7 dagar och låter tradern välja ett lösenord, som standard på minst 10 tecken. En ny inbjudan ersätter traderns äldre oanvända och fungerar också som återställning av ett glömt lösenord. Bara en hash av länkens token sparas.
- **Terminalen nås med en engångslänk.** Knappen "Open terminal" ber handelsplattformen om en inloggningslänk för traderns aktuella konto (ADR 0012). Lösenordet till handelsplattformen visas aldrig.
- **Siffror i realtid från handelsplattformen.** Portalen visar kontot värderat till senaste priserna och golvens marginal från handelsplattformens admin-API. Svarar den inte visas det handelsplattformen senast rapporterade, utan marginal. Portalen räknar aldrig själv på pengar.

## Konsekvenser

- En ny firma behöver bara konfiguration och en DNS-post, ingen egen installation.
- Propfirm-tjänsten litar på `X-Forwarded-Host`. I produktion får den därför bara nås genom portalen eller en proxy som sätter headern, aldrig direkt från internet.
- Next.js skickar inte vidare webbläsarens adress. Proxyn framför portalen måste sätta `X-Forwarded-For` och `X-Forwarded-Proto`, och propfirm-tjänsten måste lita på portalens nät, så att gränsen för inloggning räknas per webbläsare och cookien blir Secure. Lokalt litar den bara på loopback.
- Portalens adress till propfirm-tjänsten (`PROP_API_URL`) läses när portalen byggs, eftersom vidareskickningen ligger i Next.js konfiguration.
- Reglerna för lösenord, inloggningsförsök och sessionens längd är inställningar. I utveckling är de avstängda, så att det går snabbt att testa med korta lösenord och många inloggningar.
- En session gäller tills den har varit oanvänd i 12 timmar, om inget annat är inställt. Den återkallas inte när lösenordet byts, och utloggning tar bara bort cookien i webbläsaren.
- Egen domän med automatiskt TLS-certifikat, självbetjäning för firmor och köp i portalen byggs senare.
