using Acheve.Common.Messages;

namespace Acheve.Application.ProcessManager.Handlers
{
    public partial class EstimationSaga
    {
        public async Task Handle(NotificationCompleted message)
        {
            _logger.LogInformation(
                "Case number {caseNumber}. Estimation successfully notified to client {clientId} in {callbackUrl}.",
                message.CaseNumber,
                Data.ClientId,
                Data.CallbackUrl);

            Data.State = EstimationStates.ClientNotified;

            await _bus.Send(new EstimationStateChanged
            {
                CaseNumber = Data.CaseNumber,
                ClientId = Data.ClientId,
                State = Data.State
            });

            MarkAsComplete();
            await CleanupDataBus();

            _logger.LogInformation(
                "Case number {caseNumber} finished.",
                message.CaseNumber);
        }

        public async Task Handle(UnableToNotify message)
        {
            _logger.LogWarning(
                "Case number {caseNumber}. Unable to notify to {clientId} in url {callbackUrl}",
                message.CaseNumber,
                Data.ClientId,
                Data.CallbackUrl);

            Data.State = EstimationStates.ClientNotificationError;

            await _bus.Send(new EstimationStateChanged
            {
                CaseNumber = Data.CaseNumber,
                ClientId = Data.ClientId,
                State = Data.State
            });

            MarkAsComplete();
            await CleanupDataBus();

            _logger.LogInformation(
                "Case number {caseNumber} finished.",
                message.CaseNumber);
        }

        private async Task CleanupDataBus()
        {
            var tickets = new List<string>();

            if (string.IsNullOrEmpty(Data.EstimationTicket) == false)
            {
                tickets.Add(Data.EstimationTicket);
            }

            foreach (var image in Data.Images)
            {
                if (string.IsNullOrEmpty(image.ImageTicket) == false)
                {
                    tickets.Add(image.ImageTicket);
                }
                if (string.IsNullOrEmpty(image.AnalisysTicket) == false)
                {
                    tickets.Add(image.AnalisysTicket);
                }
            }

            foreach (var ticket in tickets)
            {
                await _bus.Advanced.DataBus.Delete(ticket);
            }
        }
    }
}
