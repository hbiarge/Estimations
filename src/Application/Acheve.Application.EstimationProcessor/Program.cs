using System.Diagnostics;
using Acheve.Application.EstimationProcessor.Handlers;
using Acheve.Common.Messages;
using Acheve.Common.Messages.Tracing;
using Acheve.Common.Shared;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Routing.TypeBased;
using Serilog;
using Serilog.Events;

namespace Acheve.Application.EstimationProcessor
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.Title = Constants.Services.EstimationService;

            Activity.DefaultIdFormat = ActivityIdFormat.W3C;

            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging((context, builder) =>
                {
                    Log.Logger = new LoggerConfiguration()
                        .MinimumLevel.Information()
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                        .MinimumLevel.Override("System", LogEventLevel.Information)
                        .Enrich.WithProperty("Application", Constants.Services.EstimationService)
                        .WriteTo.Console()
                        .CreateLogger();

                    builder.ClearProviders();
                    builder.AddSerilog();

                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<ServicesConfiguration>(hostContext.Configuration.GetSection("Services"));
                    services.AddSingleton<IPostConfigureOptions<ServicesConfiguration>, ServicesPostConfiguration>();

                    services.AddHttpClient("estimations")
                        .AddStandardResilienceHandler();

                    // Automatically register all handlers from the assembly of a given type...
                    services.AutoRegisterHandlersFromAssemblyOf<ExternalEstimationReadyHandler>();

                    //Configure Rebus
                    services.AddRebus(configure => configure
                        .Options(o =>
                        {
                            o.LogPipeline(verbose: true);
                            o.UseDistributeTracingFlow();
                        })
                        .Logging(l => l.Serilog())
                        .DataBus(d => d.StoreInBlobStorage(Constants.Azure.Storage.ConnectionString, Constants.Azure.Storage.DataBusContainer))
                        .Transport(t =>
                        {
                            t.UseAzureServiceBus(
                                Constants.Azure.ServiceBus.ConnectionString, 
                                Constants.Queues.ExternalEstimations);
                            t.UseNativeHeaders();
                            t.UseNativeDeadlettering();
                        })
                        .Routing(r => r.TypeBased()
                            .Map<EstimationCompleted>(Constants.Queues.ProcessManager)
                            .Map<UnableToEstimate>(Constants.Queues.ProcessManager)
                            .Map<AwaitEstimationToBeProcessed>(Constants.Queues.ProcessManager)));

                    services.AddHostedService<Worker>();
                });
    }
}
