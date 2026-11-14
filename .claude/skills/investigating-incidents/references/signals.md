# Signals

Every service carries `service_name` (see [system.md](system.md)) and `service_namespace="application"`. Names are as Grafana's Prometheus shows them.

## Metrics: the cases

| Metric | Labels | Answers |
| --- | --- | --- |
| `estimations_cases_started_total` | — | Traffic: cases started |
| `estimations_cases_finished_total` | `estimation_result` (`success`, `failure`), `estimation_failure_reason` (`none`, `no_valid_images`, `estimation_rejected`, `estimation_timeout`), `estimation_final_state` (`ClientNotified`, `ClientNotificationError`) | How cases end, and why they fail |
| `estimations_images_finished_total` | `estimation_image_result` (`analysed`, `download_error`, `download_timeout`, `analysis_error`, `analysis_timeout`) | How each image ends |
| `estimations_case_duration_seconds_*` | histogram | Case duration, from start to client notified |
| `estimations_case_state_duration_seconds_*` | `estimation_state` (the saga states), histogram | Time spent in each state: where cases slow down |
| `estimations_cases_active`, `estimations_cases_oldest_active_age_seconds` | — | Cases running now and the oldest one |

The Prometheus counters start at the first event, so use `increase(...[15m])` or `rate(...[5m])`, never the raw value.

## Metrics: our calls and our endpoints

| Metric | Key labels | Answers |
| --- | --- | --- |
| `http_client_request_duration_seconds_*` | `service_name` (the caller), `server_port`, `http_response_status_code`, `error_type` | Our outgoing calls: latency and failures. Each caller talks to one external system (see [system.md](system.md)); `server_port` 5001 images, 5002 image analysis, 5003 estimations, 5004 notifications |
| `http_server_request_duration_seconds_*` | `service_name`, `http_route`, `http_response_status_code` | Requests to our services: the Api's client routes and callback routes (`/callbacks/...`) |
| `resilience_profile_active` | `service_name`, `resilience_profile` | 1 for the resilience profile each caller uses now |
| `resilience_polly_strategy_events_total` | `service_name`, `event_name`, `pipeline_instance` (the profile), `event_severity` | What our resilience did: `OnRetry`, `OnTimeout`, `OnHedging`, `OnCircuitOpened`, `OnCircuitClosed`, `OnCircuitHalfOpened`, `OnRateLimiterRejected`; `ExecutionAttempt` and `PipelineExecuting`/`PipelineExecuted` count every call |
| `resilience_polly_strategy_attempt_duration_milliseconds_*` | `service_name`, `pipeline_instance`, histogram | How long each attempt took, including the ones a timeout cut short |
| `resilience_polly_pipeline_duration_milliseconds_*` | `service_name`, `pipeline_instance`, histogram | How long a whole call took, all attempts included |

An `http_client` failure with `error_type` and no status code means the call never got an answer (timeout, refused connection).

## Metrics: the broker

`rabbitmq_detailed_queue_messages_ready{queue}` (waiting) and `rabbitmq_detailed_queue_messages_unacked{queue}` (being handled), per queue (see [system.md](system.md)); `queue="error"` holds messages whose handler failed every retry. `rabbitmq_detailed_queue_consumers{queue}` is the number of consumers per queue.

## Logs (Loki)

Indexed by `service_name` and `service_namespace`. Each line has structured metadata: `severity_text` (`Information`, `Warning`, `Error`), `scope_name` (the logger category), `caseNumber` when the line is about a case, `trace_id`, `span_id`.

Worth searching:

- `processmanager` Warnings: the saga's view of every failure. Lines start with `Case number <guid>.`, for example `Wait time exceeded to get the analysis of image 2` or `Unable to download image 0. <reason>`.
- `estimationprocessor`, `imageprocessor`, `imagedownloads`, `callbacknotifier` Warnings and Errors: why an outgoing call failed, as the worker saw it.
- `Resilience profile changed from <old> to <new>`: a change to a caller's resilience profile, with its settings.
- `api`: rejected callbacks, and requests to the Api.

## Traces (Tempo)

- One trace per case, from `POST /estimations/` on the `api`, through every message (`<Message type>, Acheve.Common.Messages send|receive|process`), to the client notification.
- Our call to an external system is a client span on the caller with `peer.service` (`external-images`, `external-imageprocess`, `external-estimations`, `external-notifications`). Our traces end there: the external systems belong to someone else.
- Their callbacks arrive as `POST /callbacks/image-analysis/...` and `POST /callbacks/estimation/...` on the `api`, then as the `image analysis callback` and `estimation callback` spans in the case's trace.
- Attributes: `estimation.case_number`, `estimation.image_id`, `http.response.status_code`, `error.type`, `rebus.message.type`, `rebus.handler.type`.

## Dashboards

| UID | Answers |
| --- | --- |
| `estimations-overview` | Is the system healthy right now? |
| `estimations-case-flow` | Where do cases slow down or fail? |
| `estimations-messaging` | Are messages flowing or piling up? |
| `estimations-edges` | Is it us or them? Calls, failures and latency per external system |
| `estimations-runtime` | Is one process sick? CPU, memory, exceptions, GC, thread pool |

## Alerts

| Alert | Fires when |
| --- | --- |
| Messages in the error queue | Any message in the `error` queue for 1 minute |
| Queue backlog | More than 50 messages waiting in one queue for 5 minutes |
| Service down | A service sent no telemetry for 2 minutes, or the RabbitMQ scrape fails |
| Api 5xx ratio | More than 5% of Api requests fail with 5xx for 5 minutes |
| External system failing | More than 20% of the calls to one external system fail for 5 minutes |
| Rejected callbacks | The Api refused a callback with a 4xx in the last 5 minutes |
| Case failure rate | More than 20% of the cases finished in 15 minutes failed (at least 5), for 5 minutes |
| Slow cases | p95 case duration over 15 minutes above 3 minutes, for 5 minutes |
| Stuck case | A case running for more than 20 minutes |
| Wait timeouts | An image analysis or an estimation wait ran out in the last 10 minutes |

An alert fires minutes after its symptom starts (it waits its `for` time, and many look back 10–15 minutes). Date the symptom from the metrics, not from the alert.
