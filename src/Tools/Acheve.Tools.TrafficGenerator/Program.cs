using Acheve.Common.Shared;
using Acheve.Tools.TrafficGenerator;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

// Plays many clients: asks for estimations over time and follows each case to its end.
// Its metrics (meter Acheve.Traffic) show the system as a client sees it.
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOptions<TrafficOptions>()
    .Bind(builder.Configuration.GetSection(TrafficOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(Random.Shared);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<TrafficOptions>>().Value);
builder.Services.AddSingleton<CaseFactory>();
builder.Services.AddSingleton<TrafficMetrics>();
builder.Services.AddSingleton<CaseRunner>();

// One token for every case, cached until it expires.
builder.Services.AddSingleton<TokenProvider>();
builder.Services.AddHttpClient(TokenProvider.HttpClientName)
    .AddStandardResilienceHandler();

// A retried POST could start the same case twice, so only the reads are retried.
builder.Services.AddSingleton<IEstimationsClient, EstimationsClient>();
builder.Services.AddHttpClient(EstimationsClient.HttpClientName, client =>
        client.BaseAddress = new Uri($"https+http://{Constants.Resources.Api}"))
    .AddStandardResilienceHandler(resilience => resilience.Retry.DisableForUnsafeHttpMethods());

builder.Services.AddHostedService<TrafficWorker>();

builder.Build().Run();
