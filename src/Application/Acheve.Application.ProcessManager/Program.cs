using Acheve.Application.ProcessManager;
using Acheve.Application.ProcessManager.Handlers;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Npgsql;
using Rebus.Config;
using Rebus.DataBus.FileSystem;
using Rebus.Routing.TypeBased;
using Rebus.Sagas.Exclusive;
using Rebus.Sagas.Idempotent;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// Saga data and deferred messages live in PostgreSQL. Rebus creates the tables on startup.
var databaseConnectionString = new NpgsqlConnectionStringBuilder(
    builder.Configuration.GetRequiredConnectionString(Constants.Resources.ProcessManagerDatabase))
{
    // Npgsql's default (100) can exhaust the server's connections; keep the pool small.
    MaxPoolSize = 30
}.ConnectionString;

// Case-level metrics recorded by the saga.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<EstimationMetrics>();
builder.Services.AddSingleton<ICaseAttachments, CaseAttachments>();

// Automatically register all handlers from the assembly of a given type...
builder.Services.AutoRegisterHandlersFromAssemblyOf<EstimationSaga>();

builder.Services.AddRebus(configure => configure
    .Options(o =>
    {
        o.LogPipeline(verbose: true);
        o.EnableDiagnosticSources();

        // The saga data is saved before its outgoing messages are sent. If sending fails, the incoming
        // message is retried with the saga already updated. Idempotent sagas save the outgoing messages
        // with the saga data, so the retry resends them instead of running the handler again.
        // (Rebus.PostgreSql's outbox can't give this guarantee: inside a handler it commits the
        // transaction before it saves the outgoing messages.)
        o.EnableIdempotentSagas();
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
        .Map<DownloadImage>(Constants.Queues.ImageDownloads)
        .Map<AnalyseImage>(Constants.Queues.ImageProcess)
        .Map<EstimateCase>(Constants.Queues.ExternalEstimations)
        .Map<NotifyEstimation>(Constants.Queues.CallbackNotifications)
        .Map<NotifyEstimationFailure>(Constants.Queues.CallbackNotifications)
        .Map<EstimationStateChanged>(Constants.Queues.StateHolder)));

// Deletes old data bus attachments (DataBus:Retention, 24 hours by default).
builder.Services.AddHostedService<DataBusSweeper>();

builder.Build().Run();
