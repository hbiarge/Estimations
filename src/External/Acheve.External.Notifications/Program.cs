using Acheve.External.Notifications.Features;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Checks the data annotations on the request models and answers 400 when they fail.
builder.Services.AddValidation();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapNotification();

app.Run();
