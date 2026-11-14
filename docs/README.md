# Docs

Read them in this order.

**The system and how we observe it**

1. [system.md](system.md): what the system does, the services, every timeout and limit, and the business decisions behind them.
2. [messages-flow.md](messages-flow.md): a case message by message, the case states, the happy path and the failures the design handles on its own.
3. [observability.md](observability.md): where the telemetry goes, the case metrics, the dashboards, the alerts and how to follow one case.

**Making it misbehave**

4. [traffic.md](traffic.md): the traffic generator, why it exists and how to set it up. Run it during every experiment.
5. [runtime-faults.md](runtime-faults.md): the three run-time switches (chaos breaks dependencies, resilience sets how much the callers tolerate, consumption stalls our own workers) and the chaos monkey that drives them.
6. [chaos-scenarios.md](chaos-scenarios.md): the cases we chose, each with the situation, what happens, the expected outcome and how to see it.

[talk/](talk/observability-talk-blueprint.md) holds the blueprint of a talk built on this sample.
