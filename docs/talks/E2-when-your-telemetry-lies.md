# E2 · When your telemetry lies

**Status:** proposal.

| | |
| --- | --- |
| **Level** | Expert |
| **Area** | Metrics |
| **Length** | 40 minutes |
| **Audience** | Teams who trust their dashboards more than they should |
| **Takeaway** | Telemetry has bugs too, so test it like code. |

## Outline

1. **The invisible first increment.** Prometheus can't see a series that appears with value 1, so rare failures are undercounted (`StartCountersAtZero`).
2. **Double counts.** Retried handlers, duplicate and late messages; recording only once Rebus acks the message (`OnAck`).
3. **Idempotent requests.** A repeated POST must not count as a second case.
4. **Gauges with several instances.** Who reports the cases running when there is more than one StateHolder?
5. **Two points of view.** The clients' outcomes (traffic generator) vs the saga's outcomes: when they disagree, which one is wrong?
6. **Averages and buckets.** Averages hide the tail; bucket boundaries can hide it too.

## Demo

`duplicate-callbacks`: the saga ignores the second callback and the cases complete normally, yet **Our calls vs callbacks** shows the duplicates.

## Files

- [EstimationSaga.cs](../../src/Application/Acheve.Application.ProcessManager/Handlers/EstimationSaga.cs) (`RecordWhenHandled`)
- [EstimationMetrics.cs](../../src/Application/Acheve.Application.ProcessManager/Handlers/EstimationMetrics.cs)
- [TrafficMetrics.cs](../../src/Tools/Acheve.Tools.TrafficGenerator/TrafficMetrics.cs)
- [messages-flow.md](../messages-flow.md#7-duplicate-late-and-out-of-order-messages), [traffic.md](../traffic.md)

## Related

Builds on [M2](M2-business-metrics-in-dotnet.md).
