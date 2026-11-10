using Acheve.Common.Messages;
using Rebus.Sagas;
using Rebus.Sagas.Idempotent;

namespace Acheve.Application.ProcessManager.Handlers
{
    public class EstimationState : SagaData, IIdempotentSagaData
    {
        public const int MaxEstimationWaits = 5;
        public static readonly TimeSpan EstimationWaitTime = TimeSpan.FromSeconds(20);

        public Guid CaseNumber { get; set; }

        public string ClientId { get; set; } = string.Empty;

        public string CallbackUrl { get; set; } = string.Empty;

        public ICollection<CaseImage> Images { get; set; } = new List<CaseImage>();

        public EstimationStates State { get; set; }

        // When the case started and when it entered its current state, for the duration metrics.
        // Cases saved before these existed keep the default value and record no duration.
        public DateTimeOffset StartedAt { get; set; }

        public DateTimeOffset StateChangedAt { get; set; }

        // Why the estimation failed (a FailureReasons value), or null.
        public string? FailureReason { get; set; }

        public string? ExternalEstimationTicket { get; set; }

        public string? ExternalEstimationError { get; set; }

        public int CurrentExternalEstimationWaits { get; set; }

        public bool EstimationWaitStarted { get; set; }

        // True once the case has the estimation result (success or error).
        public bool EstimationFinished => State >= EstimationStates.EstimationReady;

        // Handled message ids and the messages sent while handling each one, saved with the rest of the saga data.
        // Rebus uses it to skip a redelivered message and resend its outgoing messages (see Program.cs).
        public IdempotencyData IdempotencyData { get; set; } = new();
    }
}