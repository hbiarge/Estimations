using Acheve.Application.Api.Features.Estimations;
using Acheve.Application.Api.Features.EstimationCallback;
using Acheve.Application.Api.Features.ImageAnalysisCallback;
using Acheve.Application.Api.Infrastructure;
using Acheve.Common.Chaos;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Rebus.Config;
using Rebus.DataBus.FileSystem;
using Rebus.Routing.TypeBased;
using StateHolder;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddAuthentication()
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.Authority = "https://demo.duendesoftware.com/";
        options.Audience = "api";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "client_id"
        };
    });

builder.Services.AddAuthorization();

// Checks the data annotations on the request models and answers 400 when they fail.
builder.Services.AddValidation();

// Signs and checks the callback URLs given to the external systems.
builder.Services.AddSingleton(CallbackSigner.FromConfiguration(builder.Configuration));

// Continues each case's trace when the external systems call back.
builder.Services.AddSingleton(new CallbackTracing(builder.Environment.ApplicationName));

builder.Services.AddGrpcClient<StateHolderService.StateHolderServiceClient>(options =>
        options.Address = new Uri($"https://{Constants.Resources.StateHolder}"))
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler();

        if (builder.Environment.IsDevelopment())
        {
            handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true;
        }

        return handler;
    })
    // gRPC reports errors inside HTTP 200 responses, so the HTTP resilience handler can't see them.
    // Use gRPC's own retry policy with the same values as AddStandardResilienceHandler():
    // 3 retries, exponential backoff starting at 2s with jitter, and a 30s limit for the whole call.
    .ConfigureChannel(channel =>
    {
        channel.ServiceConfig = new ServiceConfig
        {
            MethodConfigs =
            {
                new MethodConfig
                {
                    Names = { MethodName.Default },
                    RetryPolicy = new RetryPolicy
                    {
                        MaxAttempts = 4, // first attempt + 3 retries
                        InitialBackoff = TimeSpan.FromSeconds(2),
                        MaxBackoff = TimeSpan.FromSeconds(30),
                        BackoffMultiplier = 2,
                        RetryableStatusCodes = { StatusCode.Unavailable }
                    }
                }
            }
        };
    })
    .AddInterceptor(() => new DefaultDeadlineInterceptor(TimeSpan.FromSeconds(30)));

builder.Services.AddRebus(configure => configure
    .Options(o =>
    {
        o.LogPipeline(verbose: true);
        o.EnableDiagnosticSources();
    })
    .DataBus(d => d.StoreInFileSystem(builder.Configuration.GetStoragePath(Constants.Storage.DataBus)))
    .Transport(t => t.UseRabbitMqAsOneWayClient(builder.Configuration.GetMessagingConnectionString()))
    .Routing(r => r.TypeBased()
        .Map<EstimationRequested>(Constants.Queues.ProcessManager)
        .Map<ImageAnalysed>(Constants.Queues.ProcessManager)
        .Map<EstimationCompleted>(Constants.Queues.ProcessManager)));

// Faults and latency on the callback endpoints (Chaos:Paths), switched at run time (/chaos).
builder.AddChaos();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseChaos();

app.UseAuthentication();
app.UseAuthorization();

app.MapEstimations();
app.MapEstimationCallback();
app.MapImageAnalysisCallback();

app.Run();
