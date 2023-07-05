namespace Acheve.Common.Messages
{
    public class AwaitEstimationToBeProcessed
    {
        public required Guid CaseNumber { get; init; }
    }

    public class StillAwaitingEstimationToBeProcessed
    {
        public required Guid CaseNumber { get; init; }
    }
}