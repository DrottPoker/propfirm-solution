# 0003. Postgres och NATS JetStream, inget Redis i början

- Status: Föreslagen
- Datum: 2026-10-02

## Sammanhang

Produktplanen nämner Postgres för data, Redis för aktuellt tillstånd och en kö (NATS eller Redis Streams) mellan tjänsterna. Varje extra del i driften är en del till som kan gå sönder, och plattformen ska vara uppe dygnet runt fem dagar i veckan.

## Beslut

- **Postgres** för all beständig data. Varje produkt har egen databas och egen roll.
- **NATS JetStream** för händelser mellan tjänsterna, till exempel affärer och kontovärde från handelsplattformen till regelmotorn. JetStream sparar händelserna, så att en tjänst som startar om kan läsa ikapp.
- **Inget Redis i början.** Handelsmotorn håller sitt tillstånd i minnet och sparar alla indata och ögonblicksbilder i Postgres. Efter en omstart läses den senaste ögonblicksbilden och indata efter den spelas upp igen (se ADR 0008).

Lokalt startas båda med `docker compose -f deploy/docker-compose.yml up -d`.

## Konsekvenser

- Färre delar att drifta och övervaka.
- Redis kan läggas till senare om ett verkligt behov uppstår, till exempel för cache i portalen.
- Historik och analys ligger i Postgres till att börja med. TimescaleDB eller ClickHouse kan bli aktuellt när datamängden växer.
