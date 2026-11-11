using Acheve.Application.ExternalImageProcessor.Handlers;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.Config;
using Rebus.DataBus.FileSystem;
using Rebus.Routing.TypeBased;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient("process", client =>
        client.BaseAddress = new Uri($"https+http://{Constants.Resources.ExternalImageProcess}"))
    .WithPeerService(Constants.Resources.ExternalImageProcess)
    .AddStandardResilienceHandler();

// Signs and checks the callback URLs given to the external systems.
builder.Services.AddSingleton(CallbackSigner.FromConfiguration(builder.Configuration));

// Automatically register all handlers from the assembly of a given type...
builder.Services.AutoRegisterHandlersFromAssemblyOf<AnalyseImageHandler>();

builder.Services.AddRebus(configure => configure
    .Options(o =>
    {
        o.LogPipeline(verbose: true);
        o.EnableDiagnosticSources();
    })
    .DataBus(d => d.StoreInFileSystem(builder.Configuration.GetStoragePath(Constants.Storage.DataBus)))
    .Transport(t => t.UseRabbitMq(
        builder.Configuration.GetMessagingConnectionString(),
        Constants.Queues.ImageProcess))
    .Routing(r => r.TypeBased()
        .Map<UnableToAnalyseImage>(Constants.Queues.ProcessManager)
        .Map<ImageAnalysisAccepted>(Constants.Queues.ProcessManager)));

builder.Build().Run();
