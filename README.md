# Estimations - Sample messaging demo

Implements a complex system using messaging, a process manager, external comunication with Http and gRPC.

The system runs locally with [Aspire](https://aspire.dev):

- **RabbitMQ** carries the messages between services ([Rebus](https://github.com/rebus-org/Rebus)).
- **PostgreSQL** stores the process manager's saga data and deferred messages.
- A **local shared folder** (`.data/`, ignored by git) stores the data bus attachments (images and estimations).
- **OpenTelemetry** sends logs, traces and metrics from every service to the Aspire dashboard.

[docs/messages-flow.md](docs/messages-flow.md) explains the whole flow: the services, every message, the case states and each failure scenario, with diagrams.

## Run the sample

### Required dependencies

You must have installed:

- .NET 10 SDK
- Docker Desktop (or another container runtime supported by Aspire). Aspire starts RabbitMQ and PostgreSQL in containers.
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

## Starting a Saga

The API requires a token from the Duende demo identity server. [requests.http](requests.http) has the full flow (get a token, start a saga and query its state).

Do a POST call to https://localhost:5000/Estimations with this Json body

``` json
{
	"CallBackUri": "https://localhost:5004/Notification",
	"imageUrls": [
	  "https://localhost:5001/images/image01.png",
	  "https://localhost:5001/images/image02.jpg"
	]
}
```

The response returns a ticket. You can query the ticket status doing a GET call to https://localhost:5000/Estimations/<ticket-received>

## Tests

```bash
dotnet test --solution Estimations.slnx
```
