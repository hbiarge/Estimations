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

Start the Aspire AppHost:

```bash
dotnet run --project src/Aspire/Acheve.AppHost
```

The console prints the dashboard URL (https://localhost:17000 with a login token). The dashboard shows every service, their logs, traces and metrics, and links to the RabbitMQ management UI.

The repository also pins the Aspire CLI as a local dotnet tool. Restore it once, then run the AppHost with it from the repository root:

```bash
dotnet tool restore
dotnet aspire run
```

### Observability

The Aspire dashboard is the quickest way to look at a run. It keeps the telemetry only in memory.

For more, open **Grafana** from the `lgtm` resource link in the dashboard (user `admin`, password `admin`). It has:

- **Tempo**: traces, including each saga's messages across RabbitMQ.
- **Loki**: logs, linked to their traces.
- **Prometheus**: metrics, including the RabbitMQ queue metrics the collector scrapes.
- **Dashboards and alert rules** for the whole system, in the `Estimations` folder. [docs/observability.md](docs/observability.md) explains each one.

The `lgtm-data` volume keeps the telemetry between runs. The collector settings are in [otel-collector.yaml](src/Aspire/Acheve.AppHost/otel-collector.yaml).

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

The response returns a ticket. You can query the ticket status doing a GET call to https://localhost:5000/estimations/<ticket-received>

## Tests

```bash
dotnet test --solution Estimations.slnx
```
