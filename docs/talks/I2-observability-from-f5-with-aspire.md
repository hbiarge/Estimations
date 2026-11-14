# I2 · Observability from F5: Aspire for .NET developers

**Status:** proposal.

| | |
| --- | --- |
| **Level** | Introductory |
| **Area** | Tooling and platform |
| **Length** | 30–40 minutes |
| **Audience** | .NET developers who have not set up telemetry yet |
| **Takeaway** | Telemetry on by default costs you one `AddServiceDefaults()` call. |

## Outline

1. **ServiceDefaults.** One project every service references: logs, traces, metrics, health checks and service discovery from one call.
2. **The AppHost.** The system described in C#. Aspire injects `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_SERVICE_NAME` and friends, so the services carry no telemetry configuration.
3. **The dashboard.** An in-memory OTLP backend: structured logs, traces and metrics with no setup. Good for the inner loop, not for production.
4. **A collector in the middle.** `WithAppForwarding()` sends every app to the collector instead of the dashboard; the collector fans out to the dashboard and to Grafana.
5. **Systems you don't own.** A second AppHost for the external systems, seen from the application only as URLs with a health check.
6. **Resource Commands.** Dev-time controls on the dashboard: chaos, resilience and consumption switches.

## Demo

`aspire run`, send one POST from [requests.http](../../requests.http) and follow its trace in the dashboard. Then stop the external AppHost and watch its health checks go red.

## Files

- [Extensions.cs](../../src/Aspire/Acheve.ServiceDefaults/Extensions.cs)
- [AppHost.cs](../../src/Aspire/Acheve.AppHost/AppHost.cs)
- [Acheve.External.AppHost](../../src/Aspire/Acheve.External.AppHost)

## Related

Section 4 of [I1](I1-do-you-know-what-your-system-is-doing.md) is the short version of this talk.
