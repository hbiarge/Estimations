# Chaos

Every fake external system, and the Api's callback endpoints, can misbehave on demand: fail requests, answer slowly, or (for the two systems that call back) lose, duplicate or delay their callbacks. A scenario is switched at run time, without a restart, from the Aspire dashboard or over HTTP. It only works in Development.

The code is in [src/Common/Acheve.Common.Chaos](../src/Common/Acheve.Common.Chaos). Faults and latency use Polly's chaos strategies (Simmy) in a middleware; the callback misbehaviour is decided by `ChaosState.PlanCallback()` in the fakes that call back.

## Scenarios

| Scenario | What happens | What to expect |
| --- | --- | --- |
| `healthy` | Nothing is injected (the default). | |
| `flaky` | 30% of requests fail with 503 and 20% wait 2 s first. | The callers' HTTP retries hide most of it: more retries and latency, few failed cases. |
| `slow` | Every request waits 5 s; callbacks come 20 s later than usual. | Slower cases and `…Timeout` checks in the saga, but no failures. |
| `down` | Every request fails with 503. | Retries run out: `UnableTo…` messages, failed images or estimations, failed client notifications. |
| `lost-callbacks` | Half of the accepted work never calls back. | The saga's wait timeouts (about 120 s) give up on those images or estimations. |
| `duplicate-callbacks` | Half of the callbacks are sent twice. | The saga ignores the second one and logs a warning; cases complete normally. |

`lost-callbacks` and `duplicate-callbacks` only apply to `external-imageprocess` and `external-estimations`. On the Api, the chaos only applies to `/callbacks` (the `Chaos:Paths` setting), so clients can still start cases and read their state. Health checks and `/chaos` itself are never broken.

## Switching a scenario

- **Aspire dashboard**: open a resource's actions (**…**) → **Commands** → **Chaos: \<scenario\>**. The external systems have them in the external AppHost's dashboard, and the Api in the application's.
- **HTTP**, on each service:
  - `GET /chaos`: the settings in force and the scenario names.
  - `POST /chaos/scenarios/{name}`: switch to a named scenario.
  - `PUT /chaos`: custom settings (scenario `custom`), for example `{"faultRate": 0.5, "faultStatusCode": 500}` or `{"callbackDropRate": 1}`. Rates go from 0 to 1.
- **At start**: set `Chaos:Scenario` (for example the environment variable `Chaos__Scenario=flaky`).

## Pausing a worker

The chaos above breaks HTTP. A worker can also stop **consuming**: it takes no more messages from its queue while the process and its health checks stay up, so its queue grows and the cases waiting on it slow down. On resume it drains the backlog. Every service with a Rebus worker supports it: `imagedownloads`, `imageprocessor`, `estimationprocessor`, `callbacknotifier`, `processmanager` and `stateholder` (the Api only sends). The code is in [src/Common/Acheve.Common.Consumption](../src/Common/Acheve.Common.Consumption). It only works in Development.

- **Aspire dashboard**: open a worker's actions (**…**) → **Commands** → **Consumption: pause** or **Consumption: resume**.
- **HTTP**, on each worker: `GET /consumption` (the state, `running` or `paused`), `POST /consumption/pause`, `POST /consumption/resume`.

A pause sets Rebus's workers to 0 and waits for the messages being handled to finish; resume puts the workers back. A pause that arrives while the worker is still starting, before Rebus has started its workers, is refused (409) and changes nothing: send it again a moment later. What to expect while paused:

- **Messages waiting per queue** grows: the backlog waits (ready) in RabbitMQ.
- **Messages being handled per queue** shows up to 5 for the paused worker: the messages RabbitMQ had already handed it (the workers prefetch 5, `Constants.Queues.Prefetch`). They are handled on resume.
- **Queues without consumers** stays at 0: the worker is still connected, it just doesn't take messages. To see that panel light up, stop the resource from the Aspire dashboard instead; that is a crash rather than a stall, and the health check goes red too.
- Whether a pause costs only latency or failed cases depends on how long it lasts against the saga's wait limits: the download waits 2 minutes, the image analysis and the estimation about 100 s, the client notification 45 s per attempt. The clock starts when the saga sends the command, so the time the message waits in the paused queue counts. Pausing the `processmanager` itself delays its own timeouts too, so it only adds latency.

It reports `consumption_paused` (meter `Acheve.Consumption`; 1 while paused, by `service_name`) and a warning log line when it pauses or resumes.

## Chaos monkey

[Acheve.Tools.ChaosMonkey](../src/Tools/Acheve.Tools.ChaosMonkey) runs **strategies**: short scripted stories that each show one thing about the system, such as what a resilience strategy buys, what it costs, or what it can't see. Each step of a strategy sets the chaos scenarios of the Api and the external systems **and** the [resilience profiles](resilience.md) of the services that call them. Typically the chaos stays the same while the profile changes halfway, so the [Chaos run dashboard](#the-chaos-run-dashboard) shows a before and an after.

The monkey starts with the application's AppHost and waits. Run the [traffic generator](traffic.md) first, then start a strategy:

- **Aspire dashboard**: open `chaosmonkey`'s actions (**…**) → **Commands** → **Strategy: \<name\>**. **Strategy: next step** ends the current step now, and **Strategy: stop** stops the strategy.
- **HTTP**, on the monkey (only in Development):
  - `GET /strategies`: the strategy names and the one running, with its current step.
  - `POST /strategies/{name}`: start it.
  - `POST /strategies/next`: go on to the next step now (after the last step, the strategy finishes).
  - `POST /strategies/stop`: stop it.

Only one strategy runs at a time: starting one stops the one running. When a strategy finishes, is stopped or is replaced, and when the monkey stops, every target goes back to `healthy`, every caller to `standard` and every worker to `running`.

### Strategies

The learning strategies take about 5 minutes: a 30 s healthy baseline, a phase without and a phase with the strategy (2 minutes each, long enough for the dashboard's rates), and a 1 minute recovery. The panels named below are on the Chaos run dashboard.

| Strategy | What breaks | What changes | What to look at |
| --- | --- | --- | --- |
| `retries` | `external-imageprocess` flaky | `imageprocessor`: `none`, then `standard` | **Client success rate** drops without resilience and recovers with it; the retries appear in **Resilience events** and **Calls to external systems**. |
| `retry-storm` | `external-images` down | `imagedownloads`: `retry-only`, then `fail-fast` | **Calls to external systems** multiplies with retries only (every call becomes four), then falls to almost nothing once the circuit opens (`OnCircuitOpened` in **Resilience events**). |
| `hedging` | `external-estimations` flaky | `estimationprocessor`: `standard`, then `hedging` | The p95 in **Client case duration** drops with hedging; `OnHedging` events and extra calls are the price. |
| `timeouts` | `external-estimations` slow | `estimationprocessor`: `standard`, then `fail-fast` | Resilience that hurts: slow but successful cases become failures in **Saga outcomes** and **Client success rate** (`OnTimeout` events). |
| `callbacks` | `external-imageprocess` loses callbacks (3 min), then `external-estimations` duplicates them | Nothing: HTTP resilience can't see callbacks | **Saga outcomes**: the saga's wait timeouts give up on lost image analyses; duplicate estimations are ignored and cases complete. |
| `backlog` | `imagedownloads` stops consuming for 1 minute, then (after 90 s to drain) for 3 minutes; about 8 minutes | Nothing | First a backlog that only costs latency: the `imagedownloads` queue in **Messages waiting per queue** grows and **Client case duration** rises, then drains with no failed cases. Then the same pause held past the saga's 2 minute download wait: `download_timeout` in **Saga outcomes**, **Client success rate** drops, and the paused service itself shows no errors. After the resume it still downloads the images of cases the saga has given up on. |
| `bad-day` | Flaky image analysis, slow estimations, client endpoint down and flaky Api callbacks, for 4 minutes | Nothing | Several things wrong at once: can you tell them apart on the dashboards? |
| `tour` | The chaos scenarios one after the other, about 18 minutes, over and over | Nothing | A long background run. |
| `random` | A random scenario on 1 to `MaxTargets` random targets for a random time between `MinDuration` and `MaxDuration`, then `HealthyGap` with everything healthy, endlessly | Nothing | A long background run. |

### Settings

The strategies are in the `ChaosMonkey:Strategies` section of the monkey's [appsettings.json](../src/Tools/Acheve.Tools.ChaosMonkey/appsettings.json). A strategy has either `Steps` (in order, repeated when `Repeat` is on) or `Random`. A step sets `Chaos` (scenario per target), `Resilience` (profile per caller) and `Consumption` (`paused` or `running` per worker) for `Duration`:

```json
{
  "Name": "Images down, retries only (every call becomes four)",
  "Duration": "00:02:00",
  "Chaos": { "external-images": "down" },
  "Resilience": { "imagedownloads": "retry-only" }
}
```

The targets a step doesn't name go back to `healthy`, the callers it doesn't name go back to `standard`, and the workers it doesn't name go back to `running`. `random` never pauses a worker.

- **Chaos targets:** `api`, `external-images`, `external-imageprocess`, `external-estimations` and `external-notifications`.
- **Callers:** `imagedownloads`, `imageprocessor`, `estimationprocessor` and `callbacknotifier`.
- **Workers:** `imagedownloads`, `imageprocessor`, `estimationprocessor`, `callbacknotifier`, `processmanager` and `stateholder`.

The monkey refuses to start when a step names a target, scenario or profile that doesn't exist (for example `lost-callbacks` on the Api). It also refuses when a strategy has no dashboard command: a new strategy needs its name in `Constants.ChaosMonkey.Strategies` and a description in [ChaosMonkeyCommands.cs](../src/Aspire/Shared/ChaosMonkeyCommands.cs).

### What it reports

- **Logs:** the strategy starting, finishing or stopping, and each step (`Chaos step 2 of retry-storm: Images down, retries only …`).
- **Metrics:** the meter `Acheve.ChaosMonkey` counts:
  - the switches it sends, in `chaos_monkey_switches_total` by `chaos_kind` (`chaos`, `resilience` or `consumption`), `chaos_target`, `chaos_value` and `chaos_result`;
  - the steps, in `chaos_monkey_steps_total` by `chaos_strategy`.
- **Where to see what each service is really in:** the services themselves report it, in `chaos_scenario_active`, `resilience_profile_active` and `consumption_paused`.
- **Unreachable services:** a target, caller or worker that can't be reached (for example when the external systems' AppHost isn't running) is logged and counted as failed, and the step goes on.

## Seeing it

- **Metrics** (meter `Acheve.Chaos`): `chaos_injections_total` by `chaos_kind` (`fault`, `latency`, `callback_drop`, `callback_duplicate`, `callback_delay`) and `chaos_scenario`; `chaos_scenario_active` is 1 for the scenario in force and 0 for the others.
- **Traces**: the request span gets a `chaos.injected` tag with the kind.
- **Logs**: a warning when the scenario really changes (switching to the scenario already in force logs nothing) and a line for each injection, with the scenario.

To see a scenario's effect on many cases, run the [traffic generator](traffic.md) while it is on.

### The Chaos run dashboard

The Grafana dashboard **Estimations / Chaos run** puts it all together: one row per service with the scenario it was in, the resilience profile of each caller, whether each worker was consuming next to the messages waiting and being handled in each queue, every scenario, profile and consumption change and every chaos monkey step marked on all the charts, the chaos monkey's steps, and next to them what clients saw (the traffic generator's outcomes, success rate, duration and load) and what the saga saw.

![Chaos run dashboard during a chaos monkey run](images/chaos-run-dashboard.jpg)

In this run the image analysis was `flaky` from 15:55 to 15:58: about a quarter of the calls to it failed, but the retries hid it and every case succeeded. Then the estimations were `slow`, and then the image analysis lost callbacks: cases waited for the saga's timeouts, the p95 duration went up to about 2 minutes and some cases failed.
