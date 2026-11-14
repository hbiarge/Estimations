# E1 · Sampling traces that last 11 minutes

**Status:** proposal.

| | |
| --- | --- |
| **Level** | Expert |
| **Area** | Tooling and platform, traces |
| **Length** | 45 minutes |
| **Audience** | People who run collectors in production and pay for trace volume |
| **Takeaway** | Tail sampling misjudges exactly the traces you most want to keep. |

## Outline

1. **Where each strategy decides.** Head sampling (SDK), probabilistic (collector, trace ID hash), tail sampling (collector, after `decision_wait`).
2. **The decision travels.** The `sampled` flag in `traceparent` survives every hop here: HTTP and gRPC, Rebus headers, deferred timeouts in PostgreSQL, callback URLs.
3. **Tail sampling vs long traces.** `decision_wait` is 30 s; a case that times out has its error minutes later, after the decision.
4. **Late spans.** Judged again as a partial trace, or following a decision cache: either way, half a case.
5. **What it would take.** A `decision_wait` longer than the worst case, and a `loadbalancing` exporter routing by trace ID.
6. **Links to dropped traces.** The callback's request trace has its own sampling decision.
7. **The alternative.** Head or probabilistic sampling for volume; find bad cases with metrics and logs, which are never sampled; decide from the outcome after the case ends.

## Demo

Add `tail_sampling` to the collector live and show half-traces of timed-out cases in Tempo.

## Files

- [otel-collector.yaml](../../src/Aspire/Acheve.AppHost/otel-collector.yaml)
- [observability.md](../observability.md#sampling-and-long-traces)

## Related

Builds on [M1](M1-tracing-across-a-bus-and-systems-you-dont-own.md).
