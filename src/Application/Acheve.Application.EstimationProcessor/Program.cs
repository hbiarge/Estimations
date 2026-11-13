using Acheve.Application.EstimationProcessor.Handlers;
using Acheve.Common.Consumption;
using Acheve.Common.Messages;
using Acheve.Common.Resilience;
using Acheve.Common.Shared;
using Rebus.Config;
using Rebus.DataBus.FileSystem;
using Rebus.Routing.TypeBased;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient("estimations", client =>
        client.BaseAddress = new Uri($"https+http://{Constants.Resources.ExternalEstimations}"))
    .WithPeerService(Constants.Resources.ExternalEstimations)
    .AddSwitchableResilienceHandler();

// Signs and checks the callback URLs given to the external systems.
builder.Services.AddSingleton(CallbackSigner.FromConfiguration(builder.Configuration));

// Automatically register all handlers from the assembly of a given type...
builder.Services.AutoRegisterHandlersFromAssemblyOf<EstimateCaseHandler>();

builder.Services.AddRebus(configure => configure
    .Options(o =>
    {
        o.LogPipeline(verbose: true);
        o.EnableDiagnosticSources();
    })
    .DataBus(d => d.StoreInFileSystem(builder.Configuration.GetStoragePath(Constants.Storage.DataBus)))
    .Transport(t => t.UseRabbitMq(
        builder.Configuration.GetMessagingConnectionString(),
        Constants.Queues.ExternalEstimations)
        .Prefetch(Constants.Queues.Prefetch))
    .Routing(r => r.TypeBased()
        .Map<UnableToEstimate>(Constants.Queues.ProcessManager)
        .Map<EstimationRequestAccepted>(Constants.Queues.ProcessManager)));

// Retries, hedging, circuit breaker and timeouts of the HttpClient above, switched at run time (/resilience).
builder.AddResilience();

// Pause and resume taking messages at run time (/consumption).
builder.AddConsumption();

// A web app only for the health checks and the /resilience endpoints; the work comes from the queue.
var app = builder.Build();

app.MapDefaultEndpoints();

app.UseResilience();

app.UseConsumption();

app.Run();
