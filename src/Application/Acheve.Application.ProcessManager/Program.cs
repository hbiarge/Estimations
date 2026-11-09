using Acheve.Application.ProcessManager;
using Acheve.Application.ProcessManager.Handlers;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Npgsql;
using Rebus.Config;
using Rebus.DataBus.FileSystem;
using Rebus.Routing.TypeBased;
using Rebus.Sagas.Exclusive;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// Saga data and deferred messages live in PostgreSQL. Rebus creates the tables on startup.
var databaseConnectionString = new NpgsqlConnectionStringBuilder(
    builder.Configuration.GetRequiredConnectionString(Constants.Resources.ProcessManagerDatabase))
{
    // Npgsql's default (100) can exhaust the server's connections; keep the pool small.
    MaxPoolSize = 30
}.ConnectionString;

// Automatically register all handlers from the assembly of a given type...
builder.Services.AutoRegisterHandlersFromAssemblyOf<EstimationSaga>();

builder.Services.AddRebus(configure => configure
    .Options(o =>
    {
        o.LogPipeline(verbose: true);
        o.EnableDiagnosticSources();
    })
    .Sagas(s =>
    {
        s.EnforceExclusiveAccess();
        s.StoreInPostgres(databaseConnectionString, Constants.Tables.SagaData, Constants.Tables.SagaIndex);
    })
    // RabbitMQ has no built-in delayed delivery, so deferred messages wait here until they are due.
    .Timeouts(t => t.StoreInPostgres(databaseConnectionString, Constants.Tables.Timeouts))
    .DataBus(d => d.StoreInFileSystem(builder.Configuration.GetStoragePath(Constants.Storage.DataBus)))
    .Transport(t => t.UseRabbitMq(
        builder.Configuration.GetMessagingConnectionString(),
        Constants.Queues.ProcessManager))
    .Routing(r => r.TypeBased()
        .Map<ImageUrlReceived>(Constants.Queues.ImageDownloads)
        .Map<ImageReady>(Constants.Queues.ImageProcess)
        .Map<ExternalEstimationReady>(Constants.Queues.ExternalEstimations)
        .Map<EstimationReady>(Constants.Queues.CallbackNotifications)
        .Map<EstimationError>(Constants.Queues.CallbackNotifications)
        .Map<EstimationStateChanged>(Constants.Queues.StateHolder)));

builder.Services.AddHostedService<Worker>();

builder.Build().Run();
