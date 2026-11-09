using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Acheve.Application.Api.Infrastructure
{
    /// <summary>
    /// Gives every unary call a deadline when the caller did not set one.
    /// The deadline covers all retry attempts, like the total request timeout of the standard HTTP resilience handler.
    /// </summary>
    public sealed class DefaultDeadlineInterceptor : Interceptor
    {
        private readonly TimeSpan _timeout;

        public DefaultDeadlineInterceptor(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            if (context.Options.Deadline is null)
            {
                context = new ClientInterceptorContext<TRequest, TResponse>(
                    context.Method,
                    context.Host,
                    context.Options.WithDeadline(DateTime.UtcNow.Add(_timeout)));
            }

            return continuation(request, context);
        }
    }
}
