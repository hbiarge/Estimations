# Chaos scenarios

The cases we chose to show, each one a chaos monkey strategy. Every case asks one question about the system, breaks one thing to answer it, and usually changes one tolerance setting halfway so you can compare a *before* and an *after*. The switches themselves (chaos, resilience, consumption) are explained in [runtime-faults.md](runtime-faults.md); the waits the saga uses are in [system.md](system.md#timeouts-and-limits).

## How to run a case

1. Start both AppHosts.
2. Press **Start** on `trafficgenerator` (20 cases a minute) and let it run for a minute, so there is a healthy baseline.
3. Open **Estimations / Chaos run** in Grafana.
4. Start the case from `chaosmonkey`'s **Commands** → **Strategy: \<name\>**. Every step is marked on every chart.

The panels named below are on the **Chaos run** dashboard unless another dashboard is named. Most alerts wait 5 minutes before firing, longer than one step, so the cases rarely fire them; the ones that do are listed. To see an alert fire, hold the same chaos by hand for about 10 minutes.

Most cases share one shape (about 5 minutes):

| Step | Duration |
| --- | --- |
| Baseline, everything healthy | 30 s |
| The fault, **without** the tolerance setting | 2 min |
| The same fault, **with** the tolerance setting | 2 min |
| Recovery, everything healthy | 1 min |

| Case | Question | Breaks | Tolerance switched |
| --- | --- | --- | --- |
| [`retries`](#retries-what-retries-buy) | What do retries buy? | `external-imageprocess` flaky | `imageprocessor`: `none` → `standard` |
| [`retry-storm`](#retry-storm-what-retries-cost) | What do retries cost against an outage? | `external-images` down | `imagedownloads`: `retry-only` → `fail-fast` |
| [`hedging`](#hedging-cutting-the-slow-tail) | Can we cut the slow tail? | `external-estimations` flaky | `estimationprocessor`: `standard` → `hedging` |
| [`timeouts`](#timeouts-resilience-that-hurts) | Can resilience make things worse? | `external-estimations` slow | `estimationprocessor`: `standard` → `fail-fast` |
| [`callbacks`](#callbacks-what-http-resilience-cant-see) | What can't HTTP resilience see? | Lost, then duplicated callbacks | nothing |
| [`backlog`](#backlog-a-stalled-worker) | When does a stalled worker cost failures? | `imagedownloads` stops consuming | nothing |
| [`bad-day`](#bad-day-several-faults-at-once) | Can we tell faults apart? | Four things at once | nothing |
| [`incident`](#incident-an-alert-for-the-on-call-agent) | Can an agent find the cause? | `external-estimations` slow | `estimationprocessor`: `standard` → `fail-fast`, 4 minutes later |
| [`tour`, `random`](#tour-and-random-background-runs) | — | Background runs | nothing |

---

## `retries`: what retries buy

**The situation.** The external image service is flaky: 30% of its requests fail with 503, and 20% wait 2 s first. This is the most common real-world fault: a dependency that mostly works.

**What happens.**

1. **No resilience (`none`).** Every 503 reaches `ExternalImageProcessor`, which reports `UnableToAnalyseImage` at once. The saga skips that image and goes on with the others. A case fails only when *all* its images fail (`no_valid_images`): about 30% of one-image cases, 9% of two-image cases, 3% of three-image cases. Most cases succeed, but with `skippedImages`: a worse estimation that no success rate shows.
2. **Standard resilience.** Each failed call is retried up to 3 times. An image now fails only if 4 attempts in a row fail (under 1%). The fault is still there, but clients no longer feel it.

**Expected outcome.** Client success rate drops in the first phase and comes back to almost 100% in the second. Calls to the external system go up by about 40% in the second phase: the retries.

**How to see it.**

- **Client success rate** and **Saga outcomes**: the dip and the recovery; `no_valid_images` appears only in the first phase.
- **Resilience events**: no events in phase 1 (`none` reports nothing), `OnRetry` in phase 2.
- **Calls to external systems**: more calls in phase 2 for the same traffic.
- **Case flow** dashboard, **Image results**: `analysis_error` in phase 1 only.
- **Failed calls to external systems** stays high in *both* phases (about 30% of the attempts): the dependency is still failing, retries only hide it.

## `retry-storm`: what retries cost

**The situation.** The images service is completely down: every request fails with 503. No setting can save these cases. The question is how we fail.

**What happens.**

1. **Retries only (`retry-only`).** Every download is tried 4 times with growing waits (about 2, 4 and 8 s), then reported as `UnableToDownloadImage`. Every call becomes four, all against a service that is already down: that is the retry storm, and in real life it keeps a struggling service down.
2. **Fail fast (`fail-fast`).** No retries. After 5 calls with 50% failures the circuit breaker opens for 30 s: calls fail at once without leaving the process. Only one probe call gets through per break.

**Expected outcome.** Every case fails in both phases (`no_valid_images`: no image can be downloaded), and the client is told. What changes is the cost: in phase 1 the calls to `external-images` multiply by four and cases take longer to fail; in phase 2 the calls fall to almost nothing and cases fail quickly.

**How to see it.**

- **Calls to external systems**: four times the calls in phase 1, close to zero in phase 2.
- **Resilience events**: `OnRetry` in phase 1; `OnCircuitOpened` and `OnCircuitClosed` in phase 2, about every 30 s.
- **Client success rate** at about 0 in both phases; **Client case duration** shorter in phase 2.
- **Overview**, **Services** panel: `external-images` shows as **Down (chaos)**.

**The lesson.** Resilience doesn't fix a dead dependency. It only picks how you fail: loudly and slowly, or quietly and quickly.

## `hedging`: cutting the slow tail

**The situation.** The external estimation service is flaky: some calls fail, and 20% wait 2 s before answering.

**What happens.**

1. **Standard resilience.** Failures are retried after a backoff (from 2 s), and slow calls are simply waited for. The estimation call's slow tail sets the pace for those cases.
2. **Hedging.** When an attempt is 1 s late or fails, a second one is sent in parallel (up to 2 extra), and the first good answer wins. A slow or failed attempt no longer sets the pace. Sending the same request twice is safe: it carries the same `Idempotency-Key`, and the fake does the work once.

**Expected outcome.** Cases succeed in both phases. In phase 2 the p95 duration drops by a few seconds, at the price of extra calls. The gain is small next to a case's total time (the estimation callback alone takes 10 s): hedging pays off when the call itself is the long part.

**How to see it.**

- **Client case duration**: the p95 line drops in phase 2.
- **Resilience events**: `OnHedging` in phase 2 only.
- **Calls to external systems**: more calls to `external-estimations` in phase 2.
- **Client success rate**: flat, close to 100%.

## `timeouts`: resilience that hurts

**The situation.** The external estimation service is slow but healthy: every request waits 5 s, and the callback comes 20 s later than usual. Nothing actually fails.

**What happens.**

1. **Standard resilience.** The 10 s attempt timeout is longer than the 5 s answer, so every call succeeds. The callback arrives about 30 s after the request, well inside the saga's 2-minute estimation wait. Cases are slower, and that's all.
2. **Fail fast.** The attempt timeout is now 3 s, shorter than the service's normal 5 s answer. Every call times out (`OnTimeout`) and isn't retried, so `EstimationProcessor` reports `UnableToEstimate` and the saga fails the case (`estimation_rejected`).

**Expected outcome.** Phase 1: every case succeeds, about 25 to 30 s slower. Phase 2: almost every case fails, even though the dependency never failed a single request on its own.

**How to see it.**

- **Client case duration** up in phase 1; **Client success rate** falls in phase 2.
- **Saga outcomes**: `estimation_rejected` in phase 2 only.
- **Resilience events**: `OnTimeout` in phase 2.
- **Case flow** dashboard, **Time spent in each state (p95)**: `WaitingForEstimation` grows in phase 1.

**The lesson.** A timeout has to fit the dependency's normal latency. Set it tighter and "slow" becomes "broken".

## `callbacks`: what HTTP resilience can't see

**The situation.** The external services accept the work over HTTP, then answer later through a callback. Two steps, no resilience change:

1. **Lost callbacks (3 min).** `external-imageprocess` accepts every image but never calls back for half of them.
2. **Duplicate callbacks (2 min).** `external-estimations` calls back twice for half of the estimations.

**What happens.**

1. Every HTTP call succeeds, so Polly sees nothing to retry. For a lost image, the saga keeps checking every 20 s and gives up after about 2 minutes (`analysis_timeout`). The case goes on with its other images and fails (`no_valid_images`) only when all of them were lost: half of the one-image cases, a quarter of the two-image cases.
2. The second estimation callback finds the case no longer waiting. The saga logs a warning, ignores it and deletes the attachment it stored. Cases complete normally.

**Expected outcome.** Step 1: cases with a lost image take more than 2 minutes, and some fail. Step 2: no client-visible effect at all. Timeouts from images lost late in step 1 still show at the start of step 2.

**How to see it.**

- **Saga outcomes** and **Image results** on **Case flow**: `analysis_timeout` during step 1.
- **Client case duration**: p95 climbs to about 2 minutes.
- **Edges** dashboard, `external-imageprocess` section: **Our calls vs callbacks** opens a gap and **Callbacks never received** rises. In the `external-estimations` section, callbacks go above calls and **Callbacks ignored** rises.
- **Resilience events** and **Failed calls to external systems** stay quiet: that is the point.
- Alert: **Wait timeouts** fires during step 1.

**The lesson.** HTTP resilience protects a call. Only the saga's own waits protect a conversation, and only the saga's idempotency guards protect against an answer that arrives twice.

## `backlog`: a stalled worker

**The situation.** It's us this time, not them. `imagedownloads` stops taking messages but stays up and healthy: a stalled consumer. Two pauses, about 8 minutes in total:

| Step | Duration |
| --- | --- |
| Baseline | 30 s |
| `imagedownloads` paused | 1 min |
| Drain | 90 s |
| `imagedownloads` paused | 3 min |
| Recovery | 2 min |

**What happens.**

1. **1-minute pause.** `DownloadImage` commands pile up in the queue. The saga's 2-minute download wait started when it sent them, and no message waits that long. On resume the worker drains the queue and every case finishes, only later.
2. **3-minute pause.** Commands sent in the first minute of the pause wait more than 2 minutes. The saga gives up on them (`download_timeout`). A case sends all its downloads at once, so all its images time out together and the case fails (`no_valid_images`). Cases that started later in the pause make it. After the resume the worker still downloads the images of the cases the saga gave up on; the saga ignores those late results and deletes the files.

**Expected outcome.** The first pause costs only latency. The second, longer than the saga's wait, costs failed cases. Meanwhile the paused service shows no errors, no failed handlers and a green health check.

**How to see it.**

- **Messages waiting per queue**: the `imagedownloads` queue grows during each pause and drains after.
- **Messages being handled per queue**: 5 for `imagedownloads` while paused (its prefetch).
- **Consumption per worker**: `imagedownloads` marked as paused.
- **Client case duration** rises in both pauses; **Saga outcomes** shows `download_timeout` and **Client success rate** drops in the second one only.
- **Messaging** dashboard: **Queues without consumers** stays at 0. The worker is still connected, which makes a stall harder to spot than a crash.
- Alert: **Wait timeouts** fires during the second pause.

**The lesson.** A backlog becomes an outage the moment the wait in the queue goes over the business timeout. Watch queue age, not only errors.

## `bad-day`: several faults at once

**The situation.** For 4 minutes, four things go wrong together:

| Target | Scenario | On its own it would… |
| --- | --- | --- |
| `external-imageprocess` | `flaky` | be hidden by retries ([`retries`](#retries-what-retries-buy)) |
| `external-estimations` | `slow` | add about 25 s per case ([`timeouts`](#timeouts-resilience-that-hurts), phase 1) |
| `external-notifications` | `down` | make every client notification fail |
| `api` (`/callbacks`) | `flaky` | be hidden: the fakes retry their callbacks |

**What happens.** Cases get the estimation, slower than usual, and then can't notify the client. The saga tries 4 times over about 5 minutes and ends the case as `ClientNotificationError`. The estimation is still readable from `GET /estimations/{ticket}`. Cases still retrying their notification when the step ends succeed on a later attempt.

**Expected outcome.** Few failures in image analysis or estimation. Many `notification_failed` outcomes for clients, plus higher duration.

**How to see it.** This is a practice run: work out each fault from the dashboards before you look at **Chaos scenario per service**.

- `OnRetry` on `imageprocessor` in **Resilience events**: the flaky image analysis.
- **Client case duration** and the `WaitingForEstimation` time on **Case flow**: the slow estimations.
- `notification_failed` in **Cases ended by outcome**, and `callbacknotifier` → `external-notifications` in **Failed calls to external systems**: the client endpoint is down.
- 503s on the `/callbacks` routes in **Api requests by route and status** (**Edges**), with no lost callbacks: the flaky Api, absorbed by the fakes' retries.

## `incident`: an alert for the on-call agent

**The situation.** A longer run (about 21 minutes) for talk [E5](talks/E5-an-agent-on-call.md). Someone (an AI agent) investigates it from the on-call Grafana, which receives none of the chaos telemetry ([observability.md](observability.md#on-call-grafana)).

| Step | Duration | What changes |
| --- | --- | --- |
| Baseline | 2 min | Nothing |
| The estimation service gets slow | 4 min | `external-estimations`: `slow` |
| Someone tightens the timeout | 12 min | Also `estimationprocessor`: `fail-fast` |
| Recovery | 3 min | Everything back to healthy and `standard` |

**What happens.** In the second step cases are slower but succeed, like phase 1 of [`timeouts`](#timeouts-resilience-that-hurts). In the third step the 3 s attempt timeout is shorter than the service's 5 s answer, and cases fail with `estimation_rejected`.

**Expected outcome.** **Case failure rate** fires about 7 to 8 minutes into the third step, on both Grafanas, with **External system failing** (100% of the calls to `external-estimations` fail) close behind: a second symptom that points at the wrong suspect.

**The trap.** The dependency really did get slow, but 4 minutes before anything failed. Blaming `external-estimations` is half right and fixes nothing: the failures start at our own change.

## `tour` and `random`: background runs

- **`tour`** (about 18 minutes, repeated): flaky image analysis, slow estimations, lost image analyses, client endpoint down, then flaky callbacks with duplicate estimations, with calm gaps in between. Leave it running to fill the dashboards with a bit of everything, or to show the alerts firing.
- **`random`** (endless): a random scenario on one random target for 1 to 3 minutes, then 1 minute healthy. Resilience stays `standard` and no worker is paused. Good for practice: look at the dashboards, guess what is broken, then check **Chaos scenario per service**.
