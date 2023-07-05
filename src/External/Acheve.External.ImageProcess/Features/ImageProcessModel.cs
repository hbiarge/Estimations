using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Acheve.External.ImageProcess.Features
{
    public class ImageProcessModel
    {
        [FromForm(Name = "CaseNumber")]
        public string? CaseNumber { get; set; }

        [FromForm(Name = "ImageId")]
        public string? ImageId { get; set; }

        [FromForm(Name = "CallbackUrl")]
        public string? CallbackUrl { get; set; }

        [FromForm(Name = "Image")]
        public IFormFile? Image { get; set; }
    }
}