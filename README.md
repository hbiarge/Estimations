# Estimations - Sample messaging demo

Implements a complex system using messaging, a process manager, external comunication with Http and gRPC.

The system runs locally with [Aspire](https://aspire.dev):

- **RabbitMQ** carries the messages between services ([Rebus](https://github.com/rebus-org/Rebus)).
- **PostgreSQL** stores the process manager's saga data and deferred messages.
- A **local shared folder** (`.data/`, ignored by git) stores the data bus attachments (images and estimations).
- **OpenTelemetry** sends logs, traces and metrics from every service to an OpenTelemetry Collector, which sends a copy to the Aspire dashboard and another to **Grafana LGTM** (Tempo, Loki and Prometheus in one container).

[docs/messages-flow.md](docs/messages-flow.md) explains the whole flow: the services, every message, the case states and each failure scenario, with diagrams.

## Run the sample

### Required dependencies

You must have installed:

- .NET 10 SDK
- Docker Desktop (or another container runtime supported by Aspire). Aspire starts RabbitMQ, PostgreSQL, the OpenTelemetry Collector and Grafana LGTM in containers.
- A trusted HTTPS development certificate: `dotnet dev-certs https --trust`

### Run the sample locally

The sample has two Aspire AppHosts, so the fake external systems run apart from the application, like systems owned by someone else:

- **Acheve.AppHost** runs the application services, RabbitMQ, PostgreSQL, the OpenTelemetry Collector and Grafana LGTM. Dashboard: https://localhost:17000.
- **Acheve.External.AppHost** runs the four fake external systems on https://localhost:5001-5004, with their own collector. Dashboard: https://localhost:17100.

Start each one in its own terminal, in any order:

```bash
dotnet run --project src/Aspire/Acheve.External.AppHost
```

```bash
dotnet run --project src/Aspire/Acheve.AppHost
```

Each console prints its dashboard URL with a login token. The application's dashboard shows every application service, their logs, traces and metrics, and links to the RabbitMQ management UI. The external systems show up there only as URLs with a health check: if their AppHost isn't running they show as unhealthy and the cases fail the way they would with a real outage.

The repository also pins the Aspire CLI as a local dotnet tool. Restore it once, then run the AppHosts with it from the repository root (`aspire run` alone starts the application's AppHost):

```bash
dotnet tool restore
```

```bash
dotnet aspire run --apphost src/Aspire/Acheve.External.AppHost/Acheve.External.AppHost.csproj
```

```bash
dotnet aspire run
```

### Observability

The Aspire dashboard is the quickest way to look at a run. It keeps the telemetry only in memory.

For more, open **Grafana** from the `lgtm` resource link in the dashboard (user `admin`, password `admin`). It has:

- **Tempo**: traces, including each saga's messages across RabbitMQ.
- **Loki**: logs, linked to their traces.
- **Prometheus**: metrics, including the RabbitMQ queue metrics the collector scrapes.
- **Dashboards and alert rules** for the whole system, in the `Estimations` folder. [docs/observability.md](docs/observability.md) explains each one.

Grafana gets the telemetry of both AppHosts. Every service tags it with `service.namespace`: `application` for the application services and `external` for the fake external systems (in Prometheus the `job` label becomes `application/<service>` or `external/<service>`). Filter on it to see one side only. The fakes don't join the application's traces, so a case's trace holds only application spans. The application tags its calls to them with `peer.service`, so Tempo's service map and the Edges dashboard still show each external system by name.

The external systems' collector sends to Grafana through a fixed host port (14317), so their telemetry reaches Grafana only while the application's AppHost is running.

The `lgtm-data` volume keeps the telemetry between runs. The collector settings are in [otel-collector.yaml](src/Aspire/Acheve.AppHost/otel-collector.yaml) (application) and [otel-collector.yaml](src/Aspire/Acheve.External.AppHost/otel-collector.yaml) (external systems).

### Chaos

The fake external systems and the Api's callbacks can fail, slow down, or lose and duplicate callbacks on demand. Switch a scenario (`flaky`, `slow`, `down`, `lost-callbacks`, `duplicate-callbacks`, back to `healthy`) from a resource's **Commands** in the Aspire dashboard. Or press **Start** on the `chaosmonkey` resource to switch them over time from a timeline (or at random). [docs/chaos.md](docs/chaos.md) explains each scenario, the `/chaos` endpoints and the chaos monkey.

### Traffic

The `trafficgenerator` resource in the application's dashboard asks for estimations over time and follows each case to its end. Press **Start** on it to begin. Combined with the chaos scenarios, it shows how the system behaves under load and failures. [docs/traffic.md](docs/traffic.md) explains its settings and metrics.

## Starting a Saga

The API requires a token from the Duende demo identity server. [requests.http](requests.http) has the full flow (get a token, start a saga and query its state).

Do a POST call to https://localhost:5000/estimations with this Json body

``` json
{
	"CallBackUri": "https://localhost:5004/Notification",
	"imageUrls": [
	  "https://localhost:5001/images/image01.png",
	  "https://localhost:5001/images/image02.jpg"
	]
}
```

The response returns a ticket. You can query the ticket status doing a GET call to https://localhost:5000/estimations/<ticket-received>. It returns the state and, once the case has a result, `result` (`Success` or `Failure`), the `estimation` or the `failureReason`, and the `skippedImages` (images the estimation was made without). A ticket the client doesn't own answers `404`. The estimation stays readable for 24 hours.

Send an `Idempotency-Key` header with the POST to make it safe to repeat: the same key from the same client returns the same ticket instead of starting a second case.

## Tests

```bash
dotnet test --solution Estimations.slnx
```
