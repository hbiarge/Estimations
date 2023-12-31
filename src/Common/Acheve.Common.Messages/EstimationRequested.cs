﻿namespace Acheve.Common.Messages
{
    public class EstimationRequested
    {
        public required Guid CaseNumber { get; init; }

        public required string ClientId { get; init; }

        public required string CallbackUrl { get; init; }

        public required ICollection<string> ImageUrls { get; init; }
    }
}
