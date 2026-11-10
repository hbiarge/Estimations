namespace Acheve.Tools.TrafficGenerator.Tests
{
    public class CaseFactoryTests
    {
        private static TrafficOptions Options(double brokenImageRate, string? brokenImageUrl = "https://images/missing.jpg") => new()
        {
            CallbackUrl = "https://client/callback",
            ImageUrls = ["https://images/1.png", "https://images/2.jpg"],
            BrokenImageUrl = brokenImageUrl,
            BrokenImageRate = brokenImageRate,
            MaxImagesPerCase = 3
        };

        [Fact]
        public void Cases_have_between_one_and_the_maximum_images_from_the_list()
        {
            var options = Options(brokenImageRate: 0);
            var factory = new CaseFactory(options, new Random(7));

            var cases = Enumerable.Range(0, 500).Select(_ => factory.Create()).ToList();

            cases.ShouldAllBe(c => c.CallBackUri == options.CallbackUrl);
            cases.Select(c => c.ImageUrls.Length).Distinct().Order().ShouldBe([1, 2, 3]);
            cases.SelectMany(c => c.ImageUrls).Distinct().ShouldBe(options.ImageUrls, ignoreOrder: true);
        }

        [Fact]
        public void A_broken_image_replaces_one_image_at_the_broken_image_rate()
        {
            var factory = new CaseFactory(Options(brokenImageRate: 1), new Random(7));

            var cases = Enumerable.Range(0, 100).Select(_ => factory.Create()).ToList();

            cases.ShouldAllBe(c => c.ImageUrls.Count(url => url == "https://images/missing.jpg") == 1);
        }

        [Fact]
        public void No_broken_image_without_a_broken_image_url()
        {
            var factory = new CaseFactory(Options(brokenImageRate: 1, brokenImageUrl: null), new Random(7));

            Enumerable.Range(0, 100).Select(_ => factory.Create())
                .ShouldAllBe(c => c.ImageUrls.All(url => url.StartsWith("https://images/") && !url.Contains("missing")));
        }
    }
}
