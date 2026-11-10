namespace Acheve.Tools.TrafficGenerator.Tests
{
    public class TrafficShapeTests
    {
        [Fact]
        public void Steady_keeps_the_configured_rate()
        {
            var options = new TrafficOptions { CasesPerMinute = 12, Pattern = TrafficPattern.Steady };

            TrafficShape.CasesPerMinute(options, TimeSpan.FromMinutes(7)).ShouldBe(12);
        }

        [Theory]
        [InlineData(0, 10)]
        [InlineData(2.5, 15)] // a quarter of the period: the peak
        [InlineData(7.5, 5)]  // three quarters: the trough
        public void Wave_swings_between_half_and_one_and_a_half_times_the_rate(double minutes, double expected)
        {
            var options = new TrafficOptions { CasesPerMinute = 10, Pattern = TrafficPattern.Wave, WavePeriod = TimeSpan.FromMinutes(10) };

            TrafficShape.CasesPerMinute(options, TimeSpan.FromMinutes(minutes)).ShouldBe(expected, tolerance: 0.0001);
        }

        [Fact]
        public void No_gap_when_the_rate_is_zero()
        {
            TrafficShape.NextGap(0, new Random(1)).ShouldBeNull();
        }

        [Fact]
        public void Gaps_average_one_minute_divided_by_the_rate()
        {
            var random = new Random(42);

            var mean = Enumerable.Range(0, 20_000)
                .Select(_ => TrafficShape.NextGap(6, random)!.Value.TotalSeconds)
                .Average();

            mean.ShouldBe(10, tolerance: 0.3);
        }
    }
}
