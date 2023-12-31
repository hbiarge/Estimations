using Serilog;
using Serilog.Events;

namespace Acheve.External.Notifications
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.Title = "ExternalNotifications";

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
                        .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Information)
                        .MinimumLevel.Override("System", LogEventLevel.Information)
                        .Enrich.WithProperty("Application", "ExternalNotifications")
                        .WriteTo.Console()
                        .CreateLogger();

                    builder.ClearProviders();
                    builder.AddSerilog();

                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
