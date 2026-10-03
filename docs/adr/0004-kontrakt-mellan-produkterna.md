# 0004. Protobuf för kontraktet mellan produkterna

- Status: Ersatt av [0012](0012-publikt-admin-api-som-kontrakt.md)
- Datum: 2026-10-02

## Sammanhang

Propfirm-plattformen pratar med handelsplattformen genom ett internt API och tar emot händelser om affärer och kontovärde. Samma gränssnitt ska senare kunna användas mot Match-Trader, cTrader eller DXtrade genom adaptrar. Kontraktet måste vara tydligt, versionerat och fungera oberoende av programspråk.

## Beslut

- Kontraktet beskrivs med **protobuf** i `contracts/`.
- **Kommandon** (skapa konto, stänga konto, sätta gränser) går via **gRPC**.
- **Händelser** (affärer, kontovärde, regelbrott) skickas som protobuf-meddelanden över **NATS JetStream**.
- Varje version ligger i ett eget paket, till exempel `trading.v1`. Ändringar som bryter kontraktet kräver en ny version.
- **buf** kontrollerar stil och upptäcker ändringar som bryter kontraktet. Det läggs till i CI när den första `.proto`-filen skrivs.

Det publika API:t mot firmornas egna system (REST och webhooks) är ett separat beslut.

## Konsekvenser

- Kod för C# genereras vid bygget med Grpc.Tools. Kod för TypeScript kan genereras vid behov.
- Kontraktet blir en tydlig plats att granska när produkterna ändras.
