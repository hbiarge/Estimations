# M4 · Resilience you can see: Polly meets OpenTelemetry

**Status:** proposal.

| | |
| --- | --- |
| **Level** | Mid |
| **Area** | Resilience |
| **Length** | 45 minutes |
| **Audience** | Developers who added `AddStandardResilienceHandler()` and never looked at what it does |
| **Takeaway** | Each resilience strategy has a cost, and you can only judge it if you can see it. |

## Outline

1. **Profiles you can switch at run time.** `standard`, `none`, `retry-only`, `fail-fast`, `hedging`.
2. **Seeing Polly.** `resilience_polly_strategy_events_total` by event, and the profile in force.
3. **What retries buy** (`retries`): a flaky system hidden from the cases.
4. **What retries cost** (`retry-storm`): against an outage every call becomes four.
5. **Cutting the slow tail** (`hedging`): the first good answer wins, at the cost of extra calls.
6. **Resilience that hurts** (`timeouts`): a slow system treated as failed.
7. **What HTTP resilience can't see** (`callbacks`): a callback that never comes; only the saga's waits catch it.

## Demo

The chaos monkey runs two cases while the **Chaos run** dashboard shows the fault without and with the tolerance setting, side by side.

## Files

- [Acheve.Common.Resilience](../../src/Common/Acheve.Common.Resilience)
- [runtime-faults.md](../runtime-faults.md#resilience)
- [chaos-scenarios.md](../chaos-scenarios.md)
