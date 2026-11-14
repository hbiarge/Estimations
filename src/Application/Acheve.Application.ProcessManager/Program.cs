using Acheve.Application.ProcessManager;
using Acheve.Application.ProcessManager.Handlers;
using Acheve.Common.Consumption;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Npgsql;
using Rebus.Config;
using Rebus.DataBus.FileSystem;
using Rebus.Routing.TypeBased;
using Rebus.Sagas.Exclusive;
using Rebus.Sagas.Idempotent;

var builder = WebApplication.CreateBuilder(args);

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
        Constants.Queues.ProcessManager)
        .Prefetch(Constants.Queues.Prefetch))
    .Routing(r => r.TypeBased()
        .Map<DownloadImage>(Constants.Queues.ImageDownloads)
        .Map<AnalyseImage>(Constants.Queues.ImageProcess)
        .Map<EstimateCase>(Constants.Queues.ExternalEstimations)
        .Map<NotifyEstimation>(Constants.Queues.CallbackNotifications)
        .Map<NotifyEstimationFailure>(Constants.Queues.CallbackNotifications)));
// EstimationStateChanged, CaseCompleted and CaseFailed are events: the saga publishes them to RabbitMQ's topic
// exchange and doesn't know who subscribes. Commands are still sent to the one queue above that handles them.

// Deletes old data bus attachments (DataBus:Retention, 24 hours by default).
builder.Services.AddHostedService<DataBusSweeper>();

// Pause and resume taking messages at run time (/consumption).
builder.AddConsumption();

// A web app only for the health checks and the /consumption endpoints; the work comes from the queue.
var app = builder.Build();

app.MapDefaultEndpoints();

app.UseConsumption();

app.Run();
