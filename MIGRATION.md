# Initial Import from taapiio

This repo was carved out of `taapiio` as a slim, taapi.io-free BTC advisor.

## Initial DB Import

A one-time pg_dump from the legacy `taapiio-postgres-db` container is included under
`BtcAdvisor/backups/initial_import_<timestamp>.sql.gz`.

Scope: all `public.*` tables except `IndicatorError` (recomputable error log, 2.8 GB).
Includes `IndicatorResults` (~9.8 GB raw → ~1.4 GB gzip), so the worker does not need to
re-fetch indicator history on first start.

### Restore steps

1. Create `BtcAdvisor/.env` from `BtcAdvisor/.env.template` and fill in real secrets
   (e.g. `DatabasePassword`, Telegram tokens, Binance keys).
2. Start the stack so the empty `btcadvisor-postgres-db` container exists:
   ```bash
   cd BtcAdvisor
   docker compose up -d db    # or full stack; worker will wait while DB is restored
   ```
3. Restore the dump (script auto-gunzips and pipes into `psql`):
   ```bash
   cd BtcAdvisor
   ./restore.sh backups/initial_import_<timestamp>.sql.gz
   ```
4. (Optional) Bring up the worker:
   ```bash
   docker compose up -d
   ```

### Notes

- EF Core migrations run automatically on worker startup
  (`BinanceCandleWarmupService` calls `Database.MigrateAsync`). The dump contains
  `__EFMigrationsHistory`, so no further migration is applied at restore time.
- `IndicatorError` is intentionally empty after restore — it gets refilled as needed.
- Source dump timestamp is encoded in the filename (UTC of dump host).
