using Acheve.Common.Shared;
using Microsoft.Extensions.Time.Testing;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public class NotificationSignerTests
    {
        private const string Body = "{\"CaseNumber\":\"1\",\"Result\":\"Success\"}";

        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        private readonly NotificationSigner _sut;

        public NotificationSignerTests()
        {
            _sut = new NotificationSigner("notification-signing-key-with-enough-length", _time);
        }

        [Fact]
        public void A_signed_body_is_valid()
        {
            var (timestamp, signature) = _sut.Sign(Body);

            signature.ShouldStartWith(NotificationSigner.SignaturePrefix);
            _sut.IsValid(Body, timestamp, signature).ShouldBeTrue();
        }

        [Fact]
        public void A_changed_body_or_timestamp_is_rejected()
        {
            var (timestamp, signature) = _sut.Sign(Body);

            _sut.IsValid(Body + " ", timestamp, signature).ShouldBeFalse();
            _sut.IsValid(Body, (long.Parse(timestamp) + 1).ToString(), signature).ShouldBeFalse();
        }

        [Fact]
        public void An_old_notification_is_rejected()
        {
            var (timestamp, signature) = _sut.Sign(Body);

            _time.Advance(TimeSpan.FromMinutes(4));
            _sut.IsValid(Body, timestamp, signature).ShouldBeTrue();

            _time.Advance(TimeSpan.FromMinutes(2));
            _sut.IsValid(Body, timestamp, signature).ShouldBeFalse();
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("not-a-number", "sha256=abc")]
        public void Missing_or_malformed_headers_are_rejected(string? timestamp, string? signature)
        {
            _sut.IsValid(Body, timestamp, signature).ShouldBeFalse();
        }

        [Fact]
        public void A_signature_from_another_key_is_rejected()
        {
            var other = new NotificationSigner("another-notification-key-with-enough-length", _time);
            var (timestamp, signature) = other.Sign(Body);

            _sut.IsValid(Body, timestamp, signature).ShouldBeFalse();
        }
    }
}
