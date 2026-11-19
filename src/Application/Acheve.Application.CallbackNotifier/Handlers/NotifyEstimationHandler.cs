using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.Bus;
using Rebus.Handlers;

namespace Acheve.Application.CallbackNotifier.Handlers
{
    public class NotifyEstimationHandler(
        IBus bus,
        CustomerNotifier notifier,
        ILogger<NotifyEstimationHandler> logger) : IHandleMessages<NotifyEstimation>
    {
        public async Task Handle(NotifyEstimation message)
        {
            using var customerScope = CustomerTelemetry.BeginScope(logger, message.CustomerId);

            logger.LogInformation(
                "Receiving case number {caseNumber} to notify ready estimation (attempt {attempt}).",
                message.CaseNumber,
                message.Attempt);

            await notifier.Notify(message.CaseNumber, message.CustomerId, message.CallbackUrl, message.Attempt, async () =>
            {
                using var estimationReader = new StreamReader(
                    await bus.Advanced.DataBus.OpenRead(message.EstimationTicket));

                return new
                {
                    message.CaseNumber,
                    Result = CaseResults.Success,
                    Estimation = await estimationReader.ReadToEndAsync(),
                    message.SkippedImages
                };
            });
        }
    }
}
