using Acheve.Application.StateHolder;
using Acheve.Application.StateHolder.Handlers;
using Acheve.Application.StateHolder.Services;
using Acheve.Common.Shared;
using Npgsql;
using Rebus.Config;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<State>();
builder.Services.AddSingleton<StateMetrics>();

// The states are also kept in PostgreSQL. StateMaintenance loads them at startup and forgets the cases
// that finished long ago (State:Retention).
builder.Services.AddSingleton(NpgsqlDataSource.Create(
    builder.Configuration.GetRequiredConnectionString(Constants.Resources.StateHolderDatabase)));
builder.Services.AddSingleton<ICaseStateStore, PostgresCaseStateStore>();
builder.Services.AddHostedService<StateMaintenance>();

builder.Services.AddGrpc();

// Automatically register all handlers from the assembly of a given type...
builder.Services.AutoRegisterHandlersFromAssemblyOf<EstimationStateChangedHandler>();

builder.Services.AddRebus(configure => configure
    .Options(o =>
    {
        o.LogPipeline(verbose: true);
        o.EnableDiagnosticSources();
    })
    .Transport(t => t.UseRabbitMq(
        builder.Configuration.GetMessagingConnectionString(),
        Constants.Queues.StateHolder)));

var app = builder.Build();

// Nothing else depends on the gauges, so create them here.
app.Services.GetRequiredService<StateMetrics>();

app.MapDefaultEndpoints();

app.MapGrpcService<StateService>();

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
