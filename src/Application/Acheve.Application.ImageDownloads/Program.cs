using Acheve.Application.ImageDownloads.Handlers;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.Config;
using Rebus.DataBus.FileSystem;
using Rebus.Routing.TypeBased;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient("images")
    .WithPeerService(Constants.Resources.ExternalImages)
    .AddStandardResilienceHandler();

// Automatically register all handlers from the assembly of a given type...
builder.Services.AutoRegisterHandlersFromAssemblyOf<DownloadImageHandler>();

builder.Services.AddRebus(configure => configure
    .Options(o =>
    {
        o.LogPipeline(verbose: true);
        o.EnableDiagnosticSources();
    })
    .DataBus(d => d.StoreInFileSystem(builder.Configuration.GetStoragePath(Constants.Storage.DataBus)))
    .Transport(t => t.UseRabbitMq(
        builder.Configuration.GetMessagingConnectionString(),
        Constants.Queues.ImageDownloads))
    .Routing(r => r.TypeBased()
        .Map<ImageDownloaded>(Constants.Queues.ProcessManager)
        .Map<UnableToDownloadImage>(Constants.Queues.ProcessManager)));

builder.Build().Run();
