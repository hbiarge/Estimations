namespace Acheve.Tools.TrafficGenerator
{
    /// <summary>How a case ended, as the customer sees it.</summary>
    public enum CaseOutcome
    {
        /// <summary>Estimated and the customer notified.</summary>
        Succeeded,

        /// <summary>The case failed (no image could be analysed, or no estimation); the customer was told about the failure.</summary>
        Failed,

        /// <summary>The customer could not be notified.</summary>
        NotificationFailed,

        /// <summary>The customer was notified, but the Api didn't say whether the case had an estimation or an error.</summary>
        Unknown,

        /// <summary>No final state within the case timeout.</summary>
        TimedOut,

        /// <summary>The Api didn't accept the case.</summary>
        Rejected
    }

    internal static class CaseStates
    {
        // Names from EstimationStates and CaseResults, as GET /estimations/{ticket} returns them.
        public const string CustomerNotified = "CustomerNotified";
        public const string CustomerNotificationError = "CustomerNotificationError";
        public const string Success = "Success";
        public const string Failure = "Failure";

        public static bool IsFinal(string state) => state is CustomerNotified or CustomerNotificationError;

        /// <summary>
        /// A failed case still ends in CustomerNotified (the customer is told about the failure),
        /// so the outcome comes from the result the Api returns next to the state.
        /// </summary>
        public static CaseOutcome Classify(CaseStatus? last) => last switch
        {
            { State: CustomerNotificationError } => CaseOutcome.NotificationFailed,
            { State: CustomerNotified, Result: Success } => CaseOutcome.Succeeded,
            { State: CustomerNotified, Result: Failure } => CaseOutcome.Failed,
            { State: CustomerNotified } => CaseOutcome.Unknown,
            _ => CaseOutcome.TimedOut
        };
    }
}
