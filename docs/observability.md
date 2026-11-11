# Estimations — Observability

The dashboards and alert rules that show the health of the whole system in Grafana.

## Where the data comes from

| Source | Signals | How it gets to Grafana |
| --- | --- | --- |
| Every .NET service | HTTP server and client metrics, .NET runtime metrics, Rebus message metrics (`Rebus.Diagnostics` meter), traces, logs | OpenTelemetry → collector → LGTM |
| ProcessManager and StateHolder | Case metrics (`Acheve.Estimations` meter), described below | OpenTelemetry → collector → LGTM |
| RabbitMQ | Queue depth (ready and unacknowledged), consumers, broker throughput | The collector scrapes the RabbitMQ Prometheus endpoint (port 15692) |
| Tempo | Service graph (who calls whom, with failures and latency) and span metrics (per handler and endpoint) | Tempo builds them from the traces |

## Case metrics

The saga records what happens to each case; the StateHolder reports the cases that are still running.

| Metric (Prometheus name) | Type | Labels | Recorded by |
| --- | --- | --- | --- |
| `estimations_cases_started_total` | counter | | ProcessManager, when a case starts |
| `estimations_cases_finished_total` | counter | `estimation_result` (success, failure), `estimation_final_state` (ClientNotified, ClientNotificationError), `estimation_failure_reason` (none, no_valid_images, estimation_rejected, estimation_timeout) | ProcessManager, when the client was notified or the notification failed |
| `estimations_case_duration_seconds` | histogram | same as above | ProcessManager, from the request to the end of the case |
| `estimations_case_state_duration_seconds` | histogram | `estimation_state` | ProcessManager, each time a case leaves a state |
| `estimations_images_finished_total` | counter | `estimation_image_result` (analysed, download_error, download_timeout, analysis_error, analysis_timeout) | ProcessManager, when an image gets its final result |
| `estimations_cases_active` | gauge | `estimation_state` | StateHolder: running cases per state |
| `estimations_cases_oldest_active_age_seconds` | gauge | | StateHolder: age of the oldest running case |

Some details:

- The saga records a metric only after Rebus has finished the message (`OnAck`). A handler that fails and is retried records nothing for the failed attempt, so nothing counts twice. Duplicate and late messages record nothing either.
- `EstimationMetrics` creates every known counter series at 0 on startup. Otherwise Prometheus can't see the first increment of a new series, and rare outcomes would be undercounted.
- The StateHolder keeps a copy of every case in PostgreSQL (`stateholderdb`) and loads it at startup, so the two gauges survive a restart. Cases that finished more than `State:Retention` (24 hours) ago are forgotten.
- Saga data saved before these metrics existed has no start time, so those cases are counted when they finish but record no duration.

## Edges and external systems

Every dependency is reached at `localhost:<port>`, so the HTTP client metrics can't tell the external systems apart. The dashboards use Tempo's service graph instead, which names each side of the call (`imageprocessor → external-imageprocess`).

The external systems run in their own AppHost and, like real external systems, don't join our traces. So Tempo can't pair our call with their span; instead each application HttpClient tags its calls with `peer.service` (`WithPeerService` in the service defaults), and Tempo draws the edge to that name from our side of the call. The callbacks they send to the Api start new traces, so the **Callbacks from external systems** panel counts the Api's callback routes instead. The external systems' own telemetry is in Grafana with `service.namespace="external"`.

## Dashboards

Open them in Grafana under **Dashboards > Estimations**.

| Dashboard | Answers | Main panels |
| --- | --- | --- |
| **Overview** (start here) | Is the system healthy right now? | Cases started, success rate, case duration, cases running and the oldest one, error-queue depth, finished cases by outcome, firing alerts, services up or down, error logs |
| **Case flow** | Where do cases slow down or fail? | Outcomes, failure reasons and image results, time spent in each state, cases running per state, wait timeouts, recent case traces and the logs of one case (paste its ticket in **Case number**) |
| **Messaging** | Are messages flowing, or piling up? | Error-queue depth, messages waiting and being handled per queue, queues without consumers, broker throughput and redeliveries, messages handled per service and type, time waiting in the queue, handler duration and failures |
| **Edges and dependencies** | Is it us or them? | Service map, Api requests, 5xx ratio, latency and rejected callbacks, calls, failures and latency for each external system, callbacks from the external systems |
| **Chaos run** | What broke, when, and what did clients see? | The chaos scenario of each service over time, injections, the chaos monkey's steps, the traffic generator's outcomes, success rate, duration and load, and the saga's outcomes and failed external calls for comparison. Every scenario change is marked on all charts. See [chaos.md](chaos.md) |
| **Service runtime** | Is one process sick? | CPU, memory, exceptions, error and warning logs, GC, thread pool, lock contention, with a service filter |

## Alert rules

Open them in Grafana under **Alerting > Alert rules**. No contact point is set up, so they only show inside Grafana. Each rule links to the panel that explains it.

| Alert | Fires when | Severity |
| --- | --- | --- |
| Messages in the error queue | Any message in the Rebus `error` queue for 1 minute | critical |
| Queue backlog | More than 50 messages waiting in one queue for 5 minutes | warning |
| Service down | A service sent no telemetry for 2 minutes, or the RabbitMQ scrape fails | critical |
| Api 5xx ratio | More than 5% of Api requests fail with a 5xx for 5 minutes | warning |
| External system failing | More than 20% of the calls to one external system fail for 5 minutes | warning |
| Rejected callbacks | The Api refused any callback with a 4xx in the last 5 minutes | warning |
| Case failure rate | More than 20% of the cases finished in the last 15 minutes failed (with at least 5 finished) for 5 minutes | critical |
| Slow cases | The 95th percentile case duration over 15 minutes is above 3 minutes, for 5 minutes | warning |
| Stuck case | A case has been running for more than 10 minutes | critical |
| Wait timeouts | An image analysis or an estimation timed out in the last 10 minutes | warning |

To see them fire, switch a [chaos scenario](chaos.md) and send steady traffic for a few minutes (start the [traffic generator](traffic.md)). For example:

- `down` on `external-imageprocess` fails every image analysis: External system failing, and Case failure rate.
- `lost-callbacks` on `external-imageprocess` or `external-estimations` makes the saga wait in vain: Wait timeouts.
- `down` on the Api rejects every callback, so images and estimations time out: Wait timeouts.
- Stopping the CallbackNotifier from the Aspire dashboard fires Service down and Queue backlog, and Stuck case after 10 minutes.
- Stopping the external systems' AppHost makes every call to them fail: External system failing.

## Changing them

The files live in [src/Aspire/Acheve.AppHost/grafana](../src/Aspire/Acheve.AppHost/grafana) and the AppHost mounts them into the LGTM container:

- `dashboards/*.json`: one file per dashboard. Grafana reloads them every 10 seconds. Changes made in the Grafana UI aren't saved: edit the dashboard in Grafana, export its JSON and replace the file.
- `provisioning/dashboards.yaml`: tells Grafana where the dashboard files are.
- `provisioning/alerting.yaml`: the alert rules. Grafana reads them only at start, so restart the AppHost after a change. An invalid rule stops Grafana from starting; run the container with `ENABLE_LOGS_GRAFANA=true` to see why.
