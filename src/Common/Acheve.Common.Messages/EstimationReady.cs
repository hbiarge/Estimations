namespace Acheve.Common.Messages
{
    public class EstimationReady
    {
        public required Guid CaseNumber { get; init; }

        public required string CallbackUrl { get; init; }
        
        public required string EstimationId { get; init; }
    }
}