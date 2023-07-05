namespace Acheve.Common.Messages
{
    public class EstimationError
    {
        public required Guid CaseNumber { get; init; }

        public required string CallbackUrl { get; init; }

        public required string Reason { get; init; }
    }
}