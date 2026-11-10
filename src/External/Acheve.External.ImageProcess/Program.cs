using Acheve.External.ImageProcess.Features;
using Acheve.External.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Checks the data annotations on the request models and answers 400 when they fail.
builder.Services.AddValidation();

// Calls back to the URL each request provides once the work is done.
builder.Services.AddHttpClient("ImageProcessConfirmation")
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

app.MapImageProcess();

app.Run();
