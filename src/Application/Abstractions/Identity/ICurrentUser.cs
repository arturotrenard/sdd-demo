using SddDemo.Ledger.Domain.Common;

namespace SddDemo.Ledger.Application.Abstractions.Identity;

/// <summary>
/// research.md §7 — Architectural-readiness stub. Authn/authz are deferred per
/// Constitution Principle V > Deferral; this interface is REQUIRED today because
/// FR-009 (owner scoping) and FR-012 (audit actor) differentiate by caller identity.
/// The future swap to a real authenticated implementation is a configuration change,
/// not a handler diff.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// The stable owner identifier resolved for the current request, or a Failure
    /// (ErrorType.Validation) when the trusted-gateway header is missing/unparseable.
    /// Authn/authz are deferred (Constitution Principle V) — this is malformed input,
    /// not an auth failure.
    /// </summary>
    Result<Guid> ResolveOwnerId();
}
