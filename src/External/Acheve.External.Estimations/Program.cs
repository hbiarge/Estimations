using Acheve.External.Estimations.Features;
using Acheve.External.Shared;

var builder = WebApplication.CreateBuilder(args);

// Like a real external system: its traces stay apart from the application's traces.
builder.AddExternalSystemDefaults();

// Checks the data annotations on the request models and answers 400 when they fail.
builder.Services.AddValidation();

// Calls back to the URL each request provides once the work is done.
builder.Services.AddHttpClient("EstimationConfirmation")
    .AddStandardResilienceHandler();

builder.Services.AddHostedService<QueuedHostedService>();
builder.Services.AddSingleton<IBackgroundTaskQueue>(_ =>
{
    if (!int.TryParse(builder.Configuration["QueueCapacity"], out var queueCapacity))
    {
        queueCapacity = 100;
    }

    return new DefaultBackgroundTaskQueue(queueCapacity);
});

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapEstimation();

app.Run();
