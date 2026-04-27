using System.ComponentModel.DataAnnotations;

namespace SddDemo.Ledger.Domain.Common;

/// <summary>
/// Constitution Principle VI > Validation — single entry point for declarative
/// Data-Annotation + IValidatableObject validation across every DTO-shaped type
/// (commands, queries, translation maps, options, Domain types). Imperative
/// if-validation in DTO-bound code is a review blocker — call this instead.
/// </summary>
public static class DomainValidator
{
    public static Result<T> Validate<T>(T candidate, IServiceProvider? services = null)
        where T : notnull
    {
        var context = new ValidationContext(candidate, serviceProvider: services, items: null);
        var results = new List<ValidationResult>();

        var ok = Validator.TryValidateObject(
            candidate,
            context,
            results,
            validateAllProperties: true);

        return ok
            ? Result<T>.Success(candidate)
            : Result<T>.Failure(Aggregate(results));
    }

    private static Error Aggregate(IReadOnlyCollection<ValidationResult> results)
    {
        var message = string.Join(
            "; ",
            results.Select(r =>
            {
                var members = r.MemberNames.Any()
                    ? string.Join(",", r.MemberNames)
                    : "(unknown)";
                return $"{members}: {r.ErrorMessage}";
            }));

        return new Error("validation", message, ErrorType.Validation);
    }
}
