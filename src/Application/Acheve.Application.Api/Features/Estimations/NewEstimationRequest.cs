using System.ComponentModel.DataAnnotations;

namespace Acheve.Application.Api.Features.Estimations
{
    public class NewEstimationRequest
    {
        [Required]
        [Url]
        public required string CallBackUri { get; init; }

        [Required]
        [MinLength(1)]
        [MaxLength(10)]
        public required string[] ImageUrls { get; init; }
    }
}