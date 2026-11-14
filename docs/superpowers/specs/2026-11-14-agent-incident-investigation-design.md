# An AI agent investigates an incident through Grafana — design

## Intent

A new talk shows an AI agent (Claude Code) working an incident the way an on-call engineer would: an alert fires, and the agent finds the cause using only the Grafana HTTP API (alerts, Prometheus, Loki, Tempo). The talk is about what an agent is good at (moving between signals tirelessly, writing queries) and where it goes wrong (blaming the obvious suspect, stopping at the first plausible story).

For it to mean anything, the agent must not be able to read the answer. Today every answer is lying around in Grafana: chaos metrics, chaos log lines, a `chaos.injected` span tag, the chaos monkey's annotations, the **Chaos run** dashboard, and the fake external systems' own telemetry. The demo needs a Grafana where that information **does not exist**, not one where it is hidden behind rules.

## Goals

- An on-call Grafana with the same dashboards and alerts, holding only what a real team would have: telemetry from our own services and RabbitMQ.
- A chaos monkey strategy that tells one clear incident story, long enough to fire a real alert.
- A Claude Code skill that gives the agent everything a new on-call engineer would get on day one: the system map, what each signal means, how to query the on-call Grafana, and an investigation method.
- A talk proposal (E5) in `docs/talks/`, with the demo script.

## Non-goals

- Not a general proxy, RBAC or multi-tenant setup.
- Not an autonomous remediation agent: it investigates and reports; it changes nothing.
- No changes to the existing Grafana, its dashboards or its alerts. The current demos keep working.

## Anti-goals

- **A blindfold that leaks.** If the agent can find `chaos_*`, a chaos log line, the `chaos.injected` tag or an external system's own spans, the demo is staged. This would make it a failure even if everything technically runs.
- **A skill that gives the answer away.** The skill describes the system, not the chaos scenarios or the strategy. "Slow dependency plus tight timeout" must never appear in it.

## Constraints

- Everything runs from the application's AppHost (`src/Aspire/Acheve.AppHost/AppHost.cs`), Development only, like the rest of the chaos tooling.
- The external fakes send telemetry directly to LGTM on port 14317 through their own collector. The on-call Grafana must not be fed from that path.
- Do not use `sed -i` on files that are bind-mounted into an LGTM container: it replaces the inode and breaks single-file mounts.
- The agent only gets data through the on-call Grafana's API. It must not read this repository (the chaos docs and code describe the answers).

## Approaches

1. **Query proxy in front of today's Grafana.** Block endpoints and dashboards, reject queries that name forbidden metrics, scrub log lines and span attributes from the responses. Rejected: it leaks through label-value endpoints, regex selectors and broad log searches, and it means writing code that rewrites Grafana responses.
2. **A second on-call Grafana that never receives the leaks.** Chosen. The app's collector sends it a filtered copy of the telemetry. The blindfold holds because the data is not there, and it mirrors real life: you never see inside systems you don't own.
3. **Folder permissions and a Viewer service account on today's Grafana.** Rejected: it hides the Chaos run dashboard but not the data; any query still finds `chaos_*`.

## Design

### 1. On-call Grafana (`lgtm-oncall`)

A second `grafana/otel-lgtm:0.39.0` container in `AppHost.cs`, next to `lgtm`:

- **Grafana on a fixed host port, 3300**, so the skill can name it. OTLP gRPC on a fixed host port (`Constants.Telemetry.OncallOtlpGrpcPort`, 14318) for the app collector.
- **Its own volume** (`lgtm-oncall-data`).
- **Dashboards:** every file in `grafana/dashboards` except `chaos-run.json`, mounted one file at a time into `/otel-lgtm/estimations-dashboards`, plus the same `dashboards.yaml` and `alerting.yaml` mounts as `lgtm`. The two **Edges** panels built on `chaos_scenario_active` will show "No data"; they stay. *(My call: they leak nothing and keep the dashboards identical to the files.)*
- **Access:** anonymous access as **Viewer**, so the agent can read dashboards, alerts and data sources but change nothing, with no token to manage. Human admin login keeps the image's default. The builder must check how `otel-lgtm` 0.39 sets anonymous access (environment variables such as `GF_AUTH_ANONYMOUS_ORG_ROLE=Viewer`, or its start script) and confirm with a request that a Viewer can't `POST /api/dashboards/db` but can read `/api/prometheus/grafana/api/v1/alerts` and query the data sources.
- In the Aspire dashboard it shows as **Grafana (on-call)**.

### 2. The filtered copy (app collector)

In `src/Aspire/Acheve.AppHost/otel-collector.yaml`, a new exporter `otlp_grpc/oncall` (endpoint from `ONCALL_OTLP_ENDPOINT`, set in `AppHost.cs`), and new pipelines `traces/oncall`, `metrics/oncall`, `logs/oncall`, `metrics/rabbitmq-oncall`. The existing pipelines stay as they are.

The on-call pipelines drop:

| Signal | Dropped | Why |
| --- | --- | --- |
| All | Resources whose `service.namespace` isn't `application` (removes `trafficgenerator`, `chaosmonkey`) | Real systems have no traffic generator or chaos monkey |
| Metrics | Every metric named `chaos_*`, and `consumption_paused` | Injected faults and a paused worker don't announce themselves in real life |
| Logs | Records from the `Acheve.Common.Chaos` category, matched by instrumentation scope name and/or the category attribute. The builder checks which one the .NET OTLP exporter sets. | "Chaos scenario changed…" and "Chaos injected…" lines on the Api |
| Traces | The `chaos.injected` span attribute (deleted, the span stays) | The tag on the Api's `/callbacks` spans |

The RabbitMQ metrics go to on-call unchanged: it's our broker.

**Kept on purpose:** `resilience_*` metrics, `resilience_polly_strategy_events_total` (including its `pipeline_instance` label), and the "resilience profile changed" warning log. These describe our own configuration, which a real team can always see. Finding "what changed on our side?" is the lesson of the talk.

The external fakes never reach the on-call Grafana: their collector only knows `lgtm`. Traces seen in on-call therefore end at our client spans to `external-*`, like real systems you don't own.

**Leak check (part of done):** with the `incident` strategy running, these queries against port 3300 return nothing:
- `GET /api/datasources/proxy/uid/prometheus/api/v1/label/__name__/values`, filtered for `chaos`
- Loki `{service_name=~".+"} |= "Chaos"`
- TraceQL `{ span.chaos.injected != nil }`
- Any series or stream with `service_name` of `trafficgenerator`, `chaosmonkey` or `external-*`

### 3. The `incident` strategy

A new chaos monkey strategy, `incident`, in `src/Tools/Acheve.Tools.ChaosMonkey/appsettings.json`. Its name goes in `Constants.ChaosMonkey` (`Incident = "incident"`, added to `Strategies`) and a description in `src/Aspire/Shared/ChaosMonkeyCommands.cs`.

| Step | Duration | Chaos | Resilience |
| --- | --- | --- | --- |
| Baseline | 2 min | — | — |
| The estimation service gets slow | 4 min | `external-estimations`: `slow` | — |
| Someone tightens the timeout | 12 min | `external-estimations`: `slow` | `estimationprocessor`: `fail-fast` |
| Recovery | 3 min | — | — |

The story has a trap built in. The dependency really did get slow (4 minutes earlier), but cases only start failing at the config change. An agent that blames `external-estimations` is half right and fixes nothing. **Case failure rate** (over 20% in 15 minutes, held for 5 minutes) should fire about 8–10 minutes into the third step. The builder confirms this with the traffic generator at its default rate and adjusts the step if needed. **Slow cases** may fire too. That is fine: it's a second symptom.

Add the strategy to `docs/chaos-scenarios.md` (in the table, plus a short section) and to `docs/runtime-faults.md` only where strategies are listed.

### 4. The skill `investigating-incidents`

`.claude/skills/investigating-incidents/SKILL.md`, plus reference files the main file points to. It's written for an agent, following the repo's `writing-for-agents` conventions: short main file, details in references.

- **SKILL.md**: when to use it (an alert fired, or "why is X failing" on Estimations), the rules, the method and the report format.
  - **Rules:** only use the on-call Grafana at `http://localhost:3300`. Don't read the Estimations source or docs. Don't change anything. Say what you couldn't verify.
  - **Method:** (1) start from the alert: what users feel, since when. (2) Narrow it down: which outcome, which stage of the saga, which service. (3) Ask what changed: config, deploys, dependency behaviour, and line up start times. (4) Find one piece of evidence per signal (a metric, a log line, a trace) that agrees. (5) Rule out at least one rival explanation explicitly. (6) Report.
  - **Report:** symptom, timeline, cause, evidence with the query behind each piece, rivals ruled out, suggested fix, confidence.
- **references/system.md**: services and queues, the saga's stages and waits, the four external systems and which service calls each, and what the resilience profiles do (names and settings, because they are our config). Rewritten from `docs/system.md` and `docs/runtime-faults.md` with every mention of chaos removed.
- **references/signals.md**: the metric names and labels (`estimations_*`, `resilience_*`, Polly events, HTTP client and server metrics, RabbitMQ), the log categories worth searching, span names and attributes, and the dashboards and alerts, each with one line on what it answers.
- **references/grafana-api.md**: curl recipes against port 3300: firing alerts and rules, PromQL instant and range queries through the data source proxy, LogQL, TraceQL search and fetching a trace by id, and listing dashboards and reading a panel's query. Data source UIDs: `prometheus`, `loki` and `tempo`. The builder confirms the last two in the `otel-lgtm` image. It can rely on the Grafana and LGTM plugin skills already enabled in the repo for query syntax, and doesn't repeat them.

### 5. Running the demo

The agent must not have this repository in reach, so the demo runs it from an empty folder. A script, `docs/talks/E5-demo/start-oncall-agent.sh`, creates a temporary folder, copies the skill into its `.claude/skills/`, adds a `.claude/settings.json` that allows `curl` to `localhost:3300` only, and prints the `claude` command to run there. The demo steps (start both AppHosts, start traffic, run `incident`, wait for the alert, give the agent the alert text) go in the talk proposal.

Residual risk, written in the talk as a point to discuss: the agent could still find the main Grafana by scanning ports. The settings allow-list makes that visible and blockable.

### 6. The talk proposal

`docs/talks/E5-an-agent-on-call.md`, in the format of the other proposals: level Expert, area **Investigation** (a new row in `docs/talks/README.md`), 45 minutes. It has:
- an outline: why a blindfold, the skill as the onboarding doc, the method, the trap, and what the agent got wrong in rehearsal
- the demo
- the files
- links to M3 and E3

## Left to the builder

- How `otel-lgtm` 0.39 configures anonymous access and data source UIDs (verify, don't assume).
- The exact collector processor syntax (`filter`, `transform`) for each drop, checked against collector 0.164.0.
- Whether the log filter matches on instrumentation scope or on an attribute.
- Fine-tuning the `incident` step durations so the alert fires inside the third step.
- The skill's wording and the depth of each reference file, within the rules above.

## Done when

- Both Grafanas run from the AppHost. The existing one is unchanged.
- The leak check above returns nothing on port 3300, while the same queries on the main Grafana return data.
- `incident` runs from the dashboard and **Case failure rate** fires on the on-call Grafana during its third step.
- A Claude Code session started by the demo script investigates using only the skill and port 3300, and produces a report. The report's quality is for rehearsal, not a pass/fail condition.
- The docs, the strategy table and the talk index are updated.
