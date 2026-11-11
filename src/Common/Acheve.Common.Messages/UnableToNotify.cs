namespace Acheve.Common.Messages
{
    public class UnableToNotify
    {
        public required Guid CaseNumber { get; init; }

        public required int Attempt { get; init; }

        public required string Error { get; init; }
    }
}
