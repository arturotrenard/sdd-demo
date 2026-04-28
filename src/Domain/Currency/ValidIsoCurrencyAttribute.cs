using System.ComponentModel.DataAnnotations;

namespace SddDemo.Ledger.Domain.Currency;

/// <summary>
/// Data Annotation form of FR-004 — applied to every DTO-shaped property
/// carrying a currency code. DomainValidator.Validate routes the
/// ValidationContext.ServiceProvider through to <see cref="GetValidationResult"/>
/// so the catalog can be resolved from DI.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class ValidIsoCurrencyAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        if (value is null)
        {
            return ValidationResult.Success;
        }

        if (value is not string code)
        {
            return new ValidationResult(
                $"{validationContext.MemberName} must be a string ISO 4217 code.",
                memberNames: validationContext.MemberName is null
                    ? Array.Empty<string>()
                    : [validationContext.MemberName]);
        }

        var catalog = validationContext.GetService(typeof(ICurrencyCatalog)) as ICurrencyCatalog;

        // Defensive — when the catalog is missing from DI we cannot validate the code.
        // Treat as a validation failure rather than throwing (Principle VI no-exceptions rule).
        if (catalog is null)
        {
            return new ValidationResult(
                "Currency catalog is not registered; cannot validate currency code.",
                memberNames: validationContext.MemberName is null
                    ? Array.Empty<string>()
                    : [validationContext.MemberName]);
        }

        return catalog.IsSupported(code)
            ? ValidationResult.Success
            : new ValidationResult(
                $"Currency code '{code}' is not a supported ISO 4217 alphabetic code.",
                memberNames: validationContext.MemberName is null
                    ? Array.Empty<string>()
                    : [validationContext.MemberName]);
    }
}
