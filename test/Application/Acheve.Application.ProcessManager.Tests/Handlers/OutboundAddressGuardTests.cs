using System.Net;
using System.Net.Sockets;
using System.Text;
using Acheve.Common.Shared;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public class OutboundAddressGuardTests
    {
        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("10.1.2.3")]
        [InlineData("172.16.0.1")]
        [InlineData("172.31.255.255")]
        [InlineData("192.168.1.1")]
        [InlineData("169.254.169.254")] // cloud metadata
        [InlineData("100.64.0.1")]
        [InlineData("0.0.0.0")]
        [InlineData("224.0.0.1")]
        [InlineData("255.255.255.255")]
        [InlineData("::1")]
        [InlineData("::")]
        [InlineData("fe80::1")]
        [InlineData("fd00:ec2::254")]
        [InlineData("ff02::1")]
        [InlineData("2001:db8::1")]
        [InlineData("::ffff:127.0.0.1")]  // IPv4 inside IPv6
        [InlineData("::ffff:10.0.0.1")]
        [InlineData("64:ff9b::7f00:1")]   // NAT64 to 127.0.0.1
        [InlineData("2002:a00:1::")]      // 6to4 to 10.0.0.1
        public void Rejects_an_address_that_is_not_on_the_public_internet(string address)
        {
            OutboundAddressGuard.IsPublic(IPAddress.Parse(address)).ShouldBeFalse();
        }

        [Theory]
        [InlineData("8.8.8.8")]
        [InlineData("1.1.1.1")]
        [InlineData("172.15.0.1")]
        [InlineData("172.32.0.1")]
        [InlineData("100.63.255.255")]
        [InlineData("100.128.0.1")]
        [InlineData("2606:4700:4700::1111")]
        [InlineData("::ffff:8.8.8.8")]
        [InlineData("64:ff9b::808:808")]
        public void Accepts_a_public_address(string address)
        {
            OutboundAddressGuard.IsPublic(IPAddress.Parse(address)).ShouldBeTrue();
        }

        [Fact]
        public async Task Refuses_to_connect_to_a_private_address()
        {
            using var client = new HttpClient(OutboundAddressGuard.CreateHandler(allowPrivateAddresses: false));

            var error = await Should.ThrowAsync<HttpRequestException>(
                () => client.GetAsync("http://127.0.0.1:1/", TestContext.Current.CancellationToken));

            error.InnerException.ShouldBeOfType<InvalidOperationException>();
        }

        [Fact]
        public async Task Refuses_a_name_that_resolves_to_a_private_address()
        {
            using var client = new HttpClient(OutboundAddressGuard.CreateHandler(allowPrivateAddresses: false));

            var error = await Should.ThrowAsync<HttpRequestException>(
                () => client.GetAsync("http://localhost:1/", TestContext.Current.CancellationToken));

            error.InnerException.ShouldBeOfType<InvalidOperationException>();
        }

        [Fact]
        public async Task Connects_to_a_private_address_when_they_are_allowed()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var server = Task.Run(async () =>
            {
                var token = TestContext.Current.CancellationToken;
                using var connection = await listener.AcceptTcpClientAsync(token);
                var stream = connection.GetStream();
                await stream.ReadAtLeastAsync(new byte[1024], 1, cancellationToken: token);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n"), token);
            }, TestContext.Current.CancellationToken);

            using var client = new HttpClient(OutboundAddressGuard.CreateHandler(allowPrivateAddresses: true));

            using var response = await client.GetAsync(
                $"http://127.0.0.1:{port}/", TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await server;
        }
    }
}
