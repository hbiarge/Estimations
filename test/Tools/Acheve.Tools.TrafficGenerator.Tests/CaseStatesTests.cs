namespace Acheve.Tools.TrafficGenerator.Tests
{
    public class CaseStatesTests
    {
        [Theory]
        [InlineData("WaitingForEstimation,EstimationReady,ClientNotified", "ClientNotified", CaseOutcome.Succeeded)]
        [InlineData("WaitingForEstimation,EstimationError,ClientNotified", "ClientNotified", CaseOutcome.Failed)]
        [InlineData("WaitingForEstimation,ClientNotified", "ClientNotified", CaseOutcome.Unknown)]
        [InlineData("EstimationReady,ClientNotificationError", "ClientNotificationError", CaseOutcome.NotificationFailed)]
        [InlineData("WaitingForImagesToBeAnalysed", "WaitingForImagesToBeAnalysed", CaseOutcome.TimedOut)]
        [InlineData("", "", CaseOutcome.TimedOut)]
        public void Classifies_the_case_from_the_states_seen(string seen, string final, CaseOutcome expected)
        {
            var states = seen.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            CaseStates.Classify(states, final).ShouldBe(expected);
        }

        [Theory]
        [InlineData(CaseOutcome.Succeeded, "succeeded")]
        [InlineData(CaseOutcome.NotificationFailed, "notification_failed")]
        [InlineData(CaseOutcome.TimedOut, "timed_out")]
        public void Outcome_tags_are_snake_case(CaseOutcome outcome, string expected)
        {
            TrafficMetrics.ToTagValue(outcome).ShouldBe(expected);
        }
    }
}
