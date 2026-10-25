# Copilot Instructions for BtcAdvisor

## Project overview

This is a .NET 10 trading signal and backtesting platform.
It fetches indicator and market data, stores data in PostgreSQL, evaluates Buy/Sell signals, and exposes APIs and UI pages for analysis.

## Repository reality check

- Solution file `BtcAdvisor.sln` references these projects:
  - `BtcAdvisor`
  - `BtcAdvisor.Tests`

## Solution structure

| Project | Purpose |
|---|---|
| `BtcAdvisor/` | ASP.NET Core Web/API application (controllers, services, repositories, hosted services, EF Core) |
| `BtcAdvisor.Tests/` | Unit and integration tests (NUnit, Moq, EF Core InMemory) |

## Tech stack

- .NET 10 (`net10.0`), C#
- ASP.NET Core
- Entity Framework Core with Npgsql (PostgreSQL)
- Docker Compose
- NUnit + Moq for tests

## Coding conventions

- 4 spaces, LF, UTF-8; follow `.editorconfig`.
- Nullable reference types are enabled; do not suppress warnings without reason.
- Prefer clear, small methods and explicit naming.
- Use XML docs for new public APIs when practical.

## Conversation stance

- Use a constructive devil's-advocate stance: challenge assumptions and show risks.
- Keep language simple and direct.
- If a request is ambiguous, ask enough focused questions before implementing.

## Git safety rules (critical)

- Never delete `main` or `origin/main` locally or remotely.
- Never use destructive git commands (`reset --hard`, `checkout --`) unless explicitly approved.
- Do not remove branches unless staleness is explicitly verified (`git fetch --prune` and `: gone]`).

## Product behavior alignment (critical)

- Replay and realtime trading decisions must use one shared decision policy.
- Do not let UI/backtest selection logic diverge from realtime messaging logic.
- Any entry/exit logic change must update replay and realtime in the same change set.

## DateTime handling (critical)

- Use UTC everywhere (`DateTime.UtcNow`).
- Normalize before comparison/persistence.
- Keep API responses and logs UTC.
- For raw SQL in PostgreSQL, prefer `::timestamptz` in new code.

## Data deletion safety (critical)

- Prefer approved application APIs for deletion.
- Direct destructive SQL/filesystem deletion needs explicit user approval in the current chat.
- Before execution, state exact target and scope.

## Concurrency rules

- `ApplicationDbContext` is not thread-safe; use separate DI scopes per parallel task.
- Bound parallelism with `SemaphoreSlim`.
- Avoid unbounded `Task.WhenAll` over DB operations.

## IndicatorId format

Use:

`{exchange}_{symbol}_{timeSpan}_{indicator}_{period}_{index}`

Examples:
- `binance_BTC/USDT_1h_ema_200_0`
- `binance_BTC/USDT_1h_price__0`

When filtering in DB queries, prefer `StartsWith` over broad `Contains` when possible.

## Testing conventions

- Framework: NUnit with `[TestFixture]` and `[Test]`.
- Mocking: Moq (`Setup`, `Returns`, `Verify`).
- For `IServiceScopeFactory.CreateScope()` tests, return a fresh scope instance per call.
- Add tests for changed behavior before declaring completion.
- Self-validate changed code paths before reporting done.

## Validation commands

Preferred:

```bash
# Build test project
 dotnet build BtcAdvisor.Tests/BtcAdvisor.Tests.csproj

# Run focused tests
 dotnet test BtcAdvisor.Tests/BtcAdvisor.Tests.csproj
```

If full solution is available and healthy:

```bash
dotnet build BtcAdvisor.sln
dotnet test BtcAdvisor.sln
```

## Completion criteria

A task is complete only when:

1. Changed behavior is implemented and self-validated.
2. All tests are green for available projects (or a blocking external reason is explicitly documented).
3. New behavior is covered by automated tests, or a clear reason is documented.
4. Replay/realtime parity is preserved for trading logic changes.
5. Dead code from refactors is removed.
6. A status message has been sent via `BtcAdvisor/scripts/sendMessage.sh`.
7. Changes are deployed locally and verified against the running application when possible; if not possible, the blocker is explicitly documented.

## Notifications

Use `BtcAdvisor/scripts/sendMessage.sh` to notify the user when:

- A task is completed, or
- A blocker or question or anything else that requires user input.

## ADR policy

Create/update an ADR for non-trivial trading logic decisions, especially:

- new gates, thresholds, or policy flags,
- new or removed condition classes,
- significant replay/realtime decision flow changes.

Store ADRs in `docs/adr/`.
