using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SddDemo.Ledger.Application.Abstractions.Identity;
using SddDemo.Ledger.Domain.Common;

namespace SddDemo.Ledger.Infrastructure.Identity;

/// <summary>
/// research.md §7 + spec.md FR-010 clarification — reads the trusted-gateway-supplied
/// <c>X-Owner-Id</c> header in non-Development environments, and falls back to a configured
/// fixed developer UUID in Development. Authn/authz are deferred (Constitution Principle V),
/// so a missing/unparseable header is treated as malformed input and returns
/// Failure(ErrorType.Validation) — there is no auth concept in this service today.
/// </summary>
public sealed class AnonymousCurrentUser(
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment environment,
    IOptions<AnonymousCurrentUserOptions> options) : ICurrentUser
{
    public const string OwnerHeaderName = "X-Owner-Id";

    private static readonly Error MissingOwner = new(
        "identity.missing_owner",
        $"No '{OwnerHeaderName}' header on the incoming request.",
        ErrorType.Validation);

    private static readonly Error InvalidOwner = new(
        "identity.invalid_owner",
        $"'{OwnerHeaderName}' header is not a valid UUID.",
        ErrorType.Validation);

    public Result<Guid> ResolveOwnerId()
    {
        var ctx = httpContextAccessor.HttpContext;

        if (ctx is not null && ctx.Request.Headers.TryGetValue(OwnerHeaderName, out var headerValues))
        {
            var raw = headerValues.ToString();
            return Guid.TryParse(raw, out var parsed)
                ? Result<Guid>.Success(parsed)
                : Result<Guid>.Failure(InvalidOwner);
        }

        // Development fallback only — never in deployed environments.
        if (environment.IsDevelopment() && options.Value.DevOwnerId is { } dev)
        {
            return Result<Guid>.Success(dev);
        }

        return Result<Guid>.Failure(MissingOwner);
    }
}

/// <summary>
/// Bound from configuration section <c>Identity</c> via Options pattern with
/// <c>ValidateDataAnnotations</c> (Constitution Principle IV).
/// </summary>
public sealed class AnonymousCurrentUserOptions
{
    /// <summary>
    /// Development-only fallback owner UUID. MUST be unset in deployed environments.
    /// Secrets-shaped values (none today) MUST come from User Secrets / env vars / vault
    /// per Constitution Principle V > Always-on.
    /// </summary>
    public Guid? DevOwnerId { get; set; }
}
