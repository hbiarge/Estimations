using Acheve.Common.Messages;
using Acheve.Common.Messages.Tracing;
using Acheve.Common.Shared;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Rebus.Config;
using Rebus.Routing.TypeBased;
using StateHolder;

namespace Acheve.Application.Api
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IHostEnvironment environment)
        {
            Configuration = configuration;
            Environment = environment;
        }

        public IConfiguration Configuration { get; }
        public IHostEnvironment Environment { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddApplicationInsightsTelemetry(config =>
                config.ConnectionString = Constants.Azure.Apm.ConnectionString);
            services.AddSingleton<ITelemetryInitializer>(sp => new ServiceNameInitializer(Constants.Services.Api));

            services.Configure<ServicesConfiguration>(Configuration.GetSection("Services"));
            services.AddSingleton<IPostConfigureOptions<ServicesConfiguration>, ServicesPostConfiguration>();

            services.AddAuthentication()
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = "https://demo.duendesoftware.com/";
                    options.Audience = "api";
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        NameClaimType = "client_id"
                    };
                });

            services.AddControllers();

            services.AddGrpcClient<StateHolderService.StateHolderServiceClient>((sp, options) =>
                {
                    var servicesConfiguration = sp.GetRequiredService<IOptions<ServicesConfiguration>>();
                    options.Address = new Uri(servicesConfiguration.Value.StateHolder!.BaseUrl);
                })
                .ConfigurePrimaryHttpMessageHandler(() =>
                {
                    var handler = new HttpClientHandler();

                    if (Environment.IsDevelopment())
                    {
                        handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true;
                    }

                    return handler;
                })
                .AddPolicyHandler(PollyDefaults.RetryPolicyBuilder)
                .AddPolicyHandler(PollyDefaults.TimeoutPolicyBuilder);

            services.AddRebus(
                configure: configurer => configurer
                    .Options(o =>
                    {
                        o.LogPipeline(verbose: true);
                        o.UseDistributeTracingFlow();
                    })
                    .Logging(l => l.Serilog())
                    .DataBus(d => d.StoreInBlobStorage(Constants.Azure.Storage.ConnectionString, Constants.Azure.Storage.DataBusContainer))
                    .Transport(t =>
                    {
                        t.UseAzureServiceBusAsOneWayClient(Constants.Azure.ServiceBus.ConnectionString);
                        t.UseNativeHeaders();
                        t.UseNativeDeadlettering();
                    })
                    .Routing(r => r.TypeBased()
                        .Map<EstimationRequested>(Constants.Queues.ProcessManager)
                        .Map<ImageAnalized>(Constants.Queues.ProcessManager)
                        .Map<EstimationCompleted>(Constants.Queues.ProcessManager)),
                isDefaultBus: true,
                onCreated: async bus => await Task.Delay(0),
                startAutomatically: true);
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
