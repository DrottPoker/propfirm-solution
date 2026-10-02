# Kontrakt

Det enda handelsplattformen och propfirm-plattformen delar: det interna API:t och händelserna mellan dem. Se [ADR 0004](../docs/adr/0004-kontrakt-mellan-produkterna.md).

Planerad struktur:

```
contracts/
└── proto/
    └── trading/
        └── v1/        # första versionen av kontraktet mot handelsplattformen
```

Regler:

- Kontraktet får inte använda kod från någon produkt.
- Ändringar som bryter kontraktet kräver en ny version (`v2`).
- buf läggs till i CI när den första `.proto`-filen skrivs.
