# Chaos monkey strategies — design

## Intent

The chaos monkey is a way to learn and observe how the system behaves. Short runs (about 4–6 minutes each) should each show one thing: what a resilience strategy buys, what it costs, or what it can't see. They must be easy to start live, for example while presenting. Today the chaos monkey only switches chaos scenarios, from one long timeline or at random; resilience profiles are switched by hand. A **strategy** is a named, scripted story: each step sets chaos scenarios on the external systems (and the Api) **and** resilience profiles on the services that call them. Typically the chaos stays the same while the profile flips halfway, so the Chaos run dashboard shows a before and after. The presenter starts a strategy with one click in the Aspire dashboard.

## Goals

- Pick and start a strategy from the Aspire dashboard (`chaosmonkey` resource → Commands → **Strategy: \<name\>**), no restart, no config edit.
- Each step sets chaos per target and resilience profile per caller service.
- Ship the learning strategies below, sized so every phase is visible on the Grafana charts.
- Keep the existing long timeline and random mode for background runs, as strategies.
- The presenter can stop a strategy, or skip to its next step early.

## Non-goals

- Starting or tuning the traffic generator (the presenter starts it before the demos).
- Custom resilience settings (`PUT /resilience`, e.g. the bulkhead demo). Named profiles only.
- Custom chaos settings (`PUT /chaos`). Named scenarios only.
- Running in the external systems' AppHost.
- Changing the talk blueprint (`docs/talk/`). Hugo chooses its demos separately.

## Anti-goals

- A strategy that leaves anything broken behind: when a strategy ends, is stopped, or is replaced, every chaos target goes back to `healthy` and every caller back to `standard`.
- Two strategies fighting: starting one stops the one that's running.
- Phases too short to read on the charts. The dashboard uses `$__rate_interval` rates and a 5 m success-rate window, and the saga's callback wait timeout is about 120 s, so a phase that should change a chart lasts at least 2 minutes (3 for `lost-callbacks`).

## Approaches

- **A. One `chaosmonkey` resource with a dashboard command per strategy (chosen).** The monkey gets HTTP endpoints to run, stop and advance strategies; the AppHost adds one command per strategy plus Stop and Next step. One resource, one strategy at a time, manual pacing possible. Costs: the monkey becomes a web app and the worker becomes a controllable runner.
- **B. One Aspire resource per strategy**, same project with a different `ChaosMonkey__Strategy`, explicit start. Least code, but six-plus extra resources in the graph shown in Demo 1, nothing prevents two running at once, and only Start/Stop as control.
- **C. Strategy picked in config, restart to change.** Ruled out: editing settings on stage.

Hugo picked **A**: it matches how chaos and resilience are already driven from the Commands menu, keeps the resource graph clean (it's shown when presenting), and is the only one that allows Next step.

## Design

### Model and configuration

`appsettings.json`, section `ChaosMonkey`:

```jsonc
"ChaosMonkey": {
  "Strategies": {
    "retry-storm": {
      "Repeat": false,
      "Steps": [
        { "Name": "Baseline", "Duration": "00:00:30" },
        { "Name": "Images down, retries only (every call becomes four)", "Duration": "00:02:00",
          "Chaos": { "external-images": "down" }, "Resilience": { "imagedownloads": "retry-only" } },
        { "Name": "Images down, fail fast (the circuit opens)", "Duration": "00:02:00",
          "Chaos": { "external-images": "down" }, "Resilience": { "imagedownloads": "fail-fast" } },
        { "Name": "Recovery", "Duration": "00:01:00" }
      ]
    },
    "random": { "Random": { "MinDuration": "00:01:00", "MaxDuration": "00:03:00", "HealthyGap": "00:01:00", "MaxTargets": 1 } }
  }
}
```

- A step sets `Chaos` (target → scenario) and `Resilience` (caller → profile). Chaos targets it doesn't name go to `healthy`; callers it doesn't name go to `standard`. (Same "unnamed means default" rule as today.)
- A strategy is either a list of `Steps` (with `Repeat`) or a `Random` block (the current random mode, chaos only, endless). `Mode` and the top-level `Timeline`/`Random` go away.
- Chaos targets: as today (`ChaosTargets`). Resilience targets: `imagedownloads`, `imageprocessor`, `estimationprocessor`, `callbacknotifier`, all profiles in `Constants.Resilience.Profiles`.
- Strategy names and their dashboard descriptions live in shared code (`Constants` plus a `ChaosMonkeyCommands.cs` next to `ChaosCommands.cs`), because the AppHost builds the commands without reading the monkey's settings. The options validator fails at start when a configured strategy isn't in that list or a listed one isn't configured, plus the existing checks (unknown target, unsupported scenario, non-positive duration) extended to resilience targets/profiles.

### Strategies shipped

Every step-based strategy starts with a 30 s healthy baseline and ends with a 1 min recovery, `Repeat: false`.

| Name | Chaos | Resilience steps | Shows |
| --- | --- | --- | --- |
| `retries` | `external-imageprocess` flaky | `imageprocessor` `none` 2 m → `standard` 2 m | Failed cases collapse once retries are on; retry events and extra calls appear. |
| `retry-storm` | `external-images` down | `imagedownloads` `retry-only` 2 m → `fail-fast` 2 m | Retries multiply load on a dead system; the breaker opens and calls drop to almost zero. |
| `hedging` | `external-estimations` flaky | `estimationprocessor` `standard` 2 m → `hedging` 2 m | p95 case duration drops; hedged attempts cost extra calls. |
| `timeouts` | `external-estimations` slow | `estimationprocessor` `standard` 2 m → `fail-fast` 2 m | Resilience that hurts: slow-but-successful cases become failures. |
| `callbacks` | `external-imageprocess` lost-callbacks 3 m → duplicate-callbacks 2 m | all `standard` | HTTP resilience can't see lost callbacks; the saga's timeouts and idempotency are the protection. |
| `bad-day` | `external-imageprocess` flaky, `external-estimations` slow, `external-notifications` down, `api` flaky, 4 m | all `standard` | Several things wrong at once; a stress test of the dashboards. |
| `tour` | today's 18-minute timeline, `Repeat: true` | all `standard` | Background run (unchanged content). |
| `random` | today's random mode | all `standard` | Background run (unchanged behaviour). |

### Runner and HTTP surface

- The project moves from the Worker SDK to the Web SDK and maps its endpoints (Development only, like `/chaos` and `/resilience`):
  - `GET /strategies`: strategy names, the running one, its current step number and name.
  - `POST /strategies/{name}`: start it. If one is running, cancel it and reset first. 404 for an unknown name.
  - `POST /strategies/stop`: cancel and reset. No-op when idle.
  - `POST /strategies/next`: end the current step's wait now and go to the next step. No-op when idle.
- A singleton runner replaces the worker's loop: it owns at most one running strategy (a task + a `CancellationTokenSource`) and a "skip" signal for the current wait. Apply a step = set every chaos target and every caller in parallel, as `ApplyAsync` does today. Reset = all chaos `healthy`, all callers `standard`, with the existing 15 s reset timeout. The host stopping also resets.
- `IChaosClient` gets `SetProfileAsync(target, profile)` calling `POST /resilience/profiles/{profile}` on the caller, same error handling as `SetScenarioAsync`.
- The monkey starts **idle**. The AppHost drops `WithExplicitStart()`, adds `WithReference` to the four caller services, and adds the commands: **Strategy: \<name\>** for each, **Strategy: stop**, **Strategy: next step**.

### Seeing it

- Logs: `Strategy {name} started`, the existing `Chaos step {n}: …` line (now including the resilience part and the strategy name), `Strategy {name} finished/stopped`, failed switches per target.
- Metrics (meter `Acheve.ChaosMonkey`): `chaos.monkey.steps` gets a `chaos.strategy` tag; switch counter gets a `chaos.kind` (`chaos` / `resilience`) tag.
- Chaos run dashboard: a third annotation, **Chaos monkey steps**, from the `Chaos step` log lines, so each step's name marks every chart during the demo. The existing logs panel query keeps working.

### Docs and tests

- Update `docs/chaos.md` (chaos monkey section: the strategies, what each one shows and what to look at on the Chaos run dashboard) and `docs/resilience.md` (point the pairings at the strategies). Not the talk blueprint.
- Tests in `Acheve.Tools.ChaosMonkey.Tests`: plan building per strategy (unnamed targets → defaults, repeat, random), validator (unknown strategy, resilience target/profile checks, constants ↔ config match), runner (start replaces the running one and resets, stop resets, next skips the wait, host stop resets) with a fake client and `FakeTimeProvider`.

## Left to the builder

Calls made during design (not discussed with Hugo): the 2-minute minimum phase length; endpoints only in Development; no custom resilience/chaos settings; step names as a dashboard annotation; the monkey starting idle with the AppHost. Also:

- Exact class names and how the runner signals "next" (e.g. a linked token per step).
- Icons and wording of the command descriptions.
- Whether `GET /strategies` returns step end time.
- The exact step texts of each strategy, within the table above.
