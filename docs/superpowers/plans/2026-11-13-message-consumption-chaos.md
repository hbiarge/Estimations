# Message Consumption Chaos Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pause and resume message consumption of any Rebus worker at run time, and a chaos monkey strategy `backlog` that shows a short pause (only latency) and then a long one (failed cases).

**Architecture:** A new library `Acheve.Common.Consumption` holds a `ConsumptionState` that sets Rebus's worker count to 0 and back, `/consumption` endpoints (Development only) and a gauge. Every Rebus worker uses it; the AppHost adds dashboard commands; the chaos monkey gets a third switch kind next to chaos and resilience.

**Tech Stack:** .NET, Rebus 8.10 (`IBus.Advanced.Workers`), Aspire, xunit v3 + Shouldly, Grafana JSON dashboards.

**Spec:** `docs/superpowers/specs/2026-11-13-message-consumption-chaos-design.md`

## Global Constraints

- Endpoints only in Development, like `/resilience`: `GET /consumption`, `POST /consumption/pause`, `POST /consumption/resume`.
- Workers: `imagedownloads`, `imageprocessor`, `estimationprocessor`, `callbacknotifier`, `processmanager`, `stateholder`. Not the Api.
- States: `paused`, `running`. Workers a step doesn't name go back to `running`; so does everything on reset.
- Meter `Acheve.Consumption`, gauge `consumption.paused` (1/0), tag `messaging.consumer` = service name. Warning log only on a real change.
- Monkey switch kind `consumption` in `chaos.monkey.switches`.
- `random` never pauses.
- Strategy `backlog` on `imagedownloads`: Baseline 30 s, A pause 60 s, Drain 90 s, B pause 3 min, Recovery 2 min.
- Never edit Grafana files with `sed -i` (breaks the LGTM single-file bind mount).
- Follow the existing style: partial classes with `[LoggerMessage]`, XML doc summaries, `Constants` for every name.

## Review Focus

- Pause twice in a row, or resume while running: no change, no log line, resume restores the original count (not 0).
- Resume after a pause restores the configured worker count, whatever it was (Rebus default is 1).
- Pause while a handler is running: the request waits for the handler to finish (Rebus's `SetNumberOfWorkers` blocks), the message is acked, not left unacked.
- Monkey reset after a strategy stops mid-pause: `imagedownloads` ends `running`.
- A monkey step naming an unknown worker or a state other than `paused`/`running` is refused at start.

---

### Task 1: Consumption library

**Files:**
- Create: `src/Common/Acheve.Common.Consumption/Acheve.Common.Consumption.csproj` (Microsoft.AspNetCore.App framework reference, `Rebus`, project ref `Acheve.Common.Shared`, `InternalsVisibleTo` the tests)
- Create: `src/Common/Acheve.Common.Consumption/ConsumptionState.cs`
- Create: `src/Common/Acheve.Common.Consumption/ConsumptionExtensions.cs`
- Modify: `src/Common/Acheve.Common.Shared/Constants.cs` (new `Consumption` class, `Telemetry.ConsumptionMeterName`)
- Modify: `src/Aspire/Acheve.ServiceDefaults/Extensions.cs` (`.AddMeter("Acheve.Consumption")` with the same comment style)
- Modify: `Estimations.slnx` (library under `/src/Common/`, tests under `/test/Common/`)
- Test: `test/Common/Acheve.Common.Consumption.Tests/ConsumptionStateTests.cs` (csproj copied from `Acheve.Common.Resilience.Tests`)

**Interfaces:**
- Produces:
  - `Constants.Consumption.Paused = "paused"`, `Running = "running"`, `States = [Paused, Running]`, `Workers = [ImageDownloads, ExternalImageProcessor, EstimationProcessor, CallbackNotifier, ProcessManager, StateHolder]` (the `Constants.Resources` names).
  - `Constants.Telemetry.ConsumptionMeterName = "Acheve.Consumption"`.
  - `public sealed partial class ConsumptionState(Func<IWorkersApi> workers, IMeterFactory meterFactory, IHostEnvironment environment, ILogger<ConsumptionState> logger)`: `bool IsPaused`, `Task PauseAsync()`, `Task ResumeAsync()`. The `Func` defers resolving `IBus` until first use.
  - `public static TBuilder AddConsumption<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder` registers the state (workers from `sp.GetRequiredService<IBus>().Advanced.Workers`).
  - `public static WebApplication UseConsumption(this WebApplication app)` creates the state and, in Development, maps the endpoints. GET returns `ConsumptionStatus(string State, int Workers)`; POSTs return the status.

- [ ] **Step 1: Write the failing tests** with a fake `IWorkersApi` (Count settable, records `SetNumberOfWorkers` calls):
  - `Pause_sets_the_workers_to_zero_and_resume_restores_the_count`: Count 3 → pause → `SetNumberOfWorkers(0)`, `IsPaused` true → resume → `SetNumberOfWorkers(3)`, `IsPaused` false.
  - `Pausing_twice_keeps_the_original_count`: Count 2 → pause, pause → resume → last call is `SetNumberOfWorkers(2)`; only one `SetNumberOfWorkers(0)` call.
  - `Resume_while_running_does_nothing`: no calls.
  - `Reports_paused`: `MetricCollector<int>` on `Acheve.Consumption`/`consumption.paused`; after pause the measurement is 1 with tag `messaging.consumer` = `environment.ApplicationName`.
- [ ] **Step 2: Run** `dotnet test test/Common/Acheve.Common.Consumption.Tests` — expected: compile failure (types missing).
- [ ] **Step 3: Implement** `ConsumptionState`: serialize pause/resume with a `SemaphoreSlim`; run the blocking `SetNumberOfWorkers` on `Task.Run`; remember the count at pause; `[LoggerMessage(Warning, "Message consumption paused ({workers} workers stopped)")]` and `"... resumed ({workers} workers)"` only on real changes. Then `ConsumptionExtensions` mirroring `ResilienceExtensions`. Add the meter to ServiceDefaults and the constants.
- [ ] **Step 4: Run** the tests — expected: PASS. `dotnet build Estimations.slnx` — expected: no warnings added.
- [ ] **Step 5: Commit** `Consumption: pause and resume a Rebus worker at run time`.

### Task 2: Workers and AppHost

**Files:**
- Modify: `Program.cs` of ImageDownloads, ExternalImageProcessor, EstimationProcessor, CallbackNotifier (`builder.AddConsumption()`, `app.UseConsumption()`), and their csproj (project ref).
- Modify: `src/Application/Acheve.Application.ProcessManager/Program.cs` and csproj: switch from `Host.CreateApplicationBuilder` / `Microsoft.NET.Sdk.Worker` to `WebApplication.CreateBuilder` / `Microsoft.NET.Sdk.Web`, `app.MapDefaultEndpoints()`, `app.UseConsumption()`; add an `http`/`https` profile to its `launchSettings.json` like ImageDownloads'.
- Modify: `src/Application/Acheve.Application.StateHolder/Program.cs`, csproj, and `appsettings.json` (`Protocols` → `Http1AndHttp2`, so HTTP/1.1 commands reach `/consumption`; gRPC keeps working over TLS).
- Create: `src/Aspire/Shared/ConsumptionCommands.cs`: `WithConsumptionCommands()` adds **Consumption: pause** (`/consumption/pause`, icon `Pause`) and **Consumption: resume** (`/consumption/resume`, icon `Play`).
- Modify: `src/Aspire/Acheve.AppHost/AppHost.cs`: `.WithHttpHealthCheck("/health").WithConsumptionCommands()` on all six workers; the chaos monkey gets `.WithReference(...)` to processmanager and stateholder too (keep the processmanager and stateholder builders in variables).

**Interfaces:**
- Consumes: `AddConsumption()`, `UseConsumption()`, `Constants.Consumption` from Task 1.
- Produces: `POST https+http://{worker}/consumption/{pause|resume}` reachable through service discovery for all six workers.

- [ ] **Step 1: Make the changes above.**
- [ ] **Step 2: Run** `dotnet build Estimations.slnx` and `dotnet test` — expected: PASS (ProcessManager and StateHolder tests still green).
- [ ] **Step 3: Run the app** (the `run` skill / `aspire run`) and, with the traffic generator on, pause `imagedownloads` from its dashboard command. Expected: `rabbitmq` queue `imagedownloads` ready count grows; `GET /consumption` says `paused`; resume drains it. Note whether the queue still shows a consumer while paused (needed for the docs, Task 5). Check every worker shows healthy and answers `GET /consumption`; check StateHolder gRPC calls still succeed (the Api reads case state).
- [ ] **Step 4: Commit** `Workers and AppHost: consumption commands on every Rebus worker`.

### Task 3: Chaos monkey switch kind

**Files:**
- Create: `src/Tools/Acheve.Tools.ChaosMonkey/ConsumptionTargets.cs` (`Names` = `Constants.Consumption.Workers`, `Supports(worker, state)`).
- Modify: `ChaosMonkeyOptions.cs`: `ChaosStepOptions.Consumption` (`Dictionary<string, string>`, doc "consumption state per worker; the workers not named go back to running"); validator errors `unknown consumption target '{worker}'. Use one of: …` and `{worker} doesn't support '{state}'. Use one of: paused, running.`
- Modify: `ChaosPlan.cs`: `ChaosStep` gets `IReadOnlyDictionary<string, string> Consumption` and `StateFor(worker)` (default `running`); `ToString` includes it; `Steps` passes it; `RandomSteps` passes empty.
- Modify: `ChaosClient.cs`: `Task<bool> SetConsumptionAsync(string worker, string state, CancellationToken)` posting to `https+http://{worker}/consumption/{pause|resume}`.
- Modify: `ChaosMonkeyMetrics.cs`: `ConsumptionKind = "consumption"`.
- Modify: `StrategyRunner.cs`: `ApplyAsync` also switches every `ConsumptionTargets.Names` worker; reset log text "… every caller to standard and every worker running".
- Modify: `appsettings.json`/`launchSettings` only if needed for service discovery of the two new references.
- Test: `ChaosPlanTests.cs`, `ChaosMonkeyOptionsValidatorTests.cs`, `ChaosClientTests.cs`, `StrategyRunnerTests.cs`, `FakeChaosClient.cs` (add `Consumption` kind).

**Interfaces:**
- Consumes: `Constants.Consumption` (Task 1); the endpoints (Task 2).
- Produces: step setting `"Consumption": { "imagedownloads": "paused" }` usable in appsettings.

- [ ] **Step 1: Write the failing tests:**
  - `ChaosPlanTests.Unnamed_workers_are_running`: a step with `Consumption = { imagedownloads: paused }` → `StateFor("imagedownloads") == "paused"`, `StateFor("processmanager") == "running"`.
  - `ChaosPlanTests.Random_steps_never_pause`: 20 random steps, every `Consumption` empty.
  - Validator: unknown worker `api` and state `stopped` each produce one error with the texts above.
  - `ChaosClientTests`: `SetConsumptionAsync("imagedownloads", "paused")` posts to `/consumption/pause`; `"running"` to `/consumption/resume` (follow the existing handler-capture style).
  - `StrategyRunnerTests.Runs_each_step_switching_chaos_and_resilience_then_resets`: extend so the step pauses `imagedownloads`; assert the six `consumption` calls per step and, after the run, `imagedownloads` → `running` in the reset; the `chaos.monkey.switches` measurement has `chaos.kind = consumption`.
  - `StrategyRunnerTests.Stop_during_a_pause_resumes_the_worker`: cancel in a paused step → last consumption call for `imagedownloads` is `running`.
- [ ] **Step 2: Run** `dotnet test test/Tools/Acheve.Tools.ChaosMonkey.Tests` — expected: FAIL (compile).
- [ ] **Step 3: Implement** the changes above.
- [ ] **Step 4: Run** the tests — expected: PASS.
- [ ] **Step 5: Commit** `Chaos monkey: consumption switches in strategy steps`.

### Task 4: The `backlog` strategy

**Files:**
- Modify: `Constants.cs`: `ChaosMonkey.Backlog = "backlog"`, added to `Strategies` (before `BadDay`).
- Modify: `src/Aspire/Shared/ChaosMonkeyCommands.cs`: `"The image downloads stop consuming, first for 1 minute (only latency) and then for 3 (the saga gives up: failed cases)."`, icon `Pause`.
- Modify: `src/Tools/Acheve.Tools.ChaosMonkey/appsettings.json`: the five steps from Global Constraints; names: `Baseline`, `Image downloads paused 1 min (only latency)`, `Drain`, `Image downloads paused 3 min (past the saga's 2 min wait: failed cases)`, `Recovery`.
- Test: `ChaosMonkeyOptionsValidatorTests` (or the existing test that binds the real appsettings, if any) — the shipped settings validate.

- [ ] **Step 1: Add the strategy** and run `dotnet test test/Tools/Acheve.Tools.ChaosMonkey.Tests` — expected: PASS (the validator requires every constant to be configured, so a missing piece fails).
- [ ] **Step 2: Commit** `Chaos monkey: backlog strategy`.

### Task 5: Dashboard, docs and a live run

**Files:**
- Modify: `src/Aspire/Acheve.AppHost/grafana/dashboards/chaos-run.json`: per worker, the consumption state next to the chaos scenario and resilience profile (from `consumption_paused`); a "Messages waiting per queue" panel (`sum by (queue) (rabbitmq_detailed_queue_messages_ready{queue!="error"})`) beside client latency; consumption state changes as annotations like the existing ones. Edit with Edit/Write only.
- Modify: `docs/chaos.md`: a "Pausing a worker" section (dashboard commands, HTTP endpoints, metric, whether "Queues without consumers" lights up — from Task 2's observation), the `backlog` row in the strategies table (what to look at: the `imagedownloads` queue and **Client case duration** in A; `download_timeout` in **Saga outcomes** and **Client success rate** in B; the late downloads after the resume), `Consumption` in the step settings and the target list, and the manual "stop the resource in Aspire" experiment (a crash, not a stall).
- Modify: `README.md` only if it lists the strategies.

- [ ] **Step 1: Make the changes.**
- [ ] **Step 2: Live run:** app + traffic generator, start `backlog`. Expected: in A the queue grows and p95 rises with no failures; in B `download_timeout` appears after about 2 minutes; at the end `imagedownloads` is `running`. Pause a worker mid-handler once and check the RabbitMQ unacked count returns to 0.
- [ ] **Step 3: Commit** `Docs and Chaos run dashboard: message consumption chaos`.
