using System;

namespace Acheve.Common.Messages
{
    public class EstimationCompleted
    {
        public required Guid CaseNumber { get; init; }

        public required string EstimationTicket { get; init; }
    }
}