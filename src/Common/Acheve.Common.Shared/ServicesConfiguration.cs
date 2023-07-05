using Microsoft.Extensions.Options;

namespace Acheve.Common.Shared
{
    public class ServicesConfiguration
    {
        public HostInfo? Api { get; set; }
        public HostInfo? Images { get; set; }
        public HostInfo? ImageProcess { get; set; }
        public HostInfo? Estimations { get; set; }
        public HostInfo? Notifications { get; set; }
        public HostInfo? StateHolder { get; set; }

        public class HostInfo
        {
            private HostInfo()
            {             
            }

            public string Protocol { get; init; } = "https";

            public string Host { get; init; } = "localhost";

            public string Port { get; init; } = "5000";

            public string BaseUrl => $"{Protocol}{Uri.SchemeDelimiter}{Host}:{Port}";

            
            public static HostInfo Create(string url)
            {
                var uri = new Uri(url);

                return new HostInfo
                {
                    Protocol = uri.Scheme,
                    Host = uri.Host,
                    Port = uri.Port.ToString("D")
                };
            }
        }
    }

    public class ServicesPostConfiguration : IPostConfigureOptions<ServicesConfiguration>
    {
        private const string BaseUri = "https://localhost";

        public void PostConfigure(string? name, ServicesConfiguration options)
        {
            options.Api ??= ServicesConfiguration.HostInfo.Create($"{BaseUri}:5000");
            options.Images ??= ServicesConfiguration.HostInfo.Create($"{BaseUri}:5001");
            options.ImageProcess ??= ServicesConfiguration.HostInfo.Create($"{BaseUri}:5002");
            options.Estimations ??= ServicesConfiguration.HostInfo.Create($"{BaseUri}:5003");
            options.Notifications ??= ServicesConfiguration.HostInfo.Create($"{BaseUri}:5004");
            options.StateHolder ??= ServicesConfiguration.HostInfo.Create($"{BaseUri}:5005");
        }
    }
}