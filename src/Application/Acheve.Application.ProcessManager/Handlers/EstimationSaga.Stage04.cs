using Acheve.Common.Messages;

namespace Acheve.Application.ProcessManager.Handlers
{
    public partial class EstimationSaga
    {
        public async Task Handle(NotificationCompleted message)
        {
            logger.LogInformation(
                "Case number {caseNumber}. Estimation successfully notified to client {clientId} in {callbackUrl}.",
                message.CaseNumber,
                Data.ClientId,
                Data.CallbackUrl);

            Data.State = EstimationStates.ClientNotified;

            await bus.Send(new EstimationStateChanged
            {
                CaseNumber = Data.CaseNumber,
                ClientId = Data.ClientId,
                State = Data.State
            });

            MarkAsComplete();
            await CleanupDataBus();

            logger.LogInformation(
                "Case number {caseNumber} finished.",
                message.CaseNumber);
        }

        public async Task Handle(UnableToNotify message)
        {
            logger.LogWarning(
                "Case number {caseNumber}. Unable to notify to {clientId} in url {callbackUrl}",
                message.CaseNumber,
                Data.ClientId,
                Data.CallbackUrl);

            Data.State = EstimationStates.ClientNotificationError;

            await bus.Send(new EstimationStateChanged
            {
                CaseNumber = Data.CaseNumber,
                ClientId = Data.ClientId,
                State = Data.State
            });

            MarkAsComplete();
            await CleanupDataBus();

            logger.LogInformation(
                "Case number {caseNumber} finished.",
                message.CaseNumber);
        }

        private async Task CleanupDataBus()
        {
            var tickets = new List<string>();

            if (string.IsNullOrEmpty(Data.ExternalEstimationTicket) == false)
            {
                tickets.Add(Data.ExternalEstimationTicket);
            }

            foreach (var image in Data.Images)
            {
                if (string.IsNullOrEmpty(image.ImageTicket) == false)
                {
                    tickets.Add(image.ImageTicket);
                }
                if (string.IsNullOrEmpty(image.AnalysisTicket) == false)
                {
                    tickets.Add(image.AnalysisTicket);
                }
            }

            foreach (var ticket in tickets)
            {
                await bus.Advanced.DataBus.Delete(ticket);
            }
        }
    }
}
