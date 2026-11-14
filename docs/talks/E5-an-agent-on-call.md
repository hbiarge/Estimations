# E5 · An agent on call

**Status:** proposal. The demo runs: see [Demo](#demo).

| | |
| --- | --- |
| **Level** | Expert |
| **Area** | Investigation |
| **Length** | 45 minutes |
| **Audience** | Teams thinking of putting an AI agent on call |
| **Takeaway** | An agent is only as good as what it can't see and the method you give it. |

## Outline

1. **Why a blindfold.** With chaos tooling, the answer to every incident is somewhere in Grafana: the chaos metrics, a log line, a span tag, the Chaos run dashboard. An agent that finds it has investigated nothing.
2. **The on-call Grafana.** A second LGTM fed by the same collector, with only what a real team would have: our own services, without the injected faults, the tools or the external systems' insides ([observability.md](../observability.md#on-call-grafana)). The blindfold holds because the data isn't there, not because of a rule. The leak check found a leak we hadn't thought of: the chaos monkey's own requests to `/chaos` and `/consumption`.
3. **A service account as the handover.** A Viewer token that expires after 8 hours, minted by the incident commander. Anonymous access is off, and the admin password is generated, so the agent can't guess its way in.
4. **The skill as day-one onboarding.** What a new engineer gets: the system map, what each signal means, how to query, and nothing about what broke ([investigating-incidents](../../.claude/skills/investigating-incidents/SKILL.md)).
5. **The method.** Symptom, narrow down, what changed, corroborate in each signal, rule out a rival, report. Each step has a "done when", so the agent can't stop at the first plausible story.
6. **The trap.** The estimation service gets slow. Four minutes later someone tightens our timeout. Only then do cases fail. Blaming the slow dependency is half right and fixes nothing.
7. **What goes wrong.** Blaming the obvious suspect, dating the incident from the alert instead of the first failure, and noticing that the dashboards mention chaos. Real rehearsal transcripts.

## Demo

About 25 minutes of stack time. Start it before the talk and investigate live.

1. Start both AppHosts, then **Start** on `trafficgenerator`.
2. Run `chaosmonkey` → **Strategy: incident** ([chaos-scenarios.md](../chaos-scenarios.md#incident-an-alert-for-the-on-call-agent)).
3. Check the blindfold: `GRAFANA_TOKEN=… docs/talks/E5-demo/check-blindfold.sh <main Grafana URL>` (it needs chaos to be active).
4. Wait for **Case failure rate** to fire on http://localhost:3300 (about 14 minutes into the strategy).
5. Run `docs/talks/E5-demo/start-oncall-agent.sh`, then `claude` in the folder it prints, and paste: *"Alert Case failure rate is firing on the on-call Grafana. Investigate."*
6. Compare its report with the trap: does it name our timeout change, or stop at the slow dependency?

## Files

- [E5-demo/start-oncall-agent.sh](E5-demo/start-oncall-agent.sh) and [E5-demo/check-blindfold.sh](E5-demo/check-blindfold.sh)
- [.claude/skills/investigating-incidents](../../.claude/skills/investigating-incidents)
- [otel-collector.yaml](../../src/Aspire/Acheve.AppHost/otel-collector.yaml): the `oncall` pipelines
- [AppHost.cs](../../src/Aspire/Acheve.AppHost/AppHost.cs): the `lgtm-oncall` container
- The `incident` strategy in [the chaos monkey's settings](../../src/Tools/Acheve.Tools.ChaosMonkey/appsettings.json)

## Related

Builds on [M3](M3-dashboards-and-alerts-as-code.md) (the alerts it starts from), [E3](E3-chaos-as-a-test-suite-for-your-telemetry.md) (the chaos it hides) and [M4](M4-resilience-you-can-see.md) (the resilience it has to reason about).
