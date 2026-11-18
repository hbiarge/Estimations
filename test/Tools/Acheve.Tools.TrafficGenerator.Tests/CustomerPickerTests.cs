namespace Acheve.Tools.TrafficGenerator.Tests
{
    public class CustomerPickerTests
    {
        private static CustomerOptions Customer(string name, double weight) => new()
        {
            Name = name,
            ClientId = name,
            ClientSecret = "secret",
            CallbackUrl = $"https://{name}/callback",
            Weight = weight
        };

        [Fact]
        public void Picks_in_proportion_to_weight_and_never_a_customer_with_weight_zero()
        {
            var options = new TrafficOptions { Customers = [Customer("acme", 1), Customer("globex", 3), Customer("initech", 0)] };
            var picker = new CustomerPicker(options);
            var random = new Random(7);

            var picks = Enumerable.Range(0, 4000).Select(_ => picker.Pick(random).Name).ToList();

            picks.ShouldNotContain("initech");
            var acme = picks.Count(x => x == "acme");
            var globex = picks.Count(x => x == "globex");
            acme.ShouldBeInRange(850, 1150);
            globex.ShouldBeInRange(2850, 3150);
        }

        [Fact]
        public void Rejects_when_every_weight_is_zero()
        {
            var options = new TrafficOptions { Customers = [Customer("acme", 0), Customer("globex", 0)] };

            Should.Throw<ArgumentException>(() => new CustomerPicker(options));
        }
    }
}
