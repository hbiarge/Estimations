using Acheve.Application.CallbackNotifier;
using Acheve.Application.CallbackNotifier.Handlers;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.Config;
using Rebus.DataBus.FileSystem;
using Rebus.Routing.TypeBased;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// The client's callback URL; in this sample it is always the fake notifications system.
builder.Services.AddHttpClient("notifications")
    .WithPeerService(Constants.Resources.ExternalNotifications)
    .AddStandardResilienceHandler();

// Posts the result, signed when ClientCallbacks:SigningKey is set.
builder.Services.AddTransient<ClientNotifier>();

// Automatically register all handlers from the assembly of a given type...
builder.Services.AutoRegisterHandlersFromAssemblyOf<NotifyEstimationHandler>();

builder.Services.AddRebus(configure => configure
    .Options(o =>
    {
        o.LogPipeline(verbose: true);
        o.EnableDiagnosticSources();
    })
    .DataBus(d => d.StoreInFileSystem(builder.Configuration.GetStoragePath(Constants.Storage.DataBus)))
    .Transport(t => t.UseRabbitMq(
        builder.Configuration.GetMessagingConnectionString(),
        Constants.Queues.CallbackNotifications))
    .Routing(r => r.TypeBased()
        .Map<NotificationCompleted>(Constants.Queues.ProcessManager)
        .Map<UnableToNotify>(Constants.Queues.ProcessManager)));

builder.Build().Run();
