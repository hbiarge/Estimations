using System;

namespace Acheve.Common.Messages
{
    public class EstimationError
    {
        public required Guid CaseNumber { get; init; }

        public required string CallbackUri { get; init; }

        public required string Reason { get; init; }
    }
}