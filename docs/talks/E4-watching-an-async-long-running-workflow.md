# E4 · Watching an asynchronous, long-running workflow

**Status:** proposal.

| | |
| --- | --- |
| **Level** | Expert |
| **Area** | Traces, metrics |
| **Length** | 45 minutes |
| **Audience** | Developers who build sagas and process managers |
| **Takeaway** | In a saga, time and state are the main signals; request latency matters less. |

## Outline

1. **Duration per state** and **cases running per state**.
2. **Queue depth vs consumption.** Messages waiting, messages being handled, queues without consumers.
3. **Prefetch.** Why a paused worker still shows 5 messages in flight.
4. **Latency or failures?** A stalled worker costs failures once the pause outlasts the saga's waits, because each wait starts when the command is sent.
5. **Timeouts as events.** The deferred messages and the **Wait timeouts** alert.
6. **Several instances.** Two messages for one case can collide; optimistic concurrency and retries.

## Demo

The `backlog` case with the **Messaging** and **Case flow** dashboards side by side.

## Files

- [system.md](../system.md#timeouts-and-limits)
- [runtime-faults.md](../runtime-faults.md#consumption)
- [StateMetrics.cs](../../src/Application/Acheve.Application.StateHolder/StateMetrics.cs)
- [Acheve.Common.Consumption](../../src/Common/Acheve.Common.Consumption)
