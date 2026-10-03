# 0018. Domäner och underdomäner för produkterna

- Status: Föreslagen
- Datum: 2026-10-03

## Sammanhang

Lösningen har flera delar som ska nås från internet: säljsidor, registreringen för firmor, firmornas portaler, terminalen och API:erna. Handelsplattformen är vårt eget varumärke och ska kunna säljas ensam (ADR 0001, ADR 0009). Propfirm-plattformen är white label, och varje firma får en egen adress direkt vid registreringen (ADR 0014, ADR 0017). Senare ska firman kunna använda sin egen domän.

Vi behöver bestämma hur adresserna fördelas innan första servern sätts upp, så att cookies, certifikat och varumärken inte blandas ihop. Produkterna har inga slutliga namn än, så nedan används `propbrand` och `tradebrand` som platshållare.

## Beslut

- **Tre domäner, en per ansikte utåt.**
  - `propbrand.com` för propfirm-plattformen mot firmorna.
  - `propbrand.app` (eller en annan separat domän) för firmornas portaler.
  - `tradebrand.com` för handelsplattformen.
- **Propfirm-plattformen.**
  - `propbrand.com`: säljsidan.
  - `app.propbrand.com`: registreringen för nya firmor (`Platform:Url`).
  - `api.propbrand.com`: firmornas API (`/api/firm/v1`) och betalningsleverantörernas meddelanden (`/api/payments/v1`, ADR 0019).
- **Firmornas portaler.**
  - `{firma}.propbrand.app`: firmans portal direkt efter registreringen (`Platform:FirmPortalUrl`). Adminpanelen ligger på samma adress under `/admin`.
  - Firmans egen domän, till exempel `portal.acme.com`, pekas hit med en DNS-post och får ett eget certifikat. Det byggs i fas 9.
- **Handelsplattformen.**
  - `tradebrand.com`: säljsidan.
  - `trade.tradebrand.com`: terminalen, gemensam för alla firmor. Firman syns som server på inloggningssidan.
  - `api.tradebrand.com`: handels-API:t, admin-API:t (`/api/admin/v1`) och partner-API:t (`/api/partner/v1`).
- **Portalerna ligger inte på vår huvuddomän.** Blir en firmas sida svartlistad, till exempel av Google eller av mejlfilter, ska det inte drabba vår säljsida, registreringen eller mejlen från oss. Shopify gör likadant med `myshopify.com`.
- **Testservern** har samma upplägg med `test.` före domänen, till exempel `app.test.propbrand.com`, `acme.test.propbrand.app` och `trade.test.tradebrand.com`.
- **Det här når inte internet:** Postgres, NATS och propfirm-tjänstens portal-API (`/api/portal`). Portal-API:t nås bara genom portalen, eftersom tjänsten litar på värdnamnet som portalen skickar med (ADR 0014). `api.propbrand.com` släpper därför bara igenom `/api/firm/v1` och `/api/payments/v1`.

## Konsekvenser

- Ingen kod behöver ändras. Adresserna är redan inställningar (`Platform:Url`, `Platform:FirmPortalUrl`, `Platform:ApiUrl`, `Terminal:Url`, `Cors:AllowedOrigins`, `NEXT_PUBLIC_TRADING_API_URL`, `PROP_API_URL`).
- Portalerna behöver ett wildcard-certifikat för `*.propbrand.app`, och testservern ett för `*.test.propbrand.app`. Ett wildcard-certifikat täcker bara en nivå, så de två kan inte dela certifikat.
- Firmornas egna domäner kräver ett certifikat per domän som hämtas automatiskt när firman har lagt in sin DNS-post. Det hör till fas 9.
- Terminalen och handels-API:t ligger på olika värdnamn, så `Cors:AllowedOrigins` måste innehålla terminalens adress.
- Mejl från plattformen skickas från `propbrand.com`. Det gäller också inbjudan till en trader efter ett köp i portalen, som har firmans namn som avsändare (ADR 0019). Andra mejl till traders skickas av firman själv (ADR 0017).
- Domänerna köps när produkterna har fått sina namn. Byts ett namn ändras bara inställningar och DNS.
