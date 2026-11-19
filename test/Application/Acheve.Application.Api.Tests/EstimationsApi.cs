#pragma warning disable CS8610, CS8765, CS8625 // The generated gRPC client is not nullable-annotated.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Acheve.Application.Api.Features.Estimations;
using Acheve.Common.Shared;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using Rebus.Bus;
using Rebus.TestHelpers;
using StateHolder;

namespace Acheve.Application.Api.Tests
{
    /// <summary>
    /// The Api's /estimations endpoints, as they are mapped in production, on a real Kestrel server on a free port.
    /// Only what is outside the Api is replaced: the token check (a header names the customer), the state holder
    /// (an in-memory copy that, like the real one, finds a case by ticket and customer) and the bus.
    /// </summary>
    internal sealed class EstimationsApi : IAsyncDisposable
    {
        /// <summary>Sent as a header, it logs the caller in as that customer.</summary>
        public const string CustomerHeader = "X-Test-Customer";

        /// <summary>A valid token that has no customer claim.</summary>
        public const string NoCustomer = "<no customer>";

        private readonly WebApplication _app;

        private EstimationsApi(WebApplication app, HttpClient client, FakeStateHolder stateHolder, FakeBus bus)
        {
            _app = app;
            Client = client;
            StateHolder = stateHolder;
            Bus = bus;
        }

        public HttpClient Client { get; }

        public FakeStateHolder StateHolder { get; }

        public FakeBus Bus { get; }

        public static async Task<EstimationsApi> StartAsync()
        {
            var stateHolder = new FakeStateHolder();
            var bus = new FakeBus();

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            // Takes the place of the JWT bearer scheme the endpoints' policy asks for.
            builder.Services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(JwtBearerDefaults.AuthenticationScheme, null);
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton<StateHolderService.StateHolderServiceClient>(stateHolder);
            builder.Services.AddSingleton<IBus>(bus);

            var app = builder.Build();

            app.UseAuthentication();
            app.UseAuthorization();
            app.MapEstimations();

            await app.StartAsync(TestContext.Current.CancellationToken);

            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

            return new EstimationsApi(app, new HttpClient { BaseAddress = new Uri(address) }, stateHolder, bus);
        }

        public HttpRequestMessage Request(HttpMethod method, string url, string? customer = null, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, url) { Content = content };

            if (customer is not null)
            {
                request.Headers.Add(CustomerHeader, customer);
            }

            return request;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }

        private sealed class HeaderAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
        {
            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                if (!Request.Headers.TryGetValue(CustomerHeader, out var customer))
                {
                    return Task.FromResult(AuthenticateResult.NoResult());
                }

                var claims = customer == NoCustomer
                    ? []
                    : new[] { new Claim(CustomerClaims.ClaimType, customer.ToString()) };

                var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));

                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
            }
        }

        /// <summary>
        /// Stands for the state holder: it keeps the cases by ticket and customer, and answers only the customer's own,
        /// like the real one. It remembers who each query was made for.
        /// </summary>
        public sealed class FakeStateHolder : StateHolderService.StateHolderServiceClient
        {
            private readonly Dictionary<(string Ticket, string CustomerId), StateResponse> _cases = [];

            public List<StateRequest> Queries { get; } = [];

            public void Add(string ticket, string customerId, string state, string? result = null, string? failureReason = null) =>
                _cases[(ticket, customerId)] = new StateResponse
                {
                    Ticket = ticket,
                    Found = true,
                    State = state,
                    Result = result ?? string.Empty,
                    FailureReason = failureReason ?? string.Empty
                };

            public override AsyncUnaryCall<StateResponse> StateQueryAsync(
                StateRequest request, Metadata headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
            {
                Queries.Add(request);

                var response = _cases.GetValueOrDefault((request.Ticket, request.CustomerId))
                               ?? new StateResponse { Ticket = request.Ticket, Found = false };

                return new AsyncUnaryCall<StateResponse>(
                    Task.FromResult(response),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { });
            }
        }
    }
}
