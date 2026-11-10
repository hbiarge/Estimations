namespace Acheve.Tools.TrafficGenerator
{
    /// <summary>How many cases to start and when.</summary>
    internal static class TrafficShape
    {
        /// <summary>Target rate, in cases per minute, at <paramref name="elapsed"/> since the generator started.</summary>
        public static double CasesPerMinute(TrafficOptions options, TimeSpan elapsed) => options.Pattern switch
        {
            // Swings between 50% and 150% of the configured rate.
            TrafficPattern.Wave when options.WavePeriod > TimeSpan.Zero =>
                options.CasesPerMinute * (1 + 0.5 * Math.Sin(2 * Math.PI * elapsed / options.WavePeriod)),
            _ => options.CasesPerMinute
        };

        /// <summary>
        /// Random gap until the next case, so arrivals follow a Poisson process with the given rate
        /// (the gaps are exponentially distributed). Null when the rate is 0.
        /// </summary>
        public static TimeSpan? NextGap(double casesPerMinute, Random random)
        {
            if (casesPerMinute <= 0)
            {
                return null;
            }

            var meanSeconds = 60 / casesPerMinute;
            return TimeSpan.FromSeconds(-Math.Log(1 - random.NextDouble()) * meanSeconds);
        }
    }
}
