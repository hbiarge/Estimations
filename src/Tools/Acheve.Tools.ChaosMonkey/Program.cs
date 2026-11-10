using Acheve.Tools.ChaosMonkey;
using Microsoft.Extensions.Options;

// Switches the chaos scenarios of the Api and the fake external systems over time,
// from a timeline in its settings or at random. Run it together with the traffic generator.
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOptions<ChaosMonkeyOptions>()
    .Bind(builder.Configuration.GetSection(ChaosMonkeyOptions.Section))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ChaosMonkeyOptions>, ChaosMonkeyOptionsValidator>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(Random.Shared);
builder.Services.AddSingleton<ChaosMonkeyMetrics>();
builder.Services.AddSingleton<IChaosClient, ChaosClient>();

// Switching a scenario is idempotent, so it's safe to retry. Short timeouts: a service that is down
// shouldn't hold the other switches of the step.
builder.Services.AddHttpClient(ChaosClient.HttpClientName)
    .AddStandardResilienceHandler(resilience =>
    {
        resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
        resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
    });

builder.Services.AddHostedService<ChaosMonkeyWorker>();

builder.Build().Run();
