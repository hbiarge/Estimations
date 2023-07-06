using System.ComponentModel.DataAnnotations;

namespace Acheve.External.ImageProcess.Features
{
    public class ImageProcessModel
    {
        [Required]
        public required string CaseNumber { get; set; }

        [Required]
        public required string ImageId { get; set; }

        [Required]
        public required string CallbackUrl { get; set; }

        [Required]
        public required IFormFile Image { get; set; }
    }
}