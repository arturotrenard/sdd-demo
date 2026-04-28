using System.ComponentModel.DataAnnotations;

namespace SddDemo.Ledger.Api.Configuration;

/// <summary>
/// Bound from configuration section <c>Ledger</c> via Options pattern with
/// <c>ValidateDataAnnotations()</c> + <c>ValidateOnStart()</c> per Constitution
/// Principle IV. **No secret-shaped values here** — Constitution Principle V > Always-on
/// requires secrets to flow from User Secrets (Development) or env vars / on-prem
/// secret manager (deployed environments). The only secrets in this feature today are
/// connection strings, which the AppHost injects via <c>ConnectionStrings:*</c>.
/// </summary>
public sealed class LedgerOptions
{
    public const string SectionName = "Ledger";

    [Range(1, 200)]
    public int DefaultPageSize { get; set; } = 50;

    [Range(1, 200)]
    public int MaxPageSize { get; set; } = 200;
}
