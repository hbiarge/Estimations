using Acheve.Common.Messages;
using Rebus.Bus;
using Rebus.Handlers;

namespace Acheve.Application.CallbackNotifier.Handlers
{
    public class NotifyEstimationHandler(
        IBus bus,
        ClientNotifier notifier,
        ILogger<NotifyEstimationHandler> logger) : IHandleMessages<NotifyEstimation>
    {
        public Task Handle(NotifyEstimation message)
        {
            logger.LogInformation(
                "Receiving case number {caseNumber} to notify ready estimation (attempt {attempt}).",
                message.CaseNumber,
                message.Attempt);

            return notifier.Notify(message.CaseNumber, message.CallbackUrl, message.Attempt, async () =>
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
