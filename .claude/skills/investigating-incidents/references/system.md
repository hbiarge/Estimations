# The Estimations system

## What it does

A client posts image URLs and a callback URL to the Api and gets a ticket at once. The system downloads each image, has an external service analyse it, asks another external service for one estimation built from the analyses, and posts the result to the client's callback URL. With everything healthy, a case with two images takes about 25 s.

One process manager, the `EstimationSaga` (one saga per case), drives four stages:

1. **Download** every image (one command per image, in parallel).
2. **Analyse** every image with the external image service, which answers later through a signed callback to the Api.
3. **Estimate** the case with the external estimation service, which also answers through a signed callback.
4. **Notify** the client with the estimation or the error.

Inside the system, services only talk through RabbitMQ messages (Rebus).

## Services

| Service (`service_name`) | Queue | Does | Calls |
| --- | --- | --- | --- |
| `api` | none (only sends) | `POST /estimations/` starts a case; `GET /estimations/{ticket}` returns its state; receives the signed callbacks | `stateholder` (gRPC) |
| `processmanager` | `processmanager` | The saga: the only service that decides what happens next. Owns every wait | — |
| `imagedownloads` | `imagedownloads` | Downloads one image | `external-images` |
| `imageprocessor` | `imageprocess` | Sends one image to be analysed | `external-imageprocess` |
| `estimationprocessor` | `externalestimations` | Sends the analyses to be estimated | `external-estimations` |
| `callbacknotifier` | `callbacknotifications` | Posts the result to the client | `external-notifications` |
| `stateholder` | `stateholder` | The latest state of every case, for the Api | — |

The four `external-*` systems belong to someone else. You see them only from our side: our client calls (tagged `peer.service`) and their callbacks into the Api. Their normal behaviour: image analysis and estimation each accept the work, then call back about 10 s later; the client endpoint answers in about 2 s.

## The saga's states and waits

States, in order: `WaitingForImagesToBeDownloaded`, `WaitingForImagesToBeAnalysed`, `WaitingForEstimation`, `EstimationReady` or `EstimationError`, then the client is notified (`ClientNotified` or `ClientNotificationError`).

Each wait starts when the saga sends the command, so time spent in a queue counts.

| Step | Wait | When it runs out |
| --- | --- | --- |
| Download one image | 2 min | The image fails (`download_timeout`); the case goes on with the rest |
| Analyse one image | about 2 min (a check every 20 s) | The image fails (`analysis_timeout`); the case goes on with the rest |
| Estimate the case | about 2 min (a check every 20 s) | The case fails (`estimation_timeout`) |
| Notify the client | 45 s per attempt, 4 attempts (now, +15 s, +30 s, +60 s) | `ClientNotificationError` |

How a case fails (`estimation_failure_reason`):

- `no_valid_images`: no image could be downloaded and analysed. A bad image alone only skips that image.
- `estimation_rejected`: the estimation processor reported it couldn't get an estimation. The external call failed after our resilience gave up.
- `estimation_timeout`: the estimation was accepted but its callback never came.

The client is told in every case: `ClientNotified` means the client was told, not that the case succeeded. `estimation_result` says `success` or `failure`.

## Resilience: how our callers treat the external systems

The four calling services wrap their HttpClient in a Polly pipeline. The set of strategies is a **profile**, chosen per service and switchable at run time (`POST /resilience/profiles/{name}` on the service). It shows in `resilience_profile_active` and in the `pipeline_instance` label of the Polly events, and a warning log says `Resilience profile changed from <old> to <new>`.

| Profile | Strategies |
| --- | --- |
| `standard` (the default) | 3 retries (exponential from 2 s, jitter), **10 s per attempt**, 30 s in total, circuit breaker (10% failures over 100+ calls in 30 s, open 5 s), concurrency limit 1000 |
| `none` | Nothing: every fault reaches the caller at once |
| `retry-only` | 3 retries (exponential from 2 s, jitter), no time limits |
| `fail-fast` | No retries, **3 s per attempt**, 5 s in total, circuit breaker (50% over 5+ calls in 30 s, open 30 s) |
| `hedging` | Up to 2 extra attempts, each sent when the previous one is 1 s late or fails; 10 s per attempt, 30 s in total |
