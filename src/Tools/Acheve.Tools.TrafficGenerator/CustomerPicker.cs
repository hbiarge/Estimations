namespace Acheve.Tools.TrafficGenerator
{
    /// <summary>Chooses the customer of each new case, in proportion to the customers' weights.</summary>
    internal sealed class CustomerPicker
    {
        private readonly CustomerOptions[] _customers;
        private readonly double _totalWeight;

        public CustomerPicker(TrafficOptions options)
        {
            _customers = options.Customers.Where(x => x.Weight > 0).ToArray();
            _totalWeight = _customers.Sum(x => x.Weight);

            if (_customers.Length == 0)
            {
                throw new ArgumentException("At least one customer needs a weight above 0.", nameof(options));
            }
        }

        public CustomerOptions Pick(Random random)
        {
            var point = random.NextDouble() * _totalWeight;

            foreach (var customer in _customers)
            {
                point -= customer.Weight;

                if (point < 0)
                {
                    return customer;
                }
            }

            return _customers[^1];
        }
    }
}
