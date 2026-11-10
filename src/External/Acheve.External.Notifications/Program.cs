using Acheve.External.Notifications.Features;
using Acheve.External.Shared;

var builder = WebApplication.CreateBuilder(args);

// Like a real external system: its traces stay apart from the application's traces.
builder.AddExternalSystemDefaults();

// Checks the data annotations on the request models and answers 400 when they fail.
builder.Services.AddValidation();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapNotification();

app.Run();
