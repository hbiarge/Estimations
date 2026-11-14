# Agent incident investigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An on-call Grafana with no chaos telemetry, an `incident` chaos monkey strategy, an `investigating-incidents` skill, and a demo script so an AI agent can investigate a real alert through the Grafana API, plus talk proposal E5.

**Architecture:** A second `otel-lgtm` container (`lgtm-oncall`) is fed by new, filtered pipelines in the application's OpenTelemetry Collector. Access is through a Viewer service account whose token is minted by the demo script. The agent runs in a throwaway folder that only holds the skill and the token.

**Tech Stack:** .NET Aspire AppHost (C#), OpenTelemetry Collector contrib 0.164.0 (`filter`, `transform` processors, OTTL), `grafana/otel-lgtm:0.39.0`, bash + curl + jq, xUnit + Shouldly, Claude Code skills (Markdown).

**Spec:** `docs/superpowers/specs/2026-11-14-agent-incident-investigation-design.md`

## Global Constraints

- The existing `lgtm` container, its dashboards, alerts and collector pipelines must not change in behaviour.
- Never use `sed -i` (or any tool that replaces the file) on files bind-mounted into an LGTM container. Use the Edit tool.
- On-call Grafana: host port **3300**; OTLP gRPC host port **14318** (`Constants.Telemetry.OncallOtlpGrpcPort`); volume `lgtm-oncall-data`; display text **Grafana (on-call)**.
- Anonymous access off (`GF_AUTH_ANONYMOUS_ENABLED=false`); admin password from the secret Aspire parameter `oncall-grafana-admin-password` (generated, persisted, `Special = false`, `MinLength = 32`) passed as `GF_SECURITY_ADMIN_PASSWORD`.
- Service account `oncall-agent`, role `Viewer`, token TTL 8 h (`secondsToLive: 28800`); the agent gets it as `GRAFANA_TOKEN`.
- Instrument names are dotted in OTLP: drop `chaos.*` and `consumption.paused`; delete span attribute `chaos.injected`; drop logs whose instrumentation scope starts with `Acheve.Common.Chaos`; keep only resources with `service.namespace == "application"` (RabbitMQ pipeline is kept whole).
- Keep visible: `resilience.*` metrics, Polly events, the "resilience profile changed" log.
- The skill never mentions chaos, the chaos monkey, scenarios or strategies.
- Docs follow the repo's style: short sentences, plain English, tables where the other docs use them.

## Review Focus

1. **Scraped RabbitMQ metrics have no `service.namespace`.** A namespace filter put on the RabbitMQ pipeline would drop them all. Task 2's validation checks `rabbitmq_detailed_queue_messages_ready` exists on 3300.
2. **The filter matches nothing because a name is wrong** (for example `chaos_` vs `chaos.`): the leak check (Task 4) must run *while* chaos is active, and it must assert the main Grafana *does* return the same series. Otherwise an empty result proves nothing.
3. **The demo script runs twice.** The second run must reuse the `oncall-agent` account and mint a new token, not fail on "already exists" (Task 6 test).
4. **The AppHost restarts with a persisted volume.** Grafana keeps the admin password from its first run in `/data`. The parameter is persisted so the two stay equal. Task 3 checks a login after a restart.
5. **The external AppHost isn't running.** The on-call Grafana must still start and the alerts must still evaluate. Nothing new may wait on the fakes (Task 3).

---

### Task 1: The `incident` strategy

**Files:**
- Modify: `src/Common/Acheve.Common.Shared/Constants.cs` (class `ChaosMonkey`)
- Modify: `src/Aspire/Shared/ChaosMonkeyCommands.cs` (`Details`)
- Modify: `src/Tools/Acheve.Tools.ChaosMonkey/appsettings.json` (`Strategies`)
- Modify: `test/Tools/Acheve.Tools.ChaosMonkey.Tests/ChaosMonkeyOptionsValidatorTests.cs`
- Modify: `docs/chaos-scenarios.md`

**Interfaces:**
- Produces: `Constants.ChaosMonkey.Incident = "incident"`, included in `Constants.ChaosMonkey.Strategies`; dashboard command **Strategy: incident**; `POST /strategies/incident`.

- [ ] **Step 1: Write the failing test** `The_incident_strategy_tightens_the_timeout_after_the_dependency_slows` in `ChaosMonkeyOptionsValidatorTests.cs`. Load the shipped settings the same way `The_shipped_settings_are_valid` does, take `Strategies[Constants.ChaosMonkey.Incident]` and assert:
  - 4 steps with durations 2, 4, 12 and 3 minutes
  - steps 2 and 3 have `Chaos["external-estimations"] == "slow"`
  - only step 3 has `Resilience["estimationprocessor"] == "fail-fast"`
  - steps 1 and 4 set nothing
- [ ] **Step 2: Run it.** `dotnet test test/Tools/Acheve.Tools.ChaosMonkey.Tests --filter Incident`. Expected: it fails to compile, because `Incident` is not defined.
- [ ] **Step 3: Add the strategy.**
  - The constant goes after `Timeouts` and before `Tour`/`Random` in `Strategies`.
  - The description in `Details`: `"The estimations get slow, then someone tightens the timeout: an incident to investigate from the on-call Grafana."`, icon `"Alert"`.
  - The steps in `appsettings.json`, named: `Baseline`; `The estimation service gets slow`; `Someone tightens the timeout (fail fast)`; `Recovery`.
- [ ] **Step 4: Run the whole test project.** Same command without the filter. Expected: all pass, including `The_shipped_settings_are_valid`.
- [ ] **Step 5: Document it.** In `docs/chaos-scenarios.md`, add an `incident` row to the case table (question: *Can an agent find the cause?*, breaks: `external-estimations` slow, then `estimationprocessor` → `fail-fast`). Add a short section with the four steps and expected outcome: **Case failure rate** fires 8–10 minutes into step 3, on both Grafanas. Link it to talk E5.
- [ ] **Step 6: Commit.** `ChaosMonkey: incident strategy for the on-call demo`

### Task 2: Filtered pipelines in the app collector

**Files:**
- Modify: `src/Aspire/Acheve.AppHost/otel-collector.yaml`

**Interfaces:**
- Consumes: env var `ONCALL_OTLP_ENDPOINT` (host:port, set in Task 3).
- Produces: exporter `otlp_grpc/oncall`; pipelines `traces/oncall`, `metrics/oncall`, `logs/oncall`, `metrics/rabbitmq-oncall`.

- [ ] **Step 1: Add the processors.**
  - `filter/oncall-application`: drops spans, metrics and log records where `resource.attributes["service.namespace"] != "application"`.
  - `filter/oncall-chaos`: drops metrics where `IsMatch(name, "^chaos\\.")` or `name == "consumption.paused"`, and log records where `IsMatch(instrumentation_scope.name, "^Acheve\\.Common\\.Chaos")`.
  - `transform/oncall-chaos`: `delete_key(span.attributes, "chaos.injected")` in a trace statement.

  Add a comment block in the file's style explaining that the on-call copy carries only what a real team would see.
- [ ] **Step 2: Add the exporter and the pipelines.** `otlp_grpc/oncall` with `endpoint: ${env:ONCALL_OTLP_ENDPOINT}` and `tls.insecure: true`. Each on-call pipeline uses receivers `[otlp]` and processors `[filter/dotnet-cli, filter/oncall-application, filter/oncall-chaos, transform/oncall-chaos (traces only), batch]`. The exception is `metrics/rabbitmq-oncall`: receiver `[prometheus/rabbitmq]`, processors `[batch]` only (Review Focus 1). Leave the existing four pipelines byte-for-byte unchanged.
- [ ] **Step 3: Validate the config offline.**
  ```bash
  docker run --rm -v "$PWD/src/Aspire/Acheve.AppHost/otel-collector.yaml:/c.yaml:ro" -e ASPIRE_ENDPOINT=x:1 -e ASPIRE_API_KEY=x -e LGTM_OTLP_ENDPOINT=x:1 -e ONCALL_OTLP_ENDPOINT=x:1 -e RABBITMQ_PROMETHEUS_ENDPOINT=x:1 otel/opentelemetry-collector-contrib:0.164.0 validate --config=/c.yaml
  ```
  Expected: exit 0, no output. Use the image name the AppHost's `AddOpenTelemetryCollector` uses, if it differs.
- [ ] **Step 4: Commit together with Task 3** (the collector needs the env var to start).

### Task 3: The on-call LGTM container

**Files:**
- Modify: `src/Common/Acheve.Common.Shared/Constants.cs` (`Telemetry.OncallOtlpGrpcPort = 14318`, `Telemetry.OncallGrafanaPort = 3300`)
- Modify: `src/Aspire/Acheve.AppHost/AppHost.cs`
- Modify: `docs/observability.md` (a short "On-call Grafana" section)

**Interfaces:**
- Consumes: Task 2's `ONCALL_OTLP_ENDPOINT`.
- Produces: Grafana at `http://localhost:3300`, admin user `admin`, password = parameter `oncall-grafana-admin-password` (user secret `Parameters:oncall-grafana-admin-password` of the AppHost project).

- [ ] **Step 1: Add the parameter and the container** in `AppHost.cs`, after `lgtm`:
  - The parameter is `AddParameter("oncall-grafana-admin-password", new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true)`.
  - The container is `AddContainer("lgtm-oncall", "grafana/otel-lgtm", "0.39.0")`, with:
    - HTTP endpoint `port: Constants.Telemetry.OncallGrafanaPort, targetPort: 3000, name: "grafana"`
    - OTLP gRPC endpoint on `OncallOtlpGrpcPort` → 4317
    - volume `lgtm-oncall-data`
    - env `GF_AUTH_ANONYMOUS_ENABLED=false` and `GF_SECURITY_ADMIN_PASSWORD` = parameter
    - URL display text `Grafana (on-call)`
  - Bind mounts: each file of `grafana/dashboards` except `chaos-run.json`, one by one into `/otel-lgtm/estimations-dashboards/<file>`. Enumerate with `Directory.EnumerateFiles` and skip `chaos-run.json` by name, so a new dashboard is included automatically. Also mount the same `dashboards.yaml` and `alerting.yaml` as `lgtm`.
  - Comment in the file's style: why it exists, and what it never receives.
- [ ] **Step 2: Wire the collector.** On the collector, add `.WithEnvironment("ONCALL_OTLP_ENDPOINT", lgtmOncall.GetEndpoint("otlp-grpc").Property(EndpointProperty.HostAndPort))` and `.WaitFor(lgtmOncall)`. Nothing waits on the external systems (Review Focus 5).
- [ ] **Step 3: Build.** `dotnet build src/Aspire/Acheve.AppHost`. Expected: 0 warnings, 0 errors.
- [ ] **Step 4: Run and check.** Start the AppHost (not the external one), wait for `lgtm-oncall` to be healthy, then:
  - `curl -s -o /dev/null -w '%{http_code}' localhost:3300/api/search` → `401`
  - `curl -s -u admin:$PW localhost:3300/api/search?query=` lists the five dashboards, without **Chaos run**
  - `curl -s -u admin:$PW localhost:3300/api/v1/provisioning/alert-rules | jq length` → `10`
  - PromQL `rabbitmq_detailed_queue_messages_ready` through `/api/datasources/proxy/uid/prometheus/api/v1/query` returns series (Review Focus 1)

  `$PW` comes from `dotnet user-secrets list --project src/Aspire/Acheve.AppHost`.
- [ ] **Step 5: Restart the AppHost** and repeat the admin `curl`. Expected: still `200` (Review Focus 4).
- [ ] **Step 6: Document it** in `docs/observability.md`: what it is, the port, what's filtered out and why, how to log in, and a link to E5.
- [ ] **Step 7: Commit** Tasks 2 and 3: `AppHost: on-call Grafana fed with a filtered copy of our telemetry`

### Task 4: The leak check

**Files:**
- Create: `docs/talks/E5-demo/check-blindfold.sh`

**Interfaces:**
- Consumes: Task 6's token (`GRAFANA_TOKEN` env), or admin basic auth when `ONCALL_ADMIN_PASSWORD` is set; main Grafana URL as `$1` (it has anonymous Admin).
- Produces: exit 0 only when every forbidden query is empty on 3300 **and** non-empty on the main Grafana.

- [ ] **Step 1: Write the script** (`set -euo pipefail`, needs `curl` and `jq`). Run each check against both Grafanas and print a table: check, main, on-call, OK/FAIL. The checks:
  1. metric names matching `^(chaos_|consumption_paused|traffic_)` from `.../api/v1/label/__name__/values`
  2. `service_name` label values matching `^(trafficgenerator|chaosmonkey|external-)`
  3. Loki `{service_name=~".+"} |= "Chaos "` over the last 30 min (query_range)
  4. Tempo search `{ span.chaos.injected != nil }` over the last 30 min
  5. dashboard `estimations-chaos-run` by UID (200 vs 404)

  The script fails when any on-call result is non-empty, or when the main Grafana is empty for checks 1–4 (Review Focus 2: "chaos wasn't running, the check proved nothing").
- [ ] **Step 2: Run it while chaos is active.** Start both AppHosts, the traffic generator, and `Strategy: incident`. During step 2 (estimations slow), also switch the Api to `flaky` by hand for a minute so check 4 has spans. Then:
  `ONCALL_ADMIN_PASSWORD=$PW docs/talks/E5-demo/check-blindfold.sh http://localhost:<main grafana port>`.
  Expected: all OK, exit 0. If something leaks, fix the processor in Task 2 and rerun.
- [ ] **Step 3: Commit.** `Docs: blindfold check for the on-call Grafana`

### Task 5: The `investigating-incidents` skill

**Files:**
- Create: `.claude/skills/investigating-incidents/SKILL.md`
- Create: `.claude/skills/investigating-incidents/references/system.md`
- Create: `.claude/skills/investigating-incidents/references/signals.md`
- Create: `.claude/skills/investigating-incidents/references/grafana-api.md`

**Interfaces:**
- Consumes: `GRAFANA_TOKEN` env; `http://localhost:3300`.
- Produces: the skill the demo script copies (Task 6).

- [ ] **Step 1: Load `mattpocock-skills:writing-for-agents`** and follow it for every file in this task.
- [ ] **Step 2: Write `SKILL.md`.**
  - Frontmatter: `name: investigating-incidents`. The `description` triggers on "an Estimations alert fired", "why are cases failing/slow", and "investigate this incident".
  - Body:
    - the rules, as in spec §4
    - the six-step method
    - the report template, with these headings: Symptom, Timeline, Cause, Evidence (each item with its query), Ruled out, Suggested fix, Confidence
    - one line pointing to each reference
  - Keep it under ~120 lines.
- [ ] **Step 3: Write `references/system.md`** from `docs/system.md`, `docs/messages-flow.md` and the resilience part of `docs/runtime-faults.md`:
  - services and queues
  - saga states and their waits (2 min download, ~2 min analysis and estimation, 45 s × 4 notification attempts)
  - which service calls which external system
  - the five resilience profiles with their settings

  No chaos, consumption pause or chaos monkey content.
- [ ] **Step 4: Write `references/signals.md`.**
  - Prometheus names as they appear in Grafana (underscored, with `_total`, `_seconds`, `_bucket`) for `estimations_*`, `resilience_*`, `resilience_polly_strategy_events_total`, `http_client_request_duration_seconds_*`, `http_server_request_duration_seconds_*` and `rabbitmq_detailed_*`. Check each name exists on 3300 with the label values endpoint before writing it down.
  - Loki labels (`service_name`, …) and the log lines worth searching.
  - Key span names and attributes.
  - The five dashboards (UID, question) and the ten alerts (title, what it means), taken from `alerting.yaml`.
- [ ] **Step 5: Write `references/grafana-api.md`.** Copy-paste curl recipes with `-H "Authorization: Bearer $GRAFANA_TOKEN"`, each tried once against 3300 with a Viewer token before it's written down:
  - firing alerts (`/api/prometheus/grafana/api/v1/alerts`)
  - rules (`/api/prometheus/grafana/api/v1/rules`)
  - PromQL instant and range queries (`/api/datasources/proxy/uid/prometheus/api/v1/query[_range]`)
  - LogQL (`/api/datasources/proxy/uid/loki/loki/api/v1/query_range`)
  - TraceQL search and trace by id (`/api/datasources/proxy/uid/tempo/api/search`, `/api/traces/<id>`)
  - dashboard JSON by UID (to read a panel's query)
  - annotations

  Recommend `jq` to keep output small.
- [ ] **Step 6: Grep check.** `grep -rniE "chaos|monkey|scenario|strateg" .claude/skills/investigating-incidents` → no matches.
- [ ] **Step 7: Commit.** `Claude Code: investigating-incidents skill for the on-call Grafana`

### Task 6: The demo launcher

**Files:**
- Create: `docs/talks/E5-demo/start-oncall-agent.sh`

**Interfaces:**
- Consumes: Task 3's admin password (user secrets), Task 5's skill folder.
- Produces: a folder (default `$(mktemp -d)/oncall`, or `$1`) with `.claude/skills/investigating-incidents/` and `.claude/settings.json`. The settings set `env.GRAFANA_TOKEN`, `permissions.allow` = `Bash(curl * http://localhost:3300/*)` and `Bash(jq *)`, and `permissions.deny` = `Read(/<absolute repo root>/**)` and `Grep(/<absolute repo root>/**)`. Claude Code's absolute-path rule syntax uses a leading `//`. Any other Bash command still prompts, so the presenter sees and declines it. The script prints `cd <folder> && claude`.

- [ ] **Step 1: Write the script** (`set -euo pipefail`; run from the repo root, or resolve the repo root from the script's path).
  - Read the password: `dotnet user-secrets list --project src/Aspire/Acheve.AppHost`, key `Parameters:oncall-grafana-admin-password`.
  - Look up the service account: `GET /api/serviceaccounts/search?query=oncall-agent`. Create it if missing: `POST /api/serviceaccounts {"name":"oncall-agent","role":"Viewer"}`.
  - Mint the token: `POST /api/serviceaccounts/<id>/tokens {"name":"oncall-<timestamp>","secondsToLive":28800}`.
  - Write the folder. Use `jq -n` to write the settings JSON, so the token is never interpolated into a shell string.
  - Never print the password or the token.
- [ ] **Step 2: Run it twice.** Expected:
  - Both runs succeed. The second reuses the account: one account in `/api/serviceaccounts/search` and two tokens (Review Focus 3).
  - With the token from the second folder: `POST /api/dashboards/db` → `403`; `GET /api/prometheus/grafana/api/v1/alerts` → `200`; a request without a token → `401`.
- [ ] **Step 3: Rerun the leak check with the agent's token.** `GRAFANA_TOKEN=<token> docs/talks/E5-demo/check-blindfold.sh <main url>` → all OK.
- [ ] **Step 4: Commit.** `Docs: script that hands the on-call agent a Grafana token`

### Task 7: Talk proposal E5

**Files:**
- Create: `docs/talks/E5-an-agent-on-call.md`
- Modify: `docs/talks/README.md`

- [ ] **Step 1: Write the proposal** in the format of `docs/talks/M3-dashboards-and-alerts-as-code.md`:
  - **Status:** proposal. Level Expert, area Investigation, 45 minutes. Audience: teams thinking of putting an agent on call. Takeaway: *An agent is only as good as what it can't see and the method you give it.*
  - **Outline:**
    1. why a blindfold
    2. the on-call Grafana and what it never receives
    3. a service account as the handover
    4. the skill as day-one onboarding
    5. the method
    6. the trap: a slow dependency vs our own timeout
    7. what goes wrong: blaming the obvious suspect, stopping early, guessing at the other Grafana
  - **Demo:** run `start-oncall-agent.sh`, then `incident`, then wait for **Case failure rate** and paste the alert to the agent.
  - Files and Related (M3, E3, M4).
- [ ] **Step 2: Update the index.** Add an **Investigation** row to the table in `docs/talks/README.md`, with E5 under Expert, and E5 in the Platform and SRE path after E3.
- [ ] **Step 3: Commit.** `Docs: talk proposal E5, an agent on call`

### Task 8: Rehearsal (verification)

- [ ] **Step 1: Run the whole demo once.** Start both AppHosts and the traffic generator, run `Strategy: incident`, and confirm **Case failure rate** fires on 3300 during step 3. Note the minute it fired; if it's outside step 3, adjust step 3's duration in Task 1's settings and test.
- [ ] **Step 2: Start the agent.** Run `start-oncall-agent.sh`, run `claude` in the folder it printed, and give it: *"Alert Case failure rate is firing on the on-call Grafana. Investigate."*
- [ ] **Step 3: Check the run.** Confirm it only ran `curl` to 3300, wrote a report in the template, and didn't try to read the repo. Save the report to `docs/talks/E5-demo/rehearsal-1.md`, so the talk can quote what it got right and wrong.
- [ ] **Step 4: Commit.** `Docs: first rehearsal of the on-call agent`
