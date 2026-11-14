# Traffic generator

[Acheve.Tools.TrafficGenerator](../src/Tools/Acheve.Tools.TrafficGenerator) plays many clients at once.

## Why it exists

One case shows that the flow works. You need many cases to see rates, percentiles, backlogs and alerts. The generator keeps a steady stream of cases going and **follows each one to its end as a client would**, so it measures what clients really get: success rate and duration from the outside. The saga's own metrics measure the same cases from the inside.

Run it during every fault experiment ([runtime-faults.md](runtime-faults.md), [chaos-scenarios.md](chaos-scenarios.md)): without traffic, a fault changes nothing you can see.

## Running it

It runs in the application's AppHost but doesn't start with it: press **Start** on `trafficgenerator` in the Aspire dashboard, and **Stop** to end it. Cases still running when it stops are dropped, not counted.

For each case it:

1. Gets a token from the Duende demo identity server (the one the Api trusts), reused until it expires.
2. Starts the case at a random time around the configured rate (a Poisson process), with 1 to `MaxImagesPerCase` images from the fake images system. A share of cases (`BrokenImageRate`) gets one image that doesn't exist, to exercise the download failure path.
3. Uses the fake notifications system as the callback URL.
4. Polls `GET /estimations/{ticket}` every `PollInterval` until the case ends or `CaseTimeout` passes.

## Settings

In the `Traffic` section of its [appsettings.json](../src/Tools/Acheve.Tools.TrafficGenerator/appsettings.json), or environment variables such as `Traffic__CasesPerMinute`:

| Setting | Shipped value | Meaning |
| --- | --- | --- |
| `CasesPerMinute` | 20 | Average new cases per minute (6 when not set). |
| `Pattern` | `Steady` | `Steady`; `Wave` (the rate swings between 50% and 150% over `WavePeriod`); `Bursts` (`BurstSize` extra cases every `BurstEvery`). |
| `WavePeriod` | 10 min | Length of one wave. |
| `BurstEvery` | 5 min | Time between bursts. |
| `BurstSize` | 20 | Extra cases in each burst. |
| `MaxImagesPerCase` | 3 | |
| `BrokenImageRate` | 0.05 | Share of cases with one missing image. |
| `PollInterval` | 1 s | |
| `CaseTimeout` | 15 min | Longer than the worst case (about 11 min). |
| `MaxCasesInFlight` | 300 | Above it, new cases are skipped (and counted). |

The AppHost sets the callback and image URLs from the external systems' URLs.

**How much load.** Each fake external system does about 120 jobs a minute ([system.md](system.md#the-workers-limits)). A case has 2 images on average, so above about 60 cases per minute the image analysis queue grows and every case takes longer than the one before. That is the fakes' limit, not the application's.

## Outcomes

A failed case still ends in `ClientNotified` once the client is told, so the generator decides the outcome from the last state **and** result it polled:

| Outcome | When |
| --- | --- |
| `succeeded` | `ClientNotified` with `Result = Success`. |
| `failed` | `ClientNotified` with `Result = Failure`: the client was told the case failed. |
| `notification_failed` | `ClientNotificationError`: the client couldn't be notified. |
| `unknown` | `ClientNotified` without a result (not expected). |
| `timed_out` | No final state within `CaseTimeout`. |
| `rejected` | The Api didn't accept the case. |

## Metrics

Meter `Acheve.Traffic`, with `service_namespace="tools"`. The **Chaos run** dashboard shows them next to the faults.

| Metric | Meaning |
| --- | --- |
| `traffic_cases_started_total` | Cases started |
| `traffic_cases_finished_total` | Cases finished, by `traffic_outcome` |
| `traffic_case_duration_seconds` | From the Api accepting the case to the poll that saw it end, by `traffic_outcome` |
| `traffic_cases_in_flight` | Cases being followed |
| `traffic_cases_skipped_total` | Cases not started because of `MaxCasesInFlight` |
| `traffic_rate_target_per_min` | The rate it aims for right now |

The success rate over 5 minutes:

```promql
sum(rate(traffic_cases_finished_total{traffic_outcome="succeeded"}[5m]))
  / sum(rate(traffic_cases_finished_total[5m]))
```

Each case also logs its outcome, duration and the states seen. Its trace starts at `trafficgenerator`; each poll adds one short trace.
