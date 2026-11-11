using Acheve.External.ImageProcess.Features;
using Acheve.Common.Chaos;
using Acheve.External.Shared;

var builder = WebApplication.CreateBuilder(args);

// Like a real external system: its traces stay apart from the application's traces.
builder.AddExternalSystemDefaults();

// Faults, latency and lost, duplicated or late callbacks, switched at run time (/chaos).
builder.AddChaos();

// Checks the data annotations on the request models and answers 400 when they fail.
builder.Services.AddValidation();

// Calls back to the URL each request provides once the work is done.
builder.Services.AddHttpClient("ImageProcessConfirmation")
    .AddStandardResilienceHandler();

// Requests already accepted, by their Idempotency-Key header.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SeenRequests>();

// Runs the accepted jobs in the background, several at a time (BackgroundQueue:MaxConcurrency).
builder.Services.AddBackgroundQueue(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseChaos();

app.MapImageProcess();

app.Run();
