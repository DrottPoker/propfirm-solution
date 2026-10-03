# Kontrakt

Det enda handelsplattformen och propfirm-plattformen delar. Se [ADR 0012](../docs/adr/0012-publikt-admin-api-som-kontrakt.md).

```
contracts/
└── trading/
    └── trading-service.json   # OpenAPI för handelstjänsten, med admin-API:t under /api/admin/v1
```

Regler:

- Dokumentet genereras när handelstjänsten byggs. Ändra det aldrig för hand. CI kontrollerar att det är committat.
- Kontraktet får inte använda kod från någon produkt.
- Admin-API:t är det firmornas system och propfirm-plattformen bygger på. Ändringar som bryter mot det kräver en ny version (`/api/admin/v2`).
