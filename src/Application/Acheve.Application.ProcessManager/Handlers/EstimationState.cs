using Acheve.Common.Messages;
using Rebus.Sagas;

namespace Acheve.Application.ProcessManager.Handlers
{
    public class EstimationState : SagaData
    {
        public const int MaxEstimationWaits = 5;
        public static readonly TimeSpan EstimationWaitTime = TimeSpan.FromSeconds(20);

        public Guid CaseNumber { get; set; }

        public string ClientId { get; set; } = string.Empty;

        public string CallbackUrl { get; set; } = string.Empty;

        public ICollection<CaseImage> Images { get; set; } = new List<CaseImage>();

        public EstimationStates State { get; set; }

        public string? ExternalEstimationTicket { get; set; }

        public string? ExternalEstimationError { get; set; }

        public int CurrentExternalEstimationWaits { get; set; }
    }
}