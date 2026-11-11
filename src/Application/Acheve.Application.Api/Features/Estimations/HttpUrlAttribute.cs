using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace Acheve.Application.Api.Features.Estimations
{
    /// <summary>
    /// The value, or every item when it is a list, must be an absolute http or https URL.
    /// Unlike [Url] it rejects ftp, and it rejects null or empty items in a list.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class HttpUrlAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            var name = validationContext.MemberName ?? validationContext.DisplayName;

            switch (value)
            {
                // [Required] reports a missing value.
                case null:
                    return ValidationResult.Success;

                case string url:
                    return IsHttpUrl(url) ? ValidationResult.Success : Invalid($"The field {name} must be an absolute http or https URL.");

                case IEnumerable items:
                    var index = 0;

                    foreach (var item in items)
                    {
                        if (!IsHttpUrl(item as string))
                        {
                            return Invalid($"The item {index} of the field {name} must be an absolute http or https URL.");
                        }

                        index++;
                    }

                    return ValidationResult.Success;

                default:
                    return Invalid($"The field {name} must be a URL.");
            }

            ValidationResult Invalid(string message) => new(message, [name]);
        }

        public static bool IsHttpUrl(string? value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
