namespace Acheve.Tools.TrafficGenerator
{
    /// <summary>The body of POST /estimations.</summary>
    public sealed record NewEstimationRequest(string CallBackUri, string[] ImageUrls);

    /// <summary>Builds random cases: 1 to MaxImagesPerCase images, sometimes with one that doesn't exist.</summary>
    internal sealed class CaseFactory(TrafficOptions options, Random random)
    {
        public NewEstimationRequest Create()
        {
            var count = random.Next(1, options.MaxImagesPerCase + 1);
            var images = new string[count];

            for (var i = 0; i < count; i++)
            {
                images[i] = options.ImageUrls[random.Next(options.ImageUrls.Length)];
            }

            if (options.BrokenImageUrl is { Length: > 0 } broken && random.NextDouble() < options.BrokenImageRate)
            {
                images[random.Next(count)] = broken;
            }

            return new NewEstimationRequest(options.CallbackUrl, images);
        }
    }
}
