namespace Acheve.Application.Api.Features.Estimations
{
    public class NewEstimationResponse
    {
        public required string Token { get; init; }

        public required string OperationId { get; init; }
    }
}