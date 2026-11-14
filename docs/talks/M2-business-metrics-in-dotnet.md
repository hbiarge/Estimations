# M2 · Metrics that mean something: business metrics in .NET

**Status:** proposal.

| | |
| --- | --- |
| **Level** | Mid |
| **Area** | Metrics |
| **Length** | 45 minutes |
| **Audience** | Developers who only have the metrics that came for free |
| **Takeaway** | "Cases finished by result" beats CPU%, and cardinality is a budget. |

## Outline

1. **`IMeterFactory` and the instrument types.** Counter, histogram, gauge, up-down counter.
2. **Buckets that fit the domain.** Histogram boundaries for a process that takes from 25 s to 11 minutes.
3. **Cardinality.** Low-cardinality tags kept as constants; IDs go on spans and logs, never on metric labels.
4. **Observable gauges.** "Cases running per state" and "age of the oldest running case", the best single signal that something is stuck.
5. **From .NET to Prometheus names.** Where `_total` and `_seconds` come from.
6. **The silent failure.** Forget `AddMeter(...)` and your instrumentation records nothing.

## Demo

Run the `timeouts` chaos case and read the time per state on the **Case flow** dashboard.

## Files

- [EstimationMetrics.cs](../../src/Application/Acheve.Application.ProcessManager/Handlers/EstimationMetrics.cs)
- [StateMetrics.cs](../../src/Application/Acheve.Application.StateHolder/StateMetrics.cs)
- [Constants.cs](../../src/Common/Acheve.Common.Shared/Constants.cs)
- [observability.md](../observability.md#case-metrics)

## Related

Expands section 5 of [I1](I1-do-you-know-what-your-system-is-doing.md). Leads into [E2](E2-when-your-telemetry-lies.md).
