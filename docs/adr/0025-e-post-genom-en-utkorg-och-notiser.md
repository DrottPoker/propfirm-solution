# 0025. E-post genom en utkorg, och notiser som firman kan stänga av

- Status: Föreslagen
- Datum: 2026-10-04

## Sammanhang

Genomgången som ny firma visade att plattformen nästan inte mejlade något. Firman fick inte veta att en challenge sålts, att en trader klarat alla faser och väntade på ett funded-konto eller att en utbetalning begärts, utan måste titta i adminpanelen. Tradern fick inget när en fas klarades, när kontot blev funded, när challengen tog slut eller när en utbetalning godkändes, betalades eller nekades, och ingen påminnelse innan challengen tar slut för att ingen affär öppnats.

Mejlen som fanns skickades direkt i anropet. Ett mejl som inte gick iväg loggades och var borta, och ett beslut som sparades kunde tappa sitt mejl, eller tvärtom.

## Beslut

- **Mejl köas i tabellen `email_outbox` i samma transaktion som ändringen de berättar om**, som webhooks ([ADR 0013](0013-journal-och-utkorg-i-propfirm-tjansten.md)). Ett mejl går alltså iväg om och bara om beslutet sparas.
- **`EmailWorker` skickar dem i bakgrunden**, äldst först, och väcks direkt när något köas. Ett mejl som mejlservern inte tar emot försöks igen efter 30 sekunder, med dubbel väntan upp till 4 timmar, i 12 försök, ungefär ett dygn. Sedan ges det upp och syns i tabellen och loggen. Ett mejl som hämtats hålls i 2 minuter, så att en annan instans inte skickar det samtidigt.
- **Notiserna köas där regelmotorns beslut sparas** och när en order betalas. Till firmans administratörer: en ny försäljning, en trader som klarat alla faser och väntar på funded-kontot, och en begärd utbetalning. Till tradern: en klarad fas, alla faser klarade, funded-kontot öppet, challengen slut (brott eller tid) och utbetalningen godkänd, betald eller nekad.
- **Påminnelsen om att handla** köas av `InactivityReminderWorker`, som tittar varje halvtimme. Den kommer när den sista dagen att öppna en affär är högst 3 dagar bort, en gång per sista dag: en nyckel i utkorgen (`inactivity:{konto}:{dag}`) hindrar att samma påminnelse köas igen. En ny affär flyttar sista dagen, och då kan en ny påminnelse komma.
- **Firman stänger av eller sätter på varje slag** under Notifications i adminpanelen. Valen sparas i `firms.email_settings` som slag och sant eller falskt. Ett slag som firman aldrig valt skickas, så att nya slag är på från början. Firmor som skickar egna mejl från webhooks stänger av våra.
- **Mejlen till traders går i firmans namn** och länkar till kontot i firmans portal. Mejlen till administratörerna går till alla firmans administratörer.
- **Inbjudningar och länkar för lösenord kan inte stängas av**, eftersom de behövs för att komma in. Länkarna för lösenord köas också i utkorgen.
- **Mejlen till administratörerna kommer från oss, som ren text och som HTML i Kronants utseende** (tillägg efter genomgången av UI och UX). Samma mall gäller alla våra mejl till firmor och vår personal: ordmärket överst, texten som stycken, en mässingsfärgad knapp för huvudlänken och en lugn sidfot med plattformens namn och varför mottagaren får mejlet, och för notiserna att de väljs under Notifications. HTML:en görs av den rena texten, så de säger samma sak. Se [specen för propfirm-tjänsten](../spec/propfirm-tjanst.md).
- **Firman ser varje notis innan den skickas** (tillägg efter genomgången av UI och UX). "Show the email" under Notifications visar mejlet som vi skulle skicka det nu, i firmans namn och utseende och om firmans egen challenge, med en påhittad trader och påhittade siffror. Det byggs av samma kod som de riktiga mejlen, så förhandsvisningen kan inte visa något annat än det som skickas, och det köas, skickas och sparas aldrig. Se [specen för portalen](../spec/portal.md).
- **Köparen får kvittot** i mejlet efter köpet. En köpare som redan har lösenord får det i ett eget mejl som köas i samma transaktion som betalningen. Det kan inte stängas av. Se [specen för köp i portalen](../spec/kop.md).

## Konsekvenser

- Ett mejl kan komma några sekunder efter beslutet, och efter ett avbrott hos mejlservern mycket senare. Det står inte i mejlet när beslutet togs.
- Texterna är fasta och på engelska. En firma som vill ha egna texter eller språk stänger av våra och skickar egna från webhooks.
- Utkorgen växer med alla mejl. Den behöver rensas eller arkiveras innan produktion.
- Mejlen som fanns innan, som inbjudan efter ett köp och mejlen om granskningen, skickas fortfarande direkt. De kan flyttas till utkorgen senare.
