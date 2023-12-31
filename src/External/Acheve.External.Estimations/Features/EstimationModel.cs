﻿using System.ComponentModel.DataAnnotations;

namespace Acheve.External.Estimations.Features
{
    public class EstimationModel
    {
        [Required]
        public string CaseNumber { get; set; } = string.Empty;

        [Required]
        public string CallbackUrl { get; set; } = string.Empty;

        [Required]
        public ICollection<ImageMetadata>? Metadata { get; set; }
    }

    public class ImageMetadata
    {
        public int ImageId { get; set; }

        public string? Metadata { get; set; }
    }
}