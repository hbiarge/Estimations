namespace Acheve.Common.Messages
{
    public class EstimationStateChanged
    {
        public required Guid CaseNumber { get; init; }

        public required string ClientId { get; init; }

        public required EstimationStates State { get; init; }
    }
}