# W1 · Instrument, break, diagnose

**Status:** proposal.

| | |
| --- | --- |
| **Format** | Hands-on workshop |
| **Length** | 3 hours |
| **Audience** | .NET developers with a laptop that runs Docker |
| **Takeaway** | You add telemetry, break the system and find the fault yourself. |

## Outline

1. **Run it** (from [I2](I2-observability-from-f5-with-aspire.md)). Start both AppHosts and follow a case.
2. **Trace it** (from [M1](M1-tracing-across-a-bus-and-systems-you-dont-own.md)). Add one span attribute and find it in Tempo.
3. **Measure it** (from [M2](M2-business-metrics-in-dotnet.md)). Add one business metric and a dashboard panel for it.
4. **Break it** (from [E3](E3-chaos-as-a-test-suite-for-your-telemetry.md)). Diagnose a chaos strategy you haven't seen.

## Preparation

The two AppHosts, the traffic generator and the chaos monkey make it work on any laptop. Follow the demo checklist in [I1](I1-do-you-know-what-your-system-is-doing.md#demo-preparation-checklist).
