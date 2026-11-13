# Resilience

The four services that call the external systems (`imagedownloads`, `imageprocessor`, `estimationprocessor` and `callbacknotifier`) protect their HttpClient with Polly. The strategies they use form a **profile**, and the profile can be switched at run time, without a restart, from the Aspire dashboard or over HTTP. Put it next to a [chaos scenario](chaos.md) on the same external system and you can see what each strategy buys you, and what it costs. Switching only works in Development.

The code is in [src/Common/Acheve.Common.Resilience](../src/Common/Acheve.Common.Resilience). `AddSwitchableResilienceHandler()` takes the place of `AddStandardResilienceHandler()`. Its pipeline is rebuilt when the profile changes, and requests already in flight finish on the old one. The pipeline is kept between requests while the profile stays the same, so the circuit breaker remembers its state.

## Profiles

| Profile | Strategies | What to expect |
| --- | --- | --- |
| `standard` | Concurrency limit 1000, total timeout 30 s, 3 retries (exponential from 2 s, with jitter), circuit breaker (10% failures over at least 100 calls in 30 s, open for 5 s), attempt timeout 10 s | The default: the same as `AddStandardResilienceHandler()`. |
| `none` | Nothing | Every fault and every slow answer reaches the handler, which reports `UnableTo…` straight away. |
| `retry-only` | 3 retries (exponential from 2 s, with jitter) | Hides flaky faults. Against an outage every call becomes four, and nothing limits how long a call takes. |
| `fail-fast` | Total timeout 5 s, circuit breaker (50% failures over at least 5 calls in 30 s, open for 30 s), attempt timeout 3 s | No retries. A system that is down is quickly left alone: calls fail at once while the circuit is open. |
| `hedging` | Total timeout 30 s, up to 2 more attempts, each one sent when the previous one is 1 s late or fails, attempt timeout 10 s | The first good answer wins. Cuts the slow tail, at the cost of extra calls. |

Retries and hedging send the same request more than once. That is safe here because every POST to an external system carries an `Idempotency-Key` and the external systems answer a repeated key without doing the work again.

## Switching a profile

- **Aspire dashboard**: open a resource's actions (**…**) → **Commands** → **Resilience: \<profile\>**.
- **HTTP**, on each of the four services:
  - `GET /resilience`: the settings in force and the profile names.
  - `POST /resilience/profiles/{name}`: switch to a named profile.
  - `PUT /resilience`: custom settings (profile `custom`). Leave a strategy out to turn it off. For example, a bulkhead of two calls with retries:

    ```json
    { "concurrencyLimit": 2, "retry": { "maxRetryAttempts": 2, "baseDelay": "00:00:01" } }
    ```

    The other settings are `totalTimeout`, `attemptTimeout`, `hedging` (`maxHedgedAttempts`, `delay`) and `circuitBreaker` (`failureRatio`, `minimumThroughput`, `samplingDuration`, `breakDuration`). Retry and hedging can't be used together, and settings Polly refuses (for example a circuit breaker with `minimumThroughput` under 2) are answered with 400.
- **At start**: set `Resilience:Profile` (for example the environment variable `Resilience__Profile=none`).

## Pairings worth showing

Run the [traffic generator](traffic.md) and switch both the chaos scenario of an external system and the profile of the service that calls it:

| External system | Caller |
| --- | --- |
| `external-images` | `imagedownloads` |
| `external-imageprocess` | `imageprocessor` |
| `external-estimations` | `estimationprocessor` |
| `external-notifications` | `callbacknotifier` |

| Chaos | Switch the profile | What you see | Strategy |
| --- | --- | --- | --- |
| `flaky` | `none` → `standard` | Failed cases collapse; the retries show up as events and as extra calls. | `retries` |
| `flaky` | `standard` → `hedging` | The 2 s answers stop setting the pace: hedged attempts win and case duration drops. | `hedging` |
| `down` | `retry-only` → `fail-fast` | First every call becomes four, with long waits. Then the circuit opens, calls fail at once and the external system gets almost no requests. | `retry-storm` |
| `slow` | `standard` → `fail-fast` | The 5 s answers go over the 3 s attempt timeout: cases fail instead of waiting. | `timeouts` |
| `healthy` | `PUT /resilience` with `concurrencyLimit: 1` | Under load, calls over the limit are rejected (`OnRateLimiterRejected`) to protect the external system. | |

The chaos monkey runs the first four pairings for you, each as a strategy of about 5 minutes: start one from `chaosmonkey`'s **Commands** in the Aspire dashboard (**Strategy: \<name\>**). See [chaos.md](chaos.md#chaos-monkey).

## Seeing it

- **Metrics**: `resilience_profile_active` (meter `Acheve.Resilience`) is 1 for the profile in force and 0 for the others. Polly's meter (`Polly`) counts every strategy event in `resilience_polly_strategy_events_total` by `event_name` (`OnRetry`, `OnHedging`, `OnTimeout`, `OnCircuitOpened`, `OnCircuitClosed`, `OnRateLimiterRejected`…), `pipeline_name` (the HttpClient) and `pipeline_instance` (the profile). With the `none` profile the pipeline is empty and Polly reports nothing.
- **Logs**: a warning when the profile really changes, and Polly's own line for each event.
- **The Chaos run dashboard** in Grafana has a **Resilience** row: the profile of each service over time, the resilience events, and the calls the services really sent to the external systems. Every profile change is marked on all the charts, next to the chaos changes.
