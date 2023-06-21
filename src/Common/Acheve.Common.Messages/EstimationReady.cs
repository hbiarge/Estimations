using System;

namespace Acheve.Common.Messages
{
    public class EstimationReady
    {
        public required Guid CaseNumber { get; init; }

        public required string CallbackUri { get; init; }
        
        public required string EstimationId { get; init; }
    }
}