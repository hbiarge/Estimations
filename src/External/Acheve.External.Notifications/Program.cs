using Acheve.Common.Chaos;
using Acheve.Common.Shared;
using Acheve.External.Notifications.Features;
using Acheve.External.Shared;

var builder = WebApplication.CreateBuilder(args);

// Like a real external system: its traces stay apart from the application's traces.
builder.AddExternalSystemDefaults();

// Faults and latency switched at run time (/chaos).
builder.AddChaos();

// Checks the signature of the notifications when ClientCallbacks:SigningKey is set (the key the application signs with).
builder.Services.AddSingleton(new NotificationVerifier(builder.Configuration.GetNotificationSigner()));

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseChaos();

app.MapNotification();

app.Run();
