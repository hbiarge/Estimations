using Acheve.Common.Shared;
using Acheve.External.Shared;
using Microsoft.Extensions.Options;

namespace Acheve.External.ImageProcess
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            //services.AddApplicationInsightsTelemetry(config =>
            //    config.ConnectionString = Constants.Azure.Apm.ConnectionString); 
            //services.AddSingleton<ITelemetryInitializer>(sp => new ServiceNameInitializer(Constants.Services.External.ImageProcess));

            services.Configure<ServicesConfiguration>(Configuration.GetSection("Service"));
            services.AddSingleton<IPostConfigureOptions<ServicesConfiguration>, ServicesPostConfiguration>();

            services.AddControllers();

            services.AddHttpClient("ImageProcessConfirmation")
                .AddPolicyHandler(PollyDefaults.RetryPolicyBuilder)
                .AddPolicyHandler(PollyDefaults.TimeoutPolicyBuilder);

            services.AddHostedService<QueuedHostedService>();
            services.AddSingleton<IBackgroundTaskQueue>(_ =>
            {
                if (!int.TryParse(Configuration["QueueCapacity"], out var queueCapacity))
                {
                    queueCapacity = 100;
                }

                return new DefaultBackgroundTaskQueue(queueCapacity);
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
