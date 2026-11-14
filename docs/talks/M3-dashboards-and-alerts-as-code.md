# M3 · Dashboards and alerts as code

**Status:** proposal.

| | |
| --- | --- |
| **Level** | Mid |
| **Area** | Tooling and platform |
| **Length** | 40 minutes |
| **Audience** | Teams with dashboards nobody opens and alerts nobody trusts |
| **Takeaway** | Every dashboard answers one question, and every alert fires on a symptom a user would feel. |

## Outline

1. **One question per dashboard.** The six dashboards and the question each one answers.
2. **Symptoms vs causes.** Users feel symptoms; causes are what you investigate afterwards.
3. **Thresholds from the design.** **Stuck case** fires at 20 minutes because the worst case ends in about 11 ([system.md](../system.md#timeouts-and-limits)).
4. **Provisioned from the repo.** Dashboards and alert rules mounted into the container and reviewed like code.
5. **Testing alerts.** [chaos-scenarios.md](../chaos-scenarios.md) says which alerts each case should fire: run the case and check.
6. **Gotchas.** Changes made in the UI aren't saved; an invalid rule stops Grafana from starting.

## Demo

Run `backlog`: **Queue backlog** fires, then **Stuck case**, then both resolve after the drain.

## Files

- [grafana/dashboards](../../src/Aspire/Acheve.AppHost/grafana/dashboards)
- [grafana/provisioning/alerting.yaml](../../src/Aspire/Acheve.AppHost/grafana/provisioning/alerting.yaml)
- [observability.md](../observability.md#dashboards)

## Related

Expands section 8 of [I1](I1-do-you-know-what-your-system-is-doing.md).
