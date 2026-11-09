using Microsoft.Extensions.Logging;

namespace Acheve.Common.Shared
{
    public static partial class RandomFailureGenerator
    {
        public static void RandomFail(double failThreshold, string url, ILogger logger)
        {
            var random = new Random();
            var value = random.NextDouble();
            var overThreshold = value >= failThreshold;

            logger.LogRandomFailureValueIsRandomvalueAndThresholdIsFailurethresholdSoValueIsOver(
                value.ToString("F"),
                failThreshold.ToString("F"),
                overThreshold);

            if (!overThreshold)
            {
                return;
            }
            
            var ex = new InvalidOperationException("Bad luck, just a random fail :(");

            logger.LogError("Ohh an error happened processing the request {url}", url);

            throw ex;
        }

        [LoggerMessage(LogLevel.Information, "Random failure value is {randomValue} and threshold is {failureThreshold}. Is over threshold? {overThreshold}")]
        static partial void LogRandomFailureValueIsRandomvalueAndThresholdIsFailurethresholdSoValueIsOver(
            this ILogger logger,
            string randomValue,
            string failureThreshold,
            bool overThreshold);
    }
}