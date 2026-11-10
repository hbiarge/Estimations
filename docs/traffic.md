# Traffic generator

[Acheve.Tools.TrafficGenerator](../src/Tools/Acheve.Tools.TrafficGenerator) plays many clients. It asks for estimations over time and follows each case to its end, so you can watch how the system behaves under load and under [chaos](chaos.md) as a client sees it.

It runs in the application's AppHost but doesn't start with it: press **Start** on `trafficgenerator` in the Aspire dashboard, and **Stop** to end it. Cases still running when it stops are dropped, not counted.

## What it does

- Gets a token from the Duende demo identity server (the one the Api trusts) and reuses it until it expires.
- Starts cases at random times (a Poisson process) around the configured rate. Each case has 1 to `MaxImagesPerCase` images from the fake images system; a share of them (`BrokenImageRate`) gets one image that doesn't exist, to exercise the download failure path.
- Uses the fake notifications system as the clients' callback endpoint.
- Polls `GET /estimations/{ticket}` every `PollInterval` until the case ends or `CaseTimeout` passes.

## Settings

In the `Traffic` section of its [appsettings.json](../src/Tools/Acheve.Tools.TrafficGenerator/appsettings.json) (or environment variables such as `Traffic__CasesPerMinute`):

| Setting | Default | Meaning |
| --- | --- | --- |
| `CasesPerMinute` | 6 | Average new cases per minute. |
| `Pattern` | `Steady` | `Steady`; `Wave` (the rate swings between 50% and 150% over `WavePeriod`); `Bursts` (`BurstSize` extra cases every `BurstEvery`). |
| `MaxImagesPerCase` | 3 | |
| `BrokenImageRate` | 0.05 | Share of cases with one missing image. |
| `PollInterval` | 1 s | |
| `CaseTimeout` | 10 min | |
| `MaxCasesInFlight` | 300 | Above it, new cases are skipped (and counted). |

The AppHost sets the callback and image URLs from the external systems' URLs.

The fake external systems run up to 20 jobs at a time (`BackgroundQueue:MaxConcurrency`), and each image analysis or estimation takes 10 s, so each fake does about 120 jobs a minute. A case has 2 images on average, so above about 60 cases per minute the image analysis queue grows and every case takes longer than the one before. That is load you can choose to apply, but it is the fakes' limit, not the application's.

## Outcomes

The Api returns only the furthest state a case has reached, and a failed case still ends in `ClientNotified` once the client is told about the failure. So the generator decides the outcome from the states it saw while polling:

| Outcome | When |
| --- | --- |
| `succeeded` | It saw `EstimationReady`, then `ClientNotified`. |
| `failed` | It saw `EstimationError`, then `ClientNotified`: the client was told the case failed. |
| `notification_failed` | `ClientNotificationError`: the client couldn't be notified. |
| `unknown` | `ClientNotified`, but the polls missed both `EstimationReady` and `EstimationError` (they last about 2 s). |
| `timed_out` | No final state within `CaseTimeout`. |
| `rejected` | The Api didn't accept the case. |

## Metrics

The **Estimations / Chaos run** Grafana dashboard shows them next to the chaos scenarios (see [chaos.md](chaos.md#the-chaos-run-dashboard)).

Meter `Acheve.Traffic`, with `service_name="trafficgenerator"` and `service_namespace="tools"`:

- `traffic_cases_started_total`
- `traffic_cases_finished_total` by `traffic_outcome`
- `traffic_case_duration_seconds` (histogram) by `traffic_outcome`: from the Api accepting the case to the poll that saw it end
- `traffic_cases_in_flight`
- `traffic_cases_skipped_total`
- `traffic_rate_target`: cases per minute it aims for right now

For example, the success rate over 5 minutes:

```promql
sum(rate(traffic_cases_finished_total{traffic_outcome="succeeded"}[5m]))
  / sum(rate(traffic_cases_finished_total[5m]))
```

Each case also logs its outcome, duration and the states seen.

The generator's requests carry its trace context, so every generated case's trace starts at `trafficgenerator`. The polls add one short trace per request.
