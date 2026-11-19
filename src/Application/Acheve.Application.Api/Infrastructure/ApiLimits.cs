using System.Globalization;
using System.Threading.RateLimiting;
using Acheve.Common.Shared;
using Microsoft.AspNetCore.RateLimiting;

namespace Acheve.Application.Api.Infrastructure
{
    /// <summary>The Limits section of the Api's settings. Every value has a default.</summary>
    public sealed class ApiLimitsOptions
    {
        public const string SectionName = "Limits";

        /// <summary>
        /// The largest request body. Every body in the Api is a few URLs or a short text, so the default is small:
        /// the callbacks are anonymous (they carry a signature, not a token) and must not take large bodies.
        /// </summary>
        public long MaxRequestBodyBytes { get; init; } = 1024 * 1024;

        /// <summary>New estimations a customer can ask for per minute.</summary>
        public int CreateEstimationsPerMinute { get; init; } = 120;

        /// <summary>
        /// Status requests a customer can make per minute. A client that polls every second for several
        /// cases at once needs many, so it is far above the limit for new estimations.
        /// </summary>
        public int ReadEstimationsPerMinute { get; init; } = 1200;

        /// <summary>Callbacks per minute from one address (all the external systems usually share one).</summary>
        public int CallbacksPerMinute { get; init; } = 600;
    }

    /// <summary>
    /// Request size and rate limits of the Api. A request over its limit gets 413 (too large) or
    /// 429 with a Retry-After header (too many).
    /// </summary>
    public static class ApiLimits
    {
        public const string CreateEstimation = "create-estimation";
        public const string ReadEstimation = "read-estimation";
        public const string Callbacks = "callbacks";

        public static WebApplicationBuilder AddApiLimits(this WebApplicationBuilder builder)
        {
            var limits = builder.Configuration.GetSection(ApiLimitsOptions.SectionName).Get<ApiLimitsOptions>()
                         ?? new ApiLimitsOptions();

            builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = limits.MaxRequestBodyBytes);

            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.OnRejected = (context, _) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter =
                            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                    }

                    return ValueTask.CompletedTask;
                };

                // Per customer: one customer's burst can't use up what the others can ask for.
                options.AddPolicy(CreateEstimation, context =>
                    PerMinute(CustomerOrAddress(context), limits.CreateEstimationsPerMinute));

                options.AddPolicy(ReadEstimation, context =>
                    PerMinute(CustomerOrAddress(context), limits.ReadEstimationsPerMinute));

                // Anonymous, so per address. Checked before the signature, so a flood of forged callbacks is cut too.
                options.AddPolicy(Callbacks, context =>
                    PerMinute(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", limits.CallbacksPerMinute));
            });

            return builder;
        }

        /// <summary>
        /// Must come after authorization: the customer is only known once the token has been checked
        /// (the endpoints name the JWT scheme in their policy, there is no default scheme).
        /// </summary>
        public static WebApplication UseApiLimits(this WebApplication app)
        {
            app.UseRateLimiter();

            return app;
        }

        private static string CustomerOrAddress(HttpContext context) =>
            CustomerClaims.CustomerIdOf(context.User)
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        private static RateLimitPartition<string> PerMinute(string key, int permits) =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    }
}
