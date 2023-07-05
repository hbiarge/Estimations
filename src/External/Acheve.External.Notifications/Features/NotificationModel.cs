using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Acheve.External.Notifications.Features
{
    public class NotificationModel
    {
        [Required]
        public string CaseNumber { get; set; } = string.Empty;
            
        [Required]
        public string Result { get; set; } = string.Empty;

        public bool Success => Result == "Success";

        [MemberNotNullWhen(false, nameof(Success))]
        public string? FailureReason { get; set; }

        [MemberNotNullWhen(true, nameof(Success))]
        public string? Estimation { get; set; }
    }
}