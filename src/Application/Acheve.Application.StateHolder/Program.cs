using Acheve.Application.StateHolder;
using Acheve.Application.StateHolder.Handlers;
using Acheve.Application.StateHolder.Services;
using Acheve.Common.Shared;
using Rebus.Config;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<State>();

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

app.MapDefaultEndpoints();

app.MapGrpcService<StateService>();

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
