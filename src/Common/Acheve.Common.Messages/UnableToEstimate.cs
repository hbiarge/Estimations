using System;

namespace Acheve.Common.Messages
{
    public class UnableToEstimate
    {
        public required Guid CaseNumber { get; init; }
        
        public required string Error { get; init; }
    }
}