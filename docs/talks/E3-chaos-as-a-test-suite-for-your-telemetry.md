# E3 · Chaos as a test suite for your telemetry

**Status:** proposal.

| | |
| --- | --- |
| **Level** | Expert |
| **Area** | Big picture, resilience |
| **Length** | 50 minutes, or a longer game day |
| **Audience** | Teams who want to know whether their telemetry holds up during an incident |
| **Takeaway** | The real test of observability is whether you can tell several faults apart. |

## Outline

1. **Hypothesis-driven cases.** Baseline, the fault without the tolerance setting, the same fault with it, recovery.
2. **Three switches.** Chaos breaks them, resilience decides how hard we fight back, consumption breaks us.
3. **Steady traffic first.** Without a constant flow of cases there is nothing to see.
4. **Mark every change.** Each switch and monkey step annotated on every chart.
5. **`bad-day`.** Four faults at once, some hidden by retries. Can you name all four?

## Format idea

Give the audience the dashboards and let someone else pick the faults; the room diagnoses live.

## Files

- [chaos-scenarios.md](../chaos-scenarios.md)
- [runtime-faults.md](../runtime-faults.md)
- [Acheve.Tools.ChaosMonkey](../../src/Tools/Acheve.Tools.ChaosMonkey)
- The **Chaos run** dashboard in [grafana/dashboards](../../src/Aspire/Acheve.AppHost/grafana/dashboards)
