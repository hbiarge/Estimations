using System.Net;
using Acheve.Application.CallbackNotifier;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.TestHelpers;
using Rebus.TestHelpers.Events;

namespace Acheve.Application.CallbackNotifier.Tests
{
    public class CustomerNotifierTests
    {
        private static readonly Guid CaseNumber = Guid.Parse("11111111-1111-1111-1111-111111111111");

        private readonly FakeBus _bus = new();
        private readonly List<HttpRequestMessage> _requests = [];
        private readonly List<string> _bodies = [];

        private sealed class StubHttpClientFactory(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : IHttpClientFactory
        {
            private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
            {
                protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                    send(request);
            }

            public HttpClient CreateClient(string name) => new(new Handler(send));
        }

        private CustomerNotifier CreateSut(CustomerNotificationSigners signers) =>
            new(
                _bus,
                new StubHttpClientFactory(async request =>
                {
                    _requests.Add(request);
                    _bodies.Add(await request.Content!.ReadAsStringAsync());

                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }),
                signers,
                NullLogger<CustomerNotifier>.Instance);

        private static CustomerNotificationSigners Signers(bool allowUnsigned, params (string Customer, string Key)[] keys) =>
            new(keys.Select(x => new KeyValuePair<string, string?>(x.Customer, x.Key)), allowUnsigned);

        private static Task<object> Payload() => Task.FromResult<object>(new { CaseNumber, Result = "Success" });

        [Fact]
        public async Task Signs_the_notification_with_the_key_of_its_customer()
        {
            var acme = new NotificationSigner("acme-key-long-enough-for-the-test-0000");
            var globex = new NotificationSigner("globex-key-long-enough-for-the-test-00");
            var sut = CreateSut(Signers(false,
                ("acme", "acme-key-long-enough-for-the-test-0000"),
                ("globex", "globex-key-long-enough-for-the-test-00")));

            await sut.Notify(CaseNumber, "acme", "https://acme.example/notify", 1, Payload);

            var request = _requests.ShouldHaveSingleItem();
            var timestamp = request.Headers.GetValues(NotificationSigner.TimestampHeader).Single();
            var signature = request.Headers.GetValues(NotificationSigner.SignatureHeader).Single();

            acme.IsValid(_bodies[0], timestamp, signature).ShouldBeTrue();
            // Another customer's key doesn't check it: one customer can't forge another's notifications.
            globex.IsValid(_bodies[0], timestamp, signature).ShouldBeFalse();
            _bus.Events.OfType<MessageSent<NotificationCompleted>>().ShouldHaveSingleItem();
        }

        [Fact]
        public async Task Does_not_send_to_a_customer_without_a_key_outside_development()
        {
            var sut = CreateSut(Signers(false, ("acme", "acme-key-long-enough-for-the-test-0000")));

            await sut.Notify(CaseNumber, "initech", "https://initech.example/notify", 1, Payload);

            _requests.ShouldBeEmpty();
            var failure = _bus.Events.OfType<MessageSent<UnableToNotify>>().ShouldHaveSingleItem();
            failure.CommandMessage.Error.ShouldContain("initech");
            _bus.Events.OfType<MessageSent<NotificationCompleted>>().ShouldBeEmpty();
        }

        [Fact]
        public async Task Sends_unsigned_to_a_customer_without_a_key_in_development()
        {
            var sut = CreateSut(Signers(true));

            await sut.Notify(CaseNumber, "acme", "https://acme.example/notify", 1, Payload);

            var request = _requests.ShouldHaveSingleItem();
            request.Headers.Contains(NotificationSigner.SignatureHeader).ShouldBeFalse();
            _bus.Events.OfType<MessageSent<NotificationCompleted>>().ShouldHaveSingleItem();
        }
    }
}
