# pc-kid-watch — specs (v0)

A self-hosted, config-driven family device manager for Windows laptops. No
subscriptions, no covert surveillance. The kids know the laptop is managed; the
agent is discoverable, not hidden. Scope is deliberately small: **time budgets,
domain allow/deny + access logging, activity summaries, and a phone-viewable
report.**

Explicitly out of scope (dropped by decision 2026-11-03): keystroke capture,
stealth/anti-detection, hiding the process from the machine's user. See
"Transparency" below — it's a design constraint, not a disclaimer.

---

## 1. Principles

- **Config-driven, no local UI.** Behaviour comes from a YAML file pulled from
  Azure Storage. The only on-device UI is a tray icon + toast notifications, so
  the tool is visible rather than covert.
- **Transparent by design.** The agent appears in the tray, in Services, and in
  Task Manager under its real name. Monitoring scope is what the kid has been
  told it is. (This is also what keeps it on the right side of AV and the law.)
- **Fail-open on enforcement, fail-safe on logging.** If config can't be
  fetched, keep the last-known-good config; never hard-lock a laptop because the
  network blipped. If logs can't upload, queue locally and retry.
- **Browser-agnostic where possible.** Prefer the DNS layer over per-browser
  hooks for domain control; add a browser extension only for what DNS can't do.

---

## 2. Architecture

```
  Each kid's laptop                         Azure                      Antonio
 +--------------------------+        +-----------------------+    +-----------+
 |  pc-kid-watch Agent      |        |  Storage account      |    |  iPhone   |
 |  (.NET Windows Service)  |        |   container: config   |    |   PWA     |
 |                          |  GET   |     <device>.yaml      |    | (Static   |
 |  - config poller  -------+------->|   container: logs     |    |  Web App) |
 |  - process/time engine   |  PUT   |     <device>/<date>.   |    +-----+-----+
 |  - DNS filter + logger --+------->|        jsonl           |          | HTTPS
 |  - activity sampler      | append |   container: reports  |          | + auth
 |  - uploader (queue)      |        |     <device>/<date>.   |    +-----v-----+
 |  - tray icon + toasts    |        |        json            |<---+ Functions |
 +--------------------------+        +-----------+-----------+    |   API     |
                                                 |  read raw      | (aggregate|
                                                 +--------------->|  + serve) |
                                                       timer fn    +-----------+
```

Four build units:
1. **Agent** — the Windows service on each laptop.
2. **Storage layout** — containers + blob naming.
3. **Reports API** — Azure Functions: aggregate raw logs into daily reports and
   serve them to the PWA.
4. **PWA** — installable SvelteKit app for the iPhone.

---

## 3. Agent (Windows service)

**Stack:** .NET 10 Worker Service (`Microsoft.Extensions.Hosting`), C#,
functional style — immutable records for config/events, pure functions for the
budget math, side effects (kill, toast, upload) pushed to the edges. Runs as
`LocalSystem` so it can enumerate and terminate processes.

Internal loops (independent `BackgroundService`s or timers):

- **Config poller** — every `config.poll_minutes` (default 15): download
  `<device>/config.yaml` (its own container), validate, atomically swap the
  in-memory config. On
  failure keep last-known-good (persisted to `%ProgramData%\pc-kid-watch\`).
- **Process/time engine** — tick every ~15s:
  - Enumerate running processes; match against `apps` rules by exe name/path.
  - Accumulate per-app **foreground+running seconds** into today's counters.
  - At `limit - warn_minutes`: toast ("Roblox closes in 10 min").
  - At `limit`: action = `warn` | `kill`. `kill` terminates the matched
    process tree (Roblox spawns several, e.g. `RobloxPlayerBeta.exe`).
  - Enforce `schedule` windows (allowed hours/days) independently of the budget.
  - Counters persist to local SQLite and **reset at local midnight**, so a
    reboot mid-day doesn't gift extra time.
- **DNS filter + logger** — see section 4.
- **Activity sampler** — every `activity.sample_minutes`: record the foreground
  app + window title (title, not keystrokes) as a usage sample. Optional
  disclosed screenshots on a separate interval, off by default.
- **Uploader** — flush queued events to `<device>/events/<date>.jsonl` via append
  blob; retry with backoff; survive restarts (queue on disk).

---

## 4. Domain control + URL logging

Two tiers, because they solve different problems and have very different cost:

**Tier 1 — DNS layer (v0, browser-agnostic).** The agent runs a local DNS
resolver (in-process .NET library such as `ARSoft.Tools.Net`, no external daemon)
bound to `127.0.0.1`, set as the system resolver; it forwards to a real
upstream (e.g. 1.1.1.1 over DoH) and, per query:
- logs the domain + allow/deny + timestamp,
- denies (NXDOMAIN / sinkhole) domains that are `blocked` or outside their
  `schedule` window.

This gives clean **block + access logging for every browser and app**. What it
*cannot* give is "minutes spent on youtube.com" — DNS sees a lookup, not dwell
time. So per-domain **time budgets** are not a Tier-1 feature.

**Tier 2 — browser extension (later).** A small MV3 extension (Chrome/Edge) +
WebExtension (Firefox) reports active-tab domain + foreground seconds to the
agent over `localhost`. Only this gives true per-domain time budgets and
accurate "time on site". Treat as a v0.2 add-on; the config already reserves
`daily_limit_minutes` on url rules for when it lands.

**v0 approximation for "limit time on a site":** combine a browser app-time
budget (Tier 1 process engine, e.g. "3h of browser/day") with scheduled blocks
of specific domains. Honest about the gap — true per-site time needs Tier 2.

---

## 5. Config schema (YAML)

Downloaded as `<device>/config.yaml` (the device's own container). Example:

```yaml
device: lenovo-mia
version: 3                 # bumped on each edit; agent logs which version is live

poll_minutes: 15           # config refresh
upload_minutes: 5          # log flush

timezone: Europe/Madrid    # drives midnight reset + schedules

apps:
  - name: Roblox
    match: ["RobloxPlayerBeta.exe", "RobloxStudioBeta.exe"]
    daily_limit_minutes: 90
    warn_minutes: 10
    action: kill           # kill | warn
    schedule:              # optional allowed windows; outside = blocked
      mon-fri: ["16:00-19:00"]
      sat-sun: ["10:00-13:00", "16:00-20:00"]
  - name: Browsers
    match: ["chrome.exe", "msedge.exe", "firefox.exe"]
    daily_limit_minutes: 180
    action: warn

urls:
  deny: ["*.example-blocked.com", "roblox.com/upgrades"]   # always blocked
  schedule:
    - domain: "youtube.com"
      allow: { mon-fri: ["17:00-19:00"], sat-sun: ["10:00-20:00"] }
  # daily_limit_minutes here is reserved for Tier 2 (browser extension)

activity:
  sample_minutes: 2        # foreground app/window-title sampling
  screenshots:
    enabled: false         # built but OFF for v0; disclosed-only
    mode: random           # fixed | random (random = jittered, less predictable)
    interval_minutes: 15   # fixed mode: every N minutes
    random_min_minutes: 10 # random mode: next shot at a random point in
    random_max_minutes: 40 #              [min, max] minutes

azure:
  logs_sas_ref: "logs"     # which SAS in the bootstrap secret to use for upload
```

**Validation:** on download, parse + schema-check; reject and keep last-known-good
if invalid. Log a `config_rejected` event so you see it in the report.

---

## 6. Log format

**Storage layout (resolved 2026-11-04):** one storage account; **one container
per device** (named for the device); **type-first prefixes**, each holding one
blob per local day. So: `<device>/config.yaml` (config mirror),
`<device>/events/<date>.jsonl` (this append blob), `<device>/reports/<date>.json`,
`<device>/shots/<date>/*.png`. Type-first (not day-first) because Azure Storage
lifecycle rules filter by path **prefix** only, so per-type retention ages
(events 90d, shots 30d, reports 365d) need `events/`, `shots/`, `reports/` as
stable prefixes. (Decided 2026-11-04.)

Raw events are JSON Lines, one append blob per device per local day
(`<device>/events/2026-11-04.jsonl`). One event per line:

```json
{"ts":"2026-11-03T17:42:10+01:00","dev":"lenovo-mia","type":"app_sample","app":"Roblox","title":"Roblox"}
{"ts":"2026-11-03T18:12:00+01:00","dev":"lenovo-mia","type":"budget","target":"Roblox","event":"warned","used_min":80,"limit_min":90}
{"ts":"2026-11-03T18:22:03+01:00","dev":"lenovo-mia","type":"budget","target":"Roblox","event":"killed","used_min":90}
{"ts":"2026-11-03T18:05:51+01:00","dev":"lenovo-mia","type":"dns","domain":"youtube.com","action":"deny","reason":"schedule"}
{"ts":"2026-11-03T09:00:00+01:00","dev":"lenovo-mia","type":"config","event":"applied","version":3}
```

Event `type`s: `app_sample`, `budget`, `dns`, `screenshot` (stores a blob ref,
not the image inline), `config`, `agent` (start/stop/heartbeat).

Append blobs cap at 50k blocks; a new day = a new blob, so that's a non-issue.

---

## 7. Reports API (Azure Functions)

**Stack:** .NET 10 isolated-worker Functions, C# — one language across agent and
API (decided 2026-11-04).

- **Timer function** (hourly + end-of-day): read today's raw `.jsonl` for each
  device, aggregate into `<device>/reports/<date>.json`:
  - per-app minutes used vs limit, warn/kill events,
  - top domains, denied attempts (count + which),
  - timeline of activity samples,
  - agent health (last heartbeat, config version live).
- **HTTP functions** for the PWA: `GET /api/devices`,
  `GET /api/report?device=&date=`, `GET /api/range?device=&from=&to=` for weekly
  rollups. Serve pre-aggregated JSON; don't make the phone parse raw logs.

---

## 8. PWA (iPhone)

**Stack:** SvelteKit (matches flowtrack), hosted on **Azure Static Web Apps**
free tier, which also hosts the Functions API under `/api` and gives built-in
auth so only you can open it.

- Installable: web app manifest + service worker; "Add to Home Screen" on iOS
  gives an app icon and standalone chrome. Cache last-fetched report for offline
  viewing.
- Views: device picker -> today's dashboard (budget rings per app, denied-domain
  list, timeline) -> week view. Read-only in v0 (viewing reports). Editing config
  from the phone is a v0.2 idea (PWA writes `config/<device>.yaml`).
- **Auth:** SWA built-in auth (Entra ID / your Microsoft account). No secrets in
  the client; the Functions API is the trust boundary.

---

## 9. Auth & security

- **Agent -> Azure:** bootstrap secret installed with the agent, DPAPI-encrypted
  under the machine account in `%ProgramData%\pc-kid-watch\secret.dat`. Holds
  scoped **SAS tokens** limited to that device's **own container** (read
  `config.yaml`, write/append events + reports + shots). No account key on the
  laptop, no access to other devices' containers. Rotate SAS by reissuing +
  redeploying the secret.
- **PWA -> API:** SWA auth; API checks the authenticated principal.
- **Blast radius:** a stolen laptop secret can read that device's config and
  append logs — it cannot read other devices or reports, and cannot delete.
- Logs contain window titles and domains (not keystrokes, not form content).
  Still personal data about a minor — keep the storage account private, lifecycle
  rules (Azure Storage lifecycle) to auto-delete: raw logs after 90 days,
  screenshots after 30, reports after 365 (defaults, tunable).

---

## 10. Build phases

1. **Agent skeleton** — Worker Service, config poller (local file first, then
   Azure), local SQLite counters, tray icon, structured event log to disk.
2. **Time engine** — process match, accumulation, schedules, warn/kill, midnight
   reset. Test with Roblox. This is the core value; ship it first.
3. **Storage + uploader** — containers, SAS bootstrap, append-blob uploader with
   on-disk retry queue.
4. **DNS layer** — local resolver, deny lists, schedule enforcement, dns events.
5. **Reports API** — aggregation timer + HTTP endpoints.
6. **PWA** — SvelteKit dashboard on Static Web Apps, SWA auth, installable.
7. **(v0.2)** browser extension for per-domain time; config editing from PWA.

---

## 11. Resolved decisions (2026-11-04)

1. **Agent:** C# on **.NET 10** (current LTS; no reason to start on the .NET 8
   first sketched).
2. **DNS:** in-process .NET resolver **library** (e.g. `ARSoft.Tools.Net`), no
   external daemon.
3. **Reports API:** C# on **.NET 10** isolated Functions — one language end to end.
4. **Storage:** one account; **one container per device**; **type-first prefixes**
   (`events/`, `reports/`, `shots/`), each with one blob per day. (Revised from
   "folder per day" on 2026-11-04 so lifecycle prefix rules can set per-type ages.)
5. **Retention:** Azure Storage lifecycle auto-delete — events 90d, shots 30d,
   reports 365d (tunable).
6. **Screenshots:** build the capability but ship it **OFF** for v0;
   disclosed-only; interval `fixed` or `random` (jittered) per config.

Azure placement (2026-11-04): **NOT** on any BeAI company subscription — this is
a personal tool holding a minor's data. Use a personal Azure account; region
West Europe; redundancy LRS.

Naming: folder `pc-kid-watch`, repo/remote `kid-watch` — left as-is by choice.
