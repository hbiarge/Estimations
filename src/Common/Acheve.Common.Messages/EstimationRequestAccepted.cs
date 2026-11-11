namespace Acheve.Common.Messages
{
    // The external estimation service accepted the request. The saga's wait for the estimation starts again from here.
    public class EstimationRequestAccepted
    {
        public required Guid CaseNumber { get; init; }
    }
}
