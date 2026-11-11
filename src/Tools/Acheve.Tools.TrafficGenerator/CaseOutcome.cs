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

        /// <summary>The client was notified, but the Api didn't say whether the case had an estimation or an error.</summary>
        Unknown,

        /// <summary>No final state within the case timeout.</summary>
        TimedOut,

        /// <summary>The Api didn't accept the case.</summary>
        Rejected
    }

    internal static class CaseStates
    {
        // Names from EstimationStates and CaseResults, as GET /estimations/{ticket} returns them.
        public const string ClientNotified = "ClientNotified";
        public const string ClientNotificationError = "ClientNotificationError";
        public const string Success = "Success";
        public const string Failure = "Failure";

        public static bool IsFinal(string state) => state is ClientNotified or ClientNotificationError;

        /// <summary>
        /// A failed case still ends in ClientNotified (the client is told about the failure),
        /// so the outcome comes from the result the Api returns next to the state.
        /// </summary>
        public static CaseOutcome Classify(CaseStatus? last) => last switch
        {
            { State: ClientNotificationError } => CaseOutcome.NotificationFailed,
            { State: ClientNotified, Result: Success } => CaseOutcome.Succeeded,
            { State: ClientNotified, Result: Failure } => CaseOutcome.Failed,
            { State: ClientNotified } => CaseOutcome.Unknown,
            _ => CaseOutcome.TimedOut
        };
    }
}
