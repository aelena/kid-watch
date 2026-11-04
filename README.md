# pc-kid-watch

A self-hosted, config-driven family device manager for Windows laptops:
transparent time budgets, domain allow/deny + access logging, activity summaries,
and a phone-viewable report. No subscriptions, no covert surveillance. Full design
in [`specs.md`](specs.md).

> Scope note: this tool is **transparent by design** — discoverable in the tray,
> Services and Task Manager. It does **not** do keystroke capture or
> stealth/anti-detection. See `specs.md`.

## Status

**Phase 1 — agent skeleton: done.** Config poller (local file), structured JSONL
event log, SQLite day-counters, background host. Pure logic (schedule + budget
engine) is unit-tested.

Next: **Phase 2 — the time engine** (enumerate processes, accumulate minutes,
evaluate the budget engine, warn/kill, reset at midnight). See `specs.md`
section 10 for the phase plan.

## Layout

```
src/
  KidWatch.Core/     class lib — config models + YAML parser, schedule logic,
                     pure BudgetEngine, AgentEvent (JSONL) — no I/O
  KidWatch.Agent/    .NET 10 Worker Service — config store/poller, JSONL sink,
                     SQLite counters, background workers, host
tests/
  KidWatch.Core.Tests/   xUnit — schedule, budget, config parser, counter store
samples/config.sample.yaml   example config (prod pulls this from Azure Storage)
```

## Prerequisites

- .NET 10 SDK (pinned in `global.json`).

## Build & test

```sh
dotnet build KidWatch.slnx
dotnet test  KidWatch.slnx
```

## Run in dev (console, not installed as a service)

The agent reads paths from the `Agent` config section; override with env vars for
a dev run that writes to a local `_run/` folder instead of `C:\ProgramData`:

```sh
# PowerShell
$env:Agent__DataDir   = "$PWD\_run"
$env:Agent__ConfigFile = "$PWD\samples\config.sample.yaml"
dotnet run --project src/KidWatch.Agent
```

Outputs:
- events → `_run/logs/<device>/<yyyy-MM-dd>/events.jsonl`
- counters → `_run/counters.db`

## Install as a Windows service (later)

Built as a `Microsoft.NET.Sdk.Worker` with `AddWindowsService`, so once published
it installs with `sc.exe create` / `New-Service` and runs as `LocalSystem`. Not
wired into an installer yet.

### Known caveat: the tray icon is deferred

A Windows service runs in Session 0 and cannot draw UI in the signed-in user's
session. The "tray icon + toasts" from the spec therefore needs a small companion
app running in the user session (or toast via a user-session helper), which is a
later task. Phase 1 ships the service core; the visible-to-the-kid UI comes with
the enforcement work in phase 2+.
