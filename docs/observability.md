# Estimations — Observability

How to see what the system is doing: where the telemetry goes, the metrics that matter, the dashboards, the alerts and how to follow one case.

## Where to look

- **Aspire dashboard** (https://localhost:17000): the quickest look at a run. Logs, traces and metrics of every application service, kept only in memory.
- **Grafana** (the `lgtm` resource link, user `admin`, password `admin`): everything, kept between runs in the `lgtm-data` volume. Tempo has the traces, Loki the logs (linked to their traces), Prometheus the metrics. The dashboards and alert rules are in the `Estimations` folder.

Every service sends logs, traces and metrics with OpenTelemetry to a collector, which sends a copy to the Aspire dashboard and another to Grafana. The external systems have their own collector, which reaches Grafana through host port 14317, so their telemetry only arrives while the application's AppHost runs.

Every signal is tagged with `service.namespace`: `application`, `external` (the fakes) or `tools` (traffic generator, chaos monkey). In Prometheus the `job` label is `<namespace>/<service>`. Filter on it to see one side only.

## Where the data comes from

| Source | Signals |
| --- | --- |
| Every .NET service | HTTP, .NET runtime and Rebus message metrics, traces, logs |
| ProcessManager and StateHolder | Case metrics (`Acheve.Estimations`), below |
| Api and fake external systems | Chaos injections and the scenario in force (`Acheve.Chaos`) |
| The four callers of the external systems | The resilience profile in force (`Acheve.Resilience`) and Polly's events (`Polly`) |
| Every Rebus worker | Whether it is consuming (`Acheve.Consumption`) |
| Traffic generator and chaos monkey | The clients' view of the cases (`Acheve.Traffic`) and the monkey's switches (`Acheve.ChaosMonkey`) |
| RabbitMQ | Queue depth (ready and unacknowledged), consumers, throughput: the collector scrapes port 15692 |
| Tempo | Service graph (who calls whom) and span metrics, built from the traces |

The chaos, resilience and consumption metrics are explained in [runtime-faults.md](runtime-faults.md); the traffic metrics in [traffic.md](traffic.md).

## Case metrics

The saga records what happens to each case; the StateHolder reports the cases still running.

| Metric | Type | Labels | What it counts |
| --- | --- | --- | --- |
| `estimations_cases_started_total` | counter | | Cases started |
| `estimations_cases_finished_total` | counter | `estimation_result` (success, failure), `estimation_final_state` (ClientNotified, ClientNotificationError), `estimation_failure_reason` (none, no_valid_images, estimation_rejected, estimation_timeout) | Cases finished |
| `estimations_case_duration_seconds` | histogram | same | From the request to the end of the case |
| `estimations_case_state_duration_seconds` | histogram | `estimation_state` | Time spent in each state |
| `estimations_images_finished_total` | counter | `estimation_image_result` (analysed, download_error, download_timeout, analysis_error, analysis_timeout) | Images by final result |
| `estimations_cases_active` | gauge | `estimation_state` | Cases running, per state (StateHolder) |
| `estimations_cases_oldest_active_age_seconds` | gauge | | Age of the oldest running case (StateHolder) |

Two things to know:

- A metric is recorded only after Rebus has finished the message. A retried handler, a duplicate or a late message records nothing, so nothing counts twice.
- Every known counter series starts at 0, so Prometheus sees the first increment of a rare outcome.

## Dashboards

**Dashboards > Estimations** in Grafana. They open on the last 15 minutes and refresh every 5 seconds.

| Dashboard | Answers | Main panels |
| --- | --- | --- |
| **Overview** (start here) | Is the system healthy right now? | Cases started, success rate, duration, cases running and the oldest one, error-queue depth, outcomes, firing alerts, services up or down, error logs |
| **Case flow** | Where do cases slow down or fail? | Outcomes, failure reasons, image results, time per state, wait timeouts, recent case traces, and the logs of one case (paste its ticket in **Case number**) |
| **Messaging** | Are messages flowing or piling up? | Error queue, messages waiting and being handled per queue, queues without consumers, redeliveries, handler duration and failures |
| **Edges and dependencies** | Is it us or them? | Service map, Api requests and rejected callbacks, calls, failures and latency per external system, and for the two systems that call back, **Our calls vs callbacks** (a gap means lost or duplicated callbacks) |
| **Chaos run** | What broke, when, and what did clients see? | Chaos scenario, resilience profile and consumption of each service over time, every change marked on every chart, the monkey's steps, and next to them the clients' outcomes and the saga's outcomes. See [runtime-faults.md](runtime-faults.md#the-chaos-run-dashboard) |
| **Service runtime** | Is one process sick? | CPU, memory, exceptions, GC, thread pool, per service |

The external systems don't join our traces, so the dashboards name them from our side of the call: every application HttpClient tags its calls with `peer.service`, and Tempo's service graph draws the edge to that name.

## Alert rules

**Alerting > Alert rules** in Grafana. No contact point is set up, so they only show inside Grafana. Each rule links to the panel that explains it.

| Alert | Fires when | Severity |
| --- | --- | --- |
| Messages in the error queue | Any message in the Rebus `error` queue for 1 minute | critical |
| Queue backlog | More than 50 messages waiting in one queue for 5 minutes | warning |
| Service down | A service sent no telemetry for 2 minutes, the RabbitMQ scrape fails, or a service is in the `down` chaos scenario | critical |
| Api 5xx ratio | More than 5% of Api requests fail with 5xx for 5 minutes | warning |
| External system failing | More than 20% of the calls to one external system fail for 5 minutes | warning |
| Rejected callbacks | The Api refused a callback with a 4xx in the last 5 minutes | warning |
| Case failure rate | More than 20% of the cases finished in 15 minutes failed (at least 5) for 5 minutes | critical |
| Slow cases | p95 case duration over 15 minutes above 3 minutes, for 5 minutes | warning |
| Stuck case | A case running for more than 20 minutes (the worst case ends in about 11, see [system.md](system.md#timeouts-and-limits)) | critical |
| Wait timeouts | An image analysis or an estimation timed out in the last 10 minutes | warning |

[chaos-scenarios.md](chaos-scenarios.md) says which alerts each scenario should fire.

## Following one case

Each case is one distributed trace. `POST /estimations` returns its trace id as `OperationId`.

- Inside the system the trace travels on its own: HTTP and gRPC calls send `traceparent`, and Rebus copies it into every message, including the deferred timeouts.
- The external services don't send our trace context back, so it travels in the callback URL. The callback signature covers it, so nobody can attach a callback to someone else's trace.
- When a callback arrives, the Api starts an `image analysis callback` or `estimation callback` span whose parent is the span that sent the request. ASP.NET Core's own request span for the callback stays in its own trace, linked from it.
- Spans are tagged with `estimation.case_number` (and `estimation.image_id`). On the **Case flow** dashboard, paste a ticket in **Case number** to see that case's logs.

A trace lasts as long as its case: about 25 s when healthy, up to about 11 minutes when every wait runs out.

### Sampling and long traces

Here nothing is sampled: the SDKs keep every span (the .NET default, parent-based "always on") and the collectors have no sampling processor ([otel-collector.yaml](../src/Aspire/Acheve.AppHost/otel-collector.yaml)). In production you would sample to cut volume, and the choice matters because our traces are long and the interesting ones are the longest.

| Strategy | Where the decision is made | Effect on a long case trace |
| --- | --- | --- |
| **Head sampling** (SDK, for example parent-based ratio) | When the first span starts, at `POST /estimations` | Safe. The decision travels in the `sampled` flag of `traceparent`, and our `traceparent` survives every hop: HTTP and gRPC headers, Rebus headers, the deferred timeouts stored in PostgreSQL, and the callback URLs. A case is kept or dropped whole, however long it lasts. The cost: the decision is blind, so a failed case is as likely to be dropped as a healthy one. |
| **Probabilistic sampling** (collector, `probabilistic_sampler`) | On each span, from a hash of the trace id | Safe too: every span of a trace has the same trace id, so it gets the same answer whenever it arrives. Also blind to the outcome. Every collector in the path needs the same settings. |
| **Tail sampling** (collector, `tail_sampling`) | After the collector has buffered a trace's spans for `decision_wait` (30 s by default) | Risky. It can keep "traces with errors" or "traces slower than 2 minutes", but it decides once the wait ends. A healthy case (about 25 s) barely fits. A case that times out has its error and its slow end minutes later, after the decision, so the policy never sees them: the traces you most want are the ones it misjudges. Spans that arrive after the decision are either judged again as a new, partial trace or follow the earlier decision, if the collector keeps a decision cache. Either way you can get half a case. |

To tail sample traces like these you would need a `decision_wait` longer than the worst case (more than 11 minutes). That means holding every running case's spans in collector memory for that long. You would also need every span of a trace to reach the same collector instance (a `loadbalancing` exporter routing by trace id). That is costly, so the usual approach is:

- Use head or probabilistic sampling for volume, so cases are kept or dropped whole.
- Find the bad cases with the metrics and logs, which are never sampled: the `estimations_*` metrics, the **Wait timeouts** alert, the logs of one case on **Case flow**.
- If you need every failed case's trace, raise the head-sampling ratio, or decide from the outcome after the case ends (for example a log or event that names the trace id) instead of in the collector.

One more effect: ASP.NET Core's own span for an incoming callback is its own trace, with its own sampling decision. Our `image analysis callback` or `estimation callback` span takes its parent from the `traceparent` in the URL, so it follows the case's decision, but the linked request trace may be dropped. A link can then point to a trace that was never stored.

## Changing dashboards and alerts

The files are in [src/Aspire/Acheve.AppHost/grafana](../src/Aspire/Acheve.AppHost/grafana), mounted into the LGTM container:

- `dashboards/*.json`: one file per dashboard, reloaded every 10 seconds. Changes made in the Grafana UI aren't saved: export the JSON and replace the file.
- `provisioning/alerting.yaml`: the alert rules, read only at start. Restart the AppHost after a change. An invalid rule stops Grafana from starting; run the container with `ENABLE_LOGS_GRAFANA=true` to see why.

## On-call Grafana

A second LGTM container, `lgtm-oncall`, at [http://localhost:3300](http://localhost:3300) (**Grafana (on-call)** in the Aspire dashboard). It has the same dashboards (except **Chaos run**) and the same alerts, but it only gets what a real team would see. It's where an AI agent investigates incidents in talk [E5](talks/E5-an-agent-on-call.md).

Its dashboards and alert rules are copies with no mention of chaos or the tools, since a Viewer can read them through the API. [OncallGrafana.cs](../src/Aspire/Acheve.AppHost/OncallGrafana.cs) writes them under the AppHost's `obj/oncall-grafana` at every start:

- The two **Edges** panels that only show a chaos scenario are left out.
- **Overview > Services** and the **Service down** rule lose their `down` scenario state.
- Queries no longer leave out the tools, because the on-call data has none.

Restart the AppHost to pick up changes to the originals. If an original changes so that a mention would get through, the AppHost refuses to start and names the line. Then update `OncallGrafana.cs`.

The application's collector sends it a filtered copy:

| Left out | Why |
| --- | --- |
| The tools (`trafficgenerator`, `chaosmonkey`): everything not in the `application` namespace | A real system has no traffic generator or chaos monkey |
| The `chaos_*` and `consumption_paused` metrics | Injected faults and paused workers don't announce themselves |
| The `Acheve.Common.Chaos` and `Acheve.Common.Consumption` logs | The Api's "Chaos scenario changed" and "Chaos injected" lines, and "Message consumption paused" |
| Requests to `/chaos`, `/consumption` and `/resilience` (spans, `http_route` values, ASP.NET Core request logs) | The chaos monkey calls them on every service at every step, even when nothing changes, so they would mark each step. A real resilience change still shows: its "Resilience profile changed" log and `resilience_profile_active` |
| The `chaos.injected` span tag | The span stays; only the tag goes |

The external systems never reach it: their collector only sends to `lgtm`. Traces there end at our calls to `external-*`, as with systems you don't own. The RabbitMQ metrics, the resilience metrics and our own logs are all there.

**Logging in.** Anonymous access is off. The user is `admin`, and the password is the AppHost parameter `oncall-grafana-admin-password`. It's generated on the first run and kept in the AppHost's user secrets:

```bash
dotnet user-secrets list --project src/Aspire/Acheve.AppHost
```

The agent gets a Viewer service account token instead, from [start-oncall-agent.sh](talks/E5-demo/start-oncall-agent.sh). [check-blindfold.sh](talks/E5-demo/check-blindfold.sh) checks that nothing leaks.
