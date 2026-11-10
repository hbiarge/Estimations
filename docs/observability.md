# Estimations — Observability

The dashboards and alert rules that show the health of the whole system in Grafana.

## Where the data comes from

| Source | Signals | How it gets to Grafana |
| --- | --- | --- |
| Every .NET service | HTTP server and client metrics, .NET runtime metrics, Rebus message metrics (`Rebus.Diagnostics` meter), traces, logs | OpenTelemetry → collector → LGTM |
| RabbitMQ | Queue depth (ready and unacknowledged), consumers, broker throughput | The collector scrapes the RabbitMQ Prometheus endpoint (port 15692) |
| Tempo | Service graph (who calls whom, with failures and latency) and span metrics (per handler and endpoint) | Tempo builds them from the traces |

Every dependency is reached at `localhost:<port>`, so the HTTP client metrics can't tell the external systems apart. The dashboards use Tempo's service graph instead, which names each side of the call (`imageprocessor → external-imageprocess`).

## Dashboards

Open them in Grafana under **Dashboards > Estimations**.

| Dashboard | Answers | Main panels |
| --- | --- | --- |
| **Messaging** | Are messages flowing, or piling up? | Error-queue depth, messages waiting and being handled per queue, queues without consumers, broker throughput and redeliveries, messages handled per service and type, time waiting in the queue, handler duration and failures |
| **Edges and dependencies** | Is it us or them? | Service map, Api requests, 5xx ratio, latency and rejected callbacks, calls, failures and latency for each external system, callbacks from the external systems |
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

To see them fire, lower the values in `Constants.FailureThresholds` (for example `ExternalImagesProcessing = 0.6`), or stop a worker from the Aspire dashboard, and send steady traffic for a few minutes.

## Changing them

The files live in [src/Aspire/Acheve.AppHost/grafana](../src/Aspire/Acheve.AppHost/grafana) and the AppHost mounts them into the LGTM container:

- `dashboards/*.json`: one file per dashboard. Grafana reloads them every 10 seconds. Changes made in the Grafana UI aren't saved: edit the dashboard in Grafana, export its JSON and replace the file.
- `provisioning/dashboards.yaml`: tells Grafana where the dashboard files are.
- `provisioning/alerting.yaml`: the alert rules. Grafana reads them only at start, so restart the AppHost after a change. An invalid rule stops Grafana from starting; run the container with `ENABLE_LOGS_GRAFANA=true` to see why.

## Not covered yet

The dashboards show how the services and messages behave, not how cases behave. Case-level metrics (cases started and finished, outcome, duration, image results, wait timeouts, stuck cases) need a meter in the ProcessManager. They are the next step, with a system overview dashboard and alerts on case failure rate, slow cases, stuck cases and wait timeouts.
