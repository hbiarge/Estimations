using Acheve.Common.Chaos;
using Acheve.External.Notifications.Features;
using Acheve.External.Shared;

var builder = WebApplication.CreateBuilder(args);

// Like a real external system: its traces stay apart from the application's traces.
builder.AddExternalSystemDefaults();

// Faults and latency switched at run time (/chaos).
builder.AddChaos();

// Checks the data annotations on the request models and answers 400 when they fail.
builder.Services.AddValidation();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseChaos();

app.MapNotification();

app.Run();
