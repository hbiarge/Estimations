namespace Acheve.Application.EstimationProcessor
{
    public class Worker : BackgroundService
    {
        public Worker(IServiceProvider serviceProvider)
        {
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
