# Estimations - Sample messaging demo

Implements a complex system using messaging, a process manager, and communication with external systems over HTTP and gRPC.

The system runs locally with [Aspire](https://aspire.dev):

- **RabbitMQ** carries the messages between services ([Rebus](https://github.com/rebus-org/Rebus)).
- **PostgreSQL** stores the process manager's saga data and deferred messages, and the state holder's copy of every case's state.
- A **local shared folder** (`.data/`, ignored by git) stores the data bus attachments (images and estimations).
- **OpenTelemetry** sends logs, traces and metrics from every service to an OpenTelemetry Collector, which sends a copy to the Aspire dashboard and another to **Grafana LGTM** (Tempo, Loki and Prometheus in one container).

[docs/](docs/README.md) explains the system (services, timeouts, business decisions), the message flow, how to observe it, and how to make it misbehave on purpose.

## Run the sample

### Required dependencies

You must have installed:

- .NET 11 SDK (the version is pinned in [global.json](global.json))
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

The Aspire dashboard is the quickest way to look at a run. For more, open **Grafana** from the `lgtm` resource link (user `admin`, password `admin`): traces in Tempo, logs in Loki, metrics in Prometheus, and the dashboards and alert rules in the `Estimations` folder. [docs/observability.md](docs/observability.md) explains them.

### Traffic and faults

- Press **Start** on `trafficgenerator` to send a steady stream of cases: [docs/traffic.md](docs/traffic.md).
- Break a dependency (chaos), change how much the callers tolerate (resilience) or stall a worker (consumption), all at run time from the resources' **Commands**: [docs/runtime-faults.md](docs/runtime-faults.md).
- Run a guided case from `chaosmonkey`'s **Commands**: [docs/chaos-scenarios.md](docs/chaos-scenarios.md).

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

## Repository layout

| Folder | What it holds |
| --- | --- |
| [src/Aspire](src/Aspire) | The two AppHosts, the service defaults (OpenTelemetry, health checks) and the dashboard commands for chaos, resilience and consumption. The Grafana dashboards and alert rules live in [Acheve.AppHost/grafana](src/Aspire/Acheve.AppHost/grafana). |
| [src/Application](src/Application) | The seven application services: Api, ProcessManager, StateHolder, ImageDownloads, ExternalImageProcessor, EstimationProcessor and CallbackNotifier. |
| [src/External](src/External) | The four fake external systems (images, image processing, estimations and the client's notification endpoint). |
| [src/Common](src/Common) | The shared messages, constants and signing code, and the chaos, resilience and consumption libraries. |
| [src/Tools](src/Tools) | The traffic generator and the chaos monkey. |
| [src/Protos](src/Protos) | The gRPC contract of the state query. |
| [test](test) | The tests, in folders that mirror `src`. |
| [docs](docs/README.md) | The system, message flow, observability, traffic, runtime faults and chaos scenarios guides, and [talks about observability](docs/talks/README.md) built on this sample. |

## Tests

```bash
dotnet test --solution Estimations.slnx
```
