using Acheve.Application.EstimationProcessor;
using Acheve.Application.EstimationProcessor.Handlers;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.Config;
using Rebus.DataBus.FileSystem;
using Rebus.Routing.TypeBased;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient("estimations", client =>
        client.BaseAddress = new Uri($"https+http://{Constants.Resources.ExternalEstimations}"))
    .AddStandardResilienceHandler();

// Automatically register all handlers from the assembly of a given type...
builder.Services.AutoRegisterHandlersFromAssemblyOf<ExternalEstimationReadyHandler>();

builder.Services.AddRebus(configure => configure
    .Options(o =>
    {
        o.LogPipeline(verbose: true);
        o.EnableDiagnosticSources();
    })
    .DataBus(d => d.StoreInFileSystem(builder.Configuration.GetStoragePath(Constants.Storage.DataBus)))
    .Transport(t => t.UseRabbitMq(
        builder.Configuration.GetMessagingConnectionString(),
        Constants.Queues.ExternalEstimations))
    .Routing(r => r.TypeBased()
        .Map<EstimationCompleted>(Constants.Queues.ProcessManager)
        .Map<UnableToEstimate>(Constants.Queues.ProcessManager)
        .Map<AwaitEstimationToBeProcessed>(Constants.Queues.ProcessManager)));

builder.Services.AddHostedService<Worker>();

builder.Build().Run();
