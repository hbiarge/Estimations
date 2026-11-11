using System.ComponentModel.DataAnnotations;

namespace Acheve.Application.Api.Features.Estimations
{
    public class NewEstimationRequest
    {
        [Required]
        [HttpUrl]
        public required string CallBackUri { get; init; }

        [Required]
        [MinLength(1)]
        [MaxLength(10)]
        [HttpUrl]
        public required string[] ImageUrls { get; init; }
    }
}
