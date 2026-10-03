# 0012. Handelsplattformens publika admin-API är kontraktet mellan produkterna

- Status: Föreslagen
- Datum: 2026-10-03
- Ersätter: [ADR 0004](0004-kontrakt-mellan-produkterna.md)

## Sammanhang

ADR 0004 föreslog ett privat kontrakt mellan produkterna: protobuf, gRPC för kommandon och NATS för händelser. Sedan dess har handelsplattformen blivit en egen produkt med vårt varumärke, som firmor kan köpa utan propfirm-plattformen (ADR 0009). Firmornas egna system, till exempel en annan CRM, behöver då samma koppling som propfirm-plattformen.

Två kopplingar skulle betyda dubbelt underhåll, och propfirm-plattformen skulle få möjligheter som andra kunder saknar.

## Beslut

- **Propfirm-plattformen använder handelsplattformens publika admin-API**, som vilken firma som helst: REST med firmans API-nyckel i headern `X-Api-Key`.
- **Admin-API:t är versionerat** under `/api/admin/v1`. Ändringar som bryter mot det kräver `v2`. Traderns API, som bara vår terminal använder, versioneras inte.
- **Händelser läses i en ström per firma**: `GET /api/admin/v1/events?after=&limit=&wait=`. Den bygger på journalen och har därför ett löpnummer per händelse, rätt ordning och bara sparade händelser. Konsumenten sparar sitt löpnummer och kan alltid läsa om. Med `wait` väntar anropet i upp till 30 sekunder på nya händelser.
- **Kontraktet är OpenAPI-dokumentet** i `contracts/trading/trading-service.json`. Det genereras när tjänsten byggs, och CI kontrollerar att det är committat. Terminalens typer genereras från det, och propfirm-plattformen genererar sin klient därifrån.
- **Direktinloggning** sker med engångslänkar som firmans system skapar. En länk gäller i 2 minuter och kan bara användas en gång. Bara en hash av token sparas.
- **Protobuf, gRPC och NATS används inte mellan produkterna.**

## Konsekvenser

- Det finns en enda koppling att underhålla, och den testas av vår egen propfirm-plattform.
- Firmor som hellre tar emot än hämtar händelser kan senare få webhooks ovanpå samma ström.
- Propfirm-plattformen behöver firmans API-nyckel för varje firma. När firmor ska kunna registrera sig själva behöver handelsplattformen ett API för att skapa firmor och servrar. Det blir ett eget beslut.
- NATS i `deploy/` behövs inte för detta. ADR 0003 gäller fortfarande för händelser inom en produkt, om behovet uppstår.
