namespace Acheve.Tools.TrafficGenerator.Tests
{
    public class CaseStatesTests
    {
        [Theory]
        [InlineData("CustomerNotified", "Success", CaseOutcome.Succeeded)]
        [InlineData("CustomerNotified", "Failure", CaseOutcome.Failed)]
        [InlineData("CustomerNotified", null, CaseOutcome.Unknown)]
        [InlineData("CustomerNotificationError", "Success", CaseOutcome.NotificationFailed)]
        [InlineData("CustomerNotificationError", "Failure", CaseOutcome.NotificationFailed)]
        [InlineData("EstimationReady", "Success", CaseOutcome.TimedOut)]
        [InlineData("WaitingForImagesToBeAnalysed", null, CaseOutcome.TimedOut)]
        public void Classifies_the_case_from_its_last_state_and_result(string state, string? result, CaseOutcome expected)
        {
            CaseStates.Classify(new CaseStatus(state, result)).ShouldBe(expected);
        }

        [Fact]
        public void A_case_never_seen_timed_out()
        {
            CaseStates.Classify(null).ShouldBe(CaseOutcome.TimedOut);
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
