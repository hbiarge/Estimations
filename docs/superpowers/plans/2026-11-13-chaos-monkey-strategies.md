# Chaos Monkey Strategies Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the chaos monkey into a runner of named strategies. Each strategy step sets chaos scenarios and resilience profiles together. A strategy is started, stopped or advanced from Aspire dashboard commands.

**Architecture:** The settings hold a dictionary of strategies, each made of steps or a random block. A singleton `StrategyRunner` runs at most one strategy in the background and resets everything when the strategy ends, is stopped or is replaced. The monkey becomes a small web app with `/strategies` endpoints in Development, and the AppHost adds one command per strategy plus stop and next.

**Tech Stack:** .NET 11, ASP.NET Core minimal APIs, Aspire AppHost (`WithHttpCommand`), xUnit v3 + Shouldly, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.AspNetCore.TestHost`.

**Spec:** `docs/superpowers/specs/2026-11-13-chaos-monkey-strategies-design.md`

## Global Constraints

- Chaos targets a step doesn't name go to `healthy`; resilience callers it doesn't name go to `standard`. A reset sets all of them to those defaults.
- One strategy at a time: starting one cancels the running one and waits for its reset first.
- The reset timeout stays 15 s, and it also runs when the host stops.
- The `/strategies` endpoints are mapped only in Development, like `/chaos` and `/resilience`.
- Strategy names come from `Constants.ChaosMonkey.Strategies`: `retries`, `retry-storm`, `hedging`, `timeouts`, `callbacks`, `bad-day`, `tour`, `random`.
- Resilience callers: `imagedownloads`, `imageprocessor`, `estimationprocessor`, `callbacknotifier`, using the profiles in `Constants.Resilience.Profiles`.
- Each step-based learning strategy is a 30 s healthy `Baseline`, its phases, then a 1 min `Recovery`, with `Repeat: false`. A phase that should change a chart lasts at least 2 min, or 3 min for `lost-callbacks`.
- Don't use `sed -i` on files under `src/Aspire/Acheve.AppHost/grafana/`. Use the Edit tool.
- Every task ends with `dotnet build Estimations.slnx` green and `dotnet test --project test/Tools/Acheve.Tools.ChaosMonkey.Tests` passing.

## Review Focus

- **Starting a strategy while another is mid-step:** the old one's reset must finish before the new one's first step is applied. Otherwise a late reset would undo the new step. Covered in Task 2 (`Starting_a_strategy_replaces_the_running_one_after_its_reset`).
- **Next step on the last step of a non-repeating strategy:** the strategy finishes and resets, as if its time ran out. It must not throw or hang. Covered in Task 2.
- **Stop or next while idle:** both are no-ops that answer 200 and don't switch anything. Covered in Tasks 2 and 3.
- **A caller service that isn't running:** its profile switch fails, is logged and counted, and the step goes on, just like an unreachable chaos target. Covered in Task 2.
- **Repeated commands, like a double click:** starting the strategy that's already running restarts it from step 1 cleanly. It must not leave two loops running. Covered in Task 2 by the replace test, since the same code path handles both.

---

### Task 1: Strategy settings, validation, plans and the shipped strategies

**Files:**
- Modify: `src/Common/Acheve.Common.Shared/Constants.cs`
- Modify: `src/Tools/Acheve.Tools.ChaosMonkey/ChaosMonkeyOptions.cs`
- Create: `src/Tools/Acheve.Tools.ChaosMonkey/ResilienceTargets.cs`
- Modify: `src/Tools/Acheve.Tools.ChaosMonkey/ChaosPlan.cs`
- Modify: `src/Tools/Acheve.Tools.ChaosMonkey/ChaosMonkeyWorker.cs`. This is a stopgap so the build stays green: the worker runs the `tour` strategy, chaos only, until Task 2 deletes it.
- Modify: `src/Tools/Acheve.Tools.ChaosMonkey/appsettings.json`
- Test: `test/Tools/Acheve.Tools.ChaosMonkey.Tests/ChaosMonkeyOptionsValidatorTests.cs`, `ChaosPlanTests.cs`, `ChaosMonkeyWorkerTests.cs` (retarget only)

**Interfaces:**
- Produces:
  - `Constants.ChaosMonkey` with `const string` fields `Retries`, `RetryStorm`, `Hedging`, `Timeouts`, `Callbacks`, `BadDay`, `Tour` and `Random`, plus `static readonly string[] Strategies` listing all of them in that order.
  - `ChaosMonkeyOptions { Dictionary<string, StrategyOptions> Strategies }`. The `Mode`, `Repeat`, `Timeline` and `Random` properties and the `ChaosMonkeyMode` enum are removed.
  - `StrategyOptions { bool Repeat; List<ChaosStepOptions> Steps = []; RandomChaosOptions? Random }`.
  - `ChaosStepOptions { string? Name; TimeSpan Duration; Dictionary<string,string> Chaos = []; Dictionary<string,string> Resilience = [] }`. The old `Scenarios` property is renamed to `Chaos`.
  - `RandomChaosOptions` is unchanged.
  - `internal static class ResilienceTargets { IReadOnlyList<string> Names; bool Supports(string caller, string profile) }`, modelled on `ChaosTargets`. `Names` holds `Constants.Resources.ImageDownloads`, `ExternalImageProcessor`, `EstimationProcessor` and `CallbackNotifier`, each supporting every profile in `Constants.Resilience.Profiles`.
  - `ChaosMonkeyOptionsValidator` validates the new shape. It prefixes its messages with `Strategy '{name}'` and, for a step, `Strategy '{name}' step {i} ({stepName})`. It checks:
    - The strategy set matches `Constants.ChaosMonkey.Strategies` in both directions.
    - Each strategy has exactly one of a non-empty `Steps` list or `Random`.
    - The existing step checks still apply.
    - Each resilience target and profile is valid.
    - The existing random checks still apply.
  - `internal sealed record ChaosStep(string Name, TimeSpan Duration, IReadOnlyDictionary<string,string> Chaos, IReadOnlyDictionary<string,string> Resilience)`, with:
    - `string ScenarioFor(string target)`, defaulting to `healthy`.
    - `string ProfileFor(string caller)`, defaulting to `standard`.
    - `ToString()`, which reads `"{Name}: {chaos list}; {resilience list} for {Duration}"`, leaves out empty parts and uses `"everything healthy"` when both are empty.
  - `ChaosPlan.Create(StrategyOptions strategy, Random random) : IEnumerable<ChaosStep>`. It uses `RandomSteps` when `strategy.Random` is set, and otherwise `Steps`, repeated while `Repeat` is on. `ChaosPlan.Steps(StrategyOptions)` and `ChaosPlan.RandomSteps(RandomChaosOptions, Random)` stay accessible to the tests. Random steps have an empty `Resilience`.

- [ ] **Step 1: Rewrite the validator and plan tests for the new shape**

In the validator tests, the helper builds `Strategies` with every name in `Constants.ChaosMonkey.Strategies`, each holding one valid 1-minute empty step, and each test overrides one entry. Keep the existing tests: shipped settings, unknown target, unsupported scenario, zero duration, empty steps, and random min/max. Retarget them and expect the same message fragments. `The_shipped_settings_are_valid` asserts `options.Strategies.Keys.ShouldBe(Constants.ChaosMonkey.Strategies, ignoreOrder: true)`. Add these tests:

```csharp
[Fact] public void An_unknown_resilience_target_fails()          // Resilience { ["api"] = "standard" } → contains "unknown resilience target 'api'"
[Fact] public void An_unknown_resilience_profile_fails()         // Resilience { ["imagedownloads"] = "turbo" } → contains "imagedownloads doesn't support profile 'turbo'"
[Fact] public void A_strategy_missing_from_the_settings_fails()  // remove "hedging" → contains "Strategy 'hedging' is not configured"
[Fact] public void A_strategy_not_in_the_constants_fails()       // add "chaos-party" → contains "Strategy 'chaos-party' has no dashboard command"
[Fact] public void A_strategy_with_steps_and_random_fails()      // both set → contains "either Steps or Random"
```

In the plan tests, keep the five existing tests and build them from `StrategyOptions`. Add these tests:

```csharp
[Fact] public void Callers_a_step_does_not_name_are_standard()
    // step with Resilience { [ImageDownloads] = FailFast }:
    // ProfileFor(ImageDownloads) == FailFast; ProfileFor(EstimationProcessor) == Standard
[Fact] public void Random_steps_never_change_resilience()       // RandomSteps(...).Take(10): every step's Resilience.ShouldBeEmpty()
```

In `ChaosMonkeyWorkerTests`, only change how the options are built: one strategy named `tour`, with the same two steps.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project test/Tools/Acheve.Tools.ChaosMonkey.Tests`
Expected: the build fails (`Strategies`, `Constants.ChaosMonkey`, `ProfileFor` and `Resilience` don't exist yet).

- [ ] **Step 3: Implement the constants, options, `ResilienceTargets`, validator, `ChaosStep` and `ChaosPlan` as listed in Interfaces**

The stopgap worker change is `ChaosPlan.Create(settings.Strategies[Constants.ChaosMonkey.Tour], random)`. Its log line no longer prints the mode.

- [ ] **Step 4: Write the shipped strategies in `appsettings.json`**

Replace the `ChaosMonkey` section with `Strategies`, keeping the style of the comments:

| Strategy | Steps between the 30 s `Baseline` and the 1 min `Recovery` |
| --- | --- |
| `retries` | 2 m `external-imageprocess: flaky` + `imageprocessor: none`; then 2 m `external-imageprocess: flaky` (caller back to standard) |
| `retry-storm` | 2 m `external-images: down` + `imagedownloads: retry-only`; then 2 m `external-images: down` + `imagedownloads: fail-fast` |
| `hedging` | 2 m `external-estimations: flaky`; then 2 m `external-estimations: flaky` + `estimationprocessor: hedging` |
| `timeouts` | 2 m `external-estimations: slow`; then 2 m `external-estimations: slow` + `estimationprocessor: fail-fast` |
| `callbacks` | 3 m `external-imageprocess: lost-callbacks`; then 2 m `external-estimations: duplicate-callbacks` |
| `bad-day` | 4 m `external-imageprocess: flaky`, `external-estimations: slow`, `external-notifications: down`, `api: flaky` |
| `tour` | Today's timeline steps unchanged, with `Scenarios` renamed to `Chaos`. `Repeat: true`, and no added baseline or recovery. |
| `random` | `"Random": {...}` with today's values (1 m, 3 m, 1 m gap, 1 target). |

Step names say what to look for, for example `"Images down, retries only (every call becomes four)"`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet build Estimations.slnx`, then `dotnet test --project test/Tools/Acheve.Tools.ChaosMonkey.Tests`. Expected: the build is green and all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Common/Acheve.Common.Shared/Constants.cs src/Tools/Acheve.Tools.ChaosMonkey test/Tools/Acheve.Tools.ChaosMonkey.Tests
git commit -m "Chaos monkey: named strategies with chaos and resilience per step"
```

### Task 2: StrategyRunner replaces the worker

**Files:**
- Modify: `src/Tools/Acheve.Tools.ChaosMonkey/ChaosClient.cs`
- Modify: `src/Tools/Acheve.Tools.ChaosMonkey/ChaosMonkeyMetrics.cs`
- Create: `src/Tools/Acheve.Tools.ChaosMonkey/StrategyRunner.cs`
- Delete: `src/Tools/Acheve.Tools.ChaosMonkey/ChaosMonkeyWorker.cs`
- Modify: `src/Tools/Acheve.Tools.ChaosMonkey/Program.cs`: register `StrategyRunner` as a singleton and as a hosted service (the same instance) in place of the worker. The host stays a Worker host until Task 3.
- Create: `test/Tools/Acheve.Tools.ChaosMonkey.Tests/FakeChaosClient.cs`. It's today's `FakeClient`, extended with `SetProfileAsync` and a single `Calls` queue of `(Kind, Target, Value)`.
- Create: `test/Tools/Acheve.Tools.ChaosMonkey.Tests/StrategyRunnerTests.cs`. Delete `ChaosMonkeyWorkerTests.cs`.
- Modify: `test/Tools/Acheve.Tools.ChaosMonkey.Tests/Acheve.Tools.ChaosMonkey.Tests.csproj`: add a `PackageReference` to `Microsoft.Extensions.TimeProvider.Testing`.

**Interfaces:**
- Consumes: `ChaosPlan.Create`, `ChaosStep`, `ChaosTargets` and `ResilienceTargets` from Task 1.
- Produces:
  - `IChaosClient.SetProfileAsync(string caller, string profile, CancellationToken) : Task<bool>`, which sends `POST https+http://{caller}/resilience/profiles/{profile}`. It handles errors the same way as `SetScenarioAsync`.
  - `ChaosMonkeyMetrics.StepStarted(string strategy)` adds the tag `chaos.strategy`.
  - `ChaosMonkeyMetrics.Switched(string kind, string target, string value, bool succeeded)` uses the tags `chaos.kind` (`"chaos"` or `"resilience"`), `chaos.target`, `chaos.value` and `chaos.result`. `chaos.value` replaces the old `chaos.scenario` tag. Nothing in Grafana reads it.
  - `internal sealed record StrategyStatus(string? Strategy, int? Step, string? StepName)`. All fields are null when idle.
  - `internal sealed partial class StrategyRunner(IChaosClient, ChaosMonkeyMetrics, IOptions<ChaosMonkeyOptions>, TimeProvider, Random, ILogger<StrategyRunner>) : IHostedService`, with:
    - `Task<bool> RunAsync(string strategy, CancellationToken)`: returns false for an unknown name. Otherwise it cancels and awaits the running strategy (and so its reset), starts the new one in the background, and returns true once the run has started. It doesn't wait for the first step.
    - `Task CancelAsync(CancellationToken)`: cancels and awaits the running strategy, and its reset. A no-op when idle.
    - `void SkipStep()`: ends the current step's wait now. A no-op when idle.
    - `StrategyStatus Status { get; }`
    - `Task Completion { get; }`: the running run's task, or `Task.CompletedTask`. It exists so tests can await a run's end.
    - `IHostedService.StartAsync` does nothing. `IHostedService.StopAsync` calls `CancelAsync`.
  - Logs:
    - `Strategy {strategy} started`
    - `Chaos step {number} of {strategy}: {step}`, at Warning level, so the existing Grafana log query `Chaos step` still matches.
    - `Strategy {strategy} finished`
    - `Strategy {strategy} stopped`
    - `Couldn't switch {target} to {value}; is it running?`
    - `Every target is back to healthy and every caller to standard.`

The behaviour, carried over from `ChaosMonkeyWorker`:
- **Applying a step** sends `SetScenarioAsync` to every `ChaosTargets.Names` entry and `SetProfileAsync` to every `ResilienceTargets.Names` entry, all in parallel, recording each in `Switched(...)`.
- **The wait** is `Task.Delay(step.Duration, time, token)`, where `token` links the run's token with a per-step `CancellationTokenSource` that `SkipStep` cancels. A cancellation caused only by the skip goes on to the next step.
- **Reset** happens in `finally`, with a fresh 15 s timeout token.
- **Concurrency:** a `SemaphoreSlim(1,1)` guards `RunAsync` and `CancelAsync`, so two of them never interleave.

- [ ] **Step 1: Write the runner tests**

Each test's options carry the strategies it needs directly, since the runner only looks names up in its options. Step durations of 10 min use a `FakeTimeProvider` that the test never advances unless stated. "Wait for round N" means polling `client.Calls.Count` with a 5 ms delay, like today's `Stopping_the_monkey…` test, with the test's cancellation token as the timeout. A round has `ChaosTargets.Scenarios.Count + ResilienceTargets.Names.Count` calls.

```csharp
[Fact] Runs_each_step_switching_chaos_and_resilience_then_resets()
    // 20 ms steps, TimeProvider.System; step 1: Chaos {imageprocess: lost-callbacks}, Resilience {imageprocessor: none}; step 2: {}; Repeat=false
    // await RunAsync("test"); await Completion
    // round 1: imageprocess=lost-callbacks, other chaos healthy; imageprocessor=none, other callers standard
    // last round: all chaos healthy, all callers standard
    // switches collector count == calls count; some measurement has chaos.kind="resilience"
[Fact] An_unknown_strategy_is_refused()                       // RunAsync("nope") == false; no calls
[Fact] Starting_a_strategy_replaces_the_running_one_after_its_reset()
    // run "a", wait round 1; RunAsync("b") → round 2 is a full reset (all defaults), round 3 is b's first step; Status.Strategy == "b"
[Fact] Next_step_skips_the_wait()                             // run, wait round 1, SkipStep(), wait round 2; Status.Step == 2
[Fact] Next_step_on_the_last_step_finishes_and_resets()       // two steps, Repeat=false; SkipStep() after each round → Completion completes; last round all defaults; Status all null
[Fact] Cancel_resets_and_cancel_when_idle_does_nothing()       // CancelAsync while running → reset round, Status idle; CancelAsync again → no new calls
[Fact] An_unreachable_caller_does_not_stop_the_others()        // FakeChaosClient(unreachable: "callbacknotifier") → every round complete; a measurement has chaos.result="failed"
[Fact] Stopping_the_host_resets()                              // ((IHostedService)runner).StopAsync → reset round
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project test/Tools/Acheve.Tools.ChaosMonkey.Tests --filter-class "*StrategyRunnerTests"`
Expected: the build fails (`StrategyRunner` and `SetProfileAsync` are missing).

- [ ] **Step 3: Implement the client and metrics changes and `StrategyRunner`, delete the worker and its tests, and update `Program.cs`**

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build Estimations.slnx`, then the full test project. Expected: the build is green and all tests pass.

- [ ] **Step 5: Commit** (message: "Chaos monkey: a strategy runner that can be started, stopped and advanced")

### Task 3: Web host and /strategies endpoints

**Files:**
- Modify: `src/Tools/Acheve.Tools.ChaosMonkey/Acheve.Tools.ChaosMonkey.csproj`: change `Sdk` to `Microsoft.NET.Sdk.Web`.
- Create: `src/Tools/Acheve.Tools.ChaosMonkey/StrategyEndpoints.cs`
- Modify: `src/Tools/Acheve.Tools.ChaosMonkey/Program.cs`
- Modify: `src/Tools/Acheve.Tools.ChaosMonkey/Properties/launchSettings.json`: add `ASPNETCORE_ENVIRONMENT=Development` and an `applicationUrl` on a port no other project's launchSettings uses, and keep `DOTNET_ENVIRONMENT`.
- Modify: `test/Tools/Acheve.Tools.ChaosMonkey.Tests/Acheve.Tools.ChaosMonkey.Tests.csproj`: add `Microsoft.AspNetCore.TestHost`.
- Test: `test/Tools/Acheve.Tools.ChaosMonkey.Tests/StrategyEndpointsTests.cs`

**Interfaces:**
- Consumes: `StrategyRunner`, `StrategyStatus` and `FakeChaosClient` from Task 2.
- Produces: `internal static IEndpointRouteBuilder MapStrategyEndpoints(this IEndpointRouteBuilder app)`, mapping a group `/strategies` with `.ExcludeFromDescription()`:
  - `GET /`: `200 { strategies: string[], running: StrategyStatus }`, where `strategies` holds the configured names.
  - `POST /stop`: calls `CancelAsync` and answers `200` with the status.
  - `POST /next`: calls `SkipStep` and answers `200` with the status.
  - `POST /{name}`: `200` with the status when `RunAsync` returns true, otherwise `404` with `"Unknown strategy '{name}'. Use one of: …"`. The literal routes `/stop` and `/next` take precedence over `/{name}`.

`Program.cs` switches to `WebApplication.CreateBuilder`, keeps every registration, calls `app.MapDefaultEndpoints()` and maps the strategy endpoints only when `app.Environment.IsDevelopment()`. Update the top comment.

- [ ] **Step 1: Write the endpoint tests**

Build a `WebApplication` with `UseTestServer()`, the runner over a `FakeChaosClient`, options with one strategy `"test"` (10 min steps), and `MapStrategyEndpoints()`.

```csharp
[Fact] Get_lists_the_strategies_and_reports_idle()           // 200; strategies == ["test"]; running.strategy null
[Fact] Post_a_strategy_starts_it()                           // POST /strategies/test → 200; running.strategy == "test"
[Fact] Post_an_unknown_strategy_is_404()                     // POST /strategies/nope → 404; body contains "Unknown strategy 'nope'"
[Fact] Stop_and_next_when_idle_answer_200()                  // both 200; no client calls
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project test/Tools/Acheve.Tools.ChaosMonkey.Tests --filter-class "*StrategyEndpointsTests"`. Expected: the build fails.

- [ ] **Step 3: Implement the SDK switch, `StrategyEndpoints.cs` and `Program.cs`**

- [ ] **Step 4: Run the build and the full test project.** Expected: the build is green and all tests pass.

- [ ] **Step 5: Commit** (message: "Chaos monkey: /strategies endpoints to start, stop and advance strategies")

### Task 4: AppHost commands

**Files:**
- Create: `src/Aspire/Shared/ChaosMonkeyCommands.cs`
- Modify: `src/Aspire/Acheve.AppHost/Acheve.AppHost.csproj`: add `<Compile Include="..\Shared\ChaosMonkeyCommands.cs" Link="ChaosMonkeyCommands.cs" />` next to the other two.
- Modify: `src/Aspire/Acheve.AppHost/AppHost.cs`: the chaos monkey block, and the `AddCallerService` calls.

**Interfaces:**
- Consumes: the `/strategies` routes (Task 3) and `Constants.ChaosMonkey` (Task 1).
- Produces: `public static IResourceBuilder<ProjectResource> WithStrategyCommands(this IResourceBuilder<ProjectResource> project)` in `namespace Aspire.Hosting`. It loops over `Constants.ChaosMonkey.Strategies` with a `Details` dictionary (description and icon) like `ChaosCommands`. It adds:
  - `Strategy: {name}`, sending `POST /strategies/{name}`, with the command name `strategy-{name}`.
  - `Strategy: stop`, sending `POST /strategies/stop`, with the icon `Stop`.
  - `Strategy: next step`, sending `POST /strategies/next`, with the icon `Next`.

  Each description is one sentence saying what the strategy shows, taken from the spec's "Shows" column.

In `AppHost.cs`, the `chaosmonkey` resource:
- Drops `.WithExplicitStart()`.
- Adds `.WithHttpHealthCheck("/health")` and `.WithStrategyCommands()`.
- Adds a `WithReference` to each of the four caller resources. Capture the return values of the `AddCallerService` calls into variables, since today they're discarded.

Rewrite its comment: the monkey starts idle, and a strategy is started from its Commands.

- [ ] **Step 1: Implement the commands file, the csproj link and the AppHost changes**
- [ ] **Step 2: Run `dotnet build Estimations.slnx`.** Expected: the build succeeds.
- [ ] **Step 3: Smoke test in the running app.** Run the application's AppHost and the external AppHost. In the Aspire dashboard, `chaosmonkey` should be running and healthy, with the Strategy commands visible. Then:
  - Run **Strategy: retry-storm**. The monkey's logs show `Strategy retry-storm started` and `Chaos step 1 of retry-storm`.
  - Run **Strategy: next step**. Step 2 is logged, and `imagedownloads` logs `Resilience profile changed` to `retry-only`.
  - Run **Strategy: stop**. The reset line is logged, and `imagedownloads` goes back to `standard`.
- [ ] **Step 4: Commit** (message: "AppHost: strategy commands on the chaos monkey")

### Task 5: Dashboard annotation and docs

**Files:**
- Modify: `src/Aspire/Acheve.AppHost/grafana/dashboards/chaos-run.json`. Use the Edit tool only.
- Modify: `docs/chaos.md`, `docs/resilience.md`, `README.md` (line 76)

Changes:
- **`chaos-run.json`:** append an annotation after "Resilience changes", in the same shape:
  - `name`: "Chaos monkey steps"
  - `expr`: `{service_name="chaosmonkey"} |= "Chaos step"`
  - `iconColor`: "purple"
  - `titleFormat`: "Chaos monkey"
  - `tagKeys`: ""

  Leave the logs panel as it is.
- **`docs/chaos.md`, "Chaos monkey" section:** rewrite it. Cover:
  - The monkey starts idle.
  - The Strategy commands: name, stop, next step.
  - The `/strategies` endpoints.
  - A table of the strategies: name, what's broken, what's switched, and what to look at on the Chaos run dashboard. Use the panel names: "Calls to external systems", "Resilience events", "Client success rate", "Client case duration", "Saga outcomes".
  - The settings shape (one step example with `Chaos` and `Resilience`).
  - The metric tags (`chaos.strategy`, `chaos.kind`, `chaos.value`).
  - The new annotation.

  Remove the Timeline/Random mode text, and describe `tour` and `random` as strategies instead.
- **`docs/resilience.md`, "Pairings worth showing":** add a "Strategy" column naming the strategy that runs each pairing (the bulkhead row has none), plus a sentence pointing to [chaos.md](chaos.md#chaos-monkey).
- **`README.md` line 76:** replace "press **Start** on the `chaosmonkey` resource to switch them over time from a timeline (or at random)" with a line about running a strategy from the `chaosmonkey` resource's Commands.
- Don't touch `docs/talk/`.

- [ ] **Step 1: Make the edits**
- [ ] **Step 2: Check the dashboard JSON parses:** `python3 -c "import json;json.load(open('src/Aspire/Acheve.AppHost/grafana/dashboards/chaos-run.json'))"`. Expected: no output.
- [ ] **Step 3: Check the docs' links:** `grep -n "chaos.md#chaos-monkey" docs/resilience.md` finds a line, and `grep -n "^## Chaos monkey" docs/chaos.md` finds the heading.
- [ ] **Step 4: Commit** (message: "Docs and Chaos run dashboard: chaos monkey strategies")
