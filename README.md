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
event log, SQLite day-counters, background host.

**Phase 2 — the time engine: done.** Samples running processes every 15s, matches
them to app rules, accumulates seconds into the counter store, runs the pure
budget engine, and warns once then kills the process tree at the limit or outside
the allowed schedule. 30 unit tests; verified live (a scheduled block killed a
real process and logged it).

Next: **Phase 3 — Azure Storage** (pull config from a blob, upload events as
append blobs) and a companion user-session tray/toast helper (the service itself
can't draw UI — see below). See `specs.md` section 10 for the phase plan.

## Layout

```
src/
  KidWatch.Core/     class lib (no I/O) — config models + YAML parser, schedule
                     logic, pure BudgetEngine, ProcessMatcher, clock, AgentEvent
  KidWatch.Agent/    .NET 10 Worker Service — config store/poller, JSONL sink,
                     SQLite counters, process probe, notifier, TimeEngine +
                     workers, host
tests/
  KidWatch.Core.Tests/   xUnit — schedule, budget, config parser, matcher,
                         counter store, time engine (with fakes)
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
