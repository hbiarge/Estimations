using Acheve.Tools.ChaosMonkey;
using Microsoft.Extensions.Options;

// Runs chaos strategies: named steps (in its settings) that switch the chaos scenarios of the Api and the fake
// external systems and the resilience profiles of the services that call them. Run it with the traffic generator.
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

// Switching a scenario or a profile is idempotent, so it's safe to retry. Short timeouts: a service that is down
// shouldn't hold the other switches of the step.
builder.Services.AddHttpClient(ChaosClient.HttpClientName)
    .AddStandardResilienceHandler(resilience =>
    {
        resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
        resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
    });

// One strategy at a time; the host stopping puts everything back to healthy and standard.
builder.Services.AddSingleton<StrategyRunner>();
builder.Services.AddHostedService(services => services.GetRequiredService<StrategyRunner>());

builder.Build().Run();
