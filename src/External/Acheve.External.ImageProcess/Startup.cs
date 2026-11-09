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
            services.Configure<ServicesConfiguration>(Configuration.GetSection("Services"));
            services.AddSingleton<IPostConfigureOptions<ServicesConfiguration>, ServicesPostConfiguration>();

            services.AddControllers();

            services.AddHttpClient("ImageProcessConfirmation")
                .AddStandardResilienceHandler();

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
