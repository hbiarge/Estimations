namespace Acheve.Tools.TrafficGenerator
{
    /// <summary>How a case ended, as the client sees it.</summary>
    public enum CaseOutcome
    {
        /// <summary>Estimated and the client notified.</summary>
        Succeeded,

        /// <summary>The case failed (no image could be analysed, or no estimation); the client was told about the failure.</summary>
        Failed,

        /// <summary>The client could not be notified.</summary>
        NotificationFailed,

        /// <summary>The client was notified, but the polls missed whether the case had an estimation or an error.</summary>
        Unknown,

        /// <summary>No final state within the case timeout.</summary>
        TimedOut,

        /// <summary>The Api didn't accept the case.</summary>
        Rejected
    }

    internal static class CaseStates
    {
        // Names from EstimationStates, as GET /estimations/{ticket} returns them.
        public const string EstimationReady = "EstimationReady";
        public const string EstimationError = "EstimationError";
        public const string ClientNotified = "ClientNotified";
        public const string ClientNotificationError = "ClientNotificationError";

        public static bool IsFinal(string state) => state is ClientNotified or ClientNotificationError;

        /// <summary>
        /// The Api only returns the furthest state a case reached, and a failed case still ends in ClientNotified
        /// (the client is told about the failure). So the outcome comes from the states seen along the way.
        /// </summary>
        public static CaseOutcome Classify(IReadOnlySet<string> seen, string final) => final switch
        {
            ClientNotificationError => CaseOutcome.NotificationFailed,
            ClientNotified when seen.Contains(EstimationError) => CaseOutcome.Failed,
            ClientNotified when seen.Contains(EstimationReady) => CaseOutcome.Succeeded,
            ClientNotified => CaseOutcome.Unknown,
            _ => CaseOutcome.TimedOut
        };
    }
}
