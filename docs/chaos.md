# Chaos

Every fake external system, and the Api's callback endpoints, can misbehave on demand: fail requests, answer slowly, or (for the two systems that call back) lose, duplicate or delay their callbacks. A scenario is switched at run time, without a restart, from the Aspire dashboard or over HTTP. It only works in Development.

The code is in [src/Common/Acheve.Common.Chaos](../src/Common/Acheve.Common.Chaos). Faults and latency use Polly's chaos strategies (Simmy) in a middleware; the callback misbehaviour is decided by `ChaosState.PlanCallback()` in the fakes that call back.

## Scenarios

| Scenario | What happens | What to expect |
| --- | --- | --- |
| `healthy` | Nothing is injected (the default). | |
| `flaky` | 30% of requests fail with 503 and 20% wait 2 s first. | The callers' HTTP retries hide most of it: more retries and latency, few failed cases. |
| `slow` | Every request waits 5 s; callbacks come 20 s later than usual. | Slower cases and `StillAwaiting…` checks in the saga, but no failures. |
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

## Seeing it

- **Metrics** (meter `Acheve.Chaos`): `chaos_injections_total` by `chaos_kind` (`fault`, `latency`, `callback_drop`, `callback_duplicate`, `callback_delay`) and `chaos_scenario`; `chaos_scenario_active` is 1 for the scenario in force and 0 for the others.
- **Traces**: the request span gets a `chaos.injected` tag with the kind.
- **Logs**: a warning when the scenario changes and a line for each injection, with the scenario.

To see a scenario's effect on many cases, run the [traffic generator](traffic.md) while it is on.
