---
name: investigating-incidents
description: Investigate an incident on the Estimations system through its on-call Grafana API. Use when an Estimations alert fired, when cases are failing or slow, or when asked to investigate an incident or find a root cause.
---

# Investigating incidents

You are on call for **Estimations**, a system that turns a client's images into an estimation through a saga, four workers and four external systems. An alert fired or someone reported a symptom. Find the **cause**, prove it, and report it.

## Rules

- Your only source is the on-call Grafana at `http://localhost:3300`, as it would be for an engineer paged at night: the telemetry, not the source code or design docs. Every request carries `-H "Authorization: Bearer $GRAFANA_TOKEN"`. The token is a Viewer: you read, you change nothing.
- Work from evidence in the telemetry. Every claim in your report cites the query that shows it.
- Write down what you couldn't check, and why.

## Method

Work the steps in order. Each one ends on its criterion.

1. **Symptom.** Read the firing alerts and their rules. Done when you can say what clients feel, how much, and since when (the first minute it was abnormal, not when the alert fired).
2. **Narrow down.** Split the failures by outcome, failure reason, saga state and service. Done when you can name the stage of the saga that fails and the service at that stage.
3. **What changed.** Look for every change near the start time: traffic, a dependency's latency or errors, queues, restarts, our own config. Put each on a **timeline** to the minute. Done when you know which change lines up with the *start of the symptom*, not just with the incident window.
4. **Corroborate.** Find one piece of evidence in each signal: a metric, a log line, a trace. All three must tell the same story. Done when they agree, or when you can explain why one of them doesn't.
5. **Rule out a rival.** Name the most tempting other explanation and show with data why it isn't the cause, or why it's only part of it.
6. **Report.** Use the template below.

The cause is the change that **starts** the symptom. When several things changed, line each one up against the first failure.

## Report

```markdown
## Incident report: <alert or symptom>

**Symptom.** What clients feel, how much, since when.
**Timeline.** One line per change and per symptom, with the minute.
**Cause.** One or two sentences.
**Evidence.** Each item: what it shows, then the query (PromQL, LogQL or TraceQL, or the API path).
**Ruled out.** Each rival explanation and the data against it.
**Suggested fix.** What to change, and what to watch to confirm it worked.
**Confidence.** High, medium or low, and what would raise it.
**Not checked.** What you couldn't verify.
```

## References

- [system.md](references/system.md): the services, the saga's stages and waits, who calls which external system, and the resilience profiles. Read it before step 2.
- [signals.md](references/signals.md): the metric names and labels, log searches, span names and attributes, dashboards and alerts.
- [grafana-api.md](references/grafana-api.md): tried and tested curl recipes for alerts, PromQL, LogQL, TraceQL and dashboards.
