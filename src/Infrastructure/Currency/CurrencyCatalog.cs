using System.Collections.Immutable;
using System.Globalization;
using SddDemo.Ledger.Domain.Currency;

namespace SddDemo.Ledger.Infrastructure.Currency;

/// <summary>
/// research.md §13 — ISO 4217 alphabetic codes are read once at construction
/// from <see cref="RegionInfo.ISOCurrencySymbol"/> across every specific culture
/// and frozen into an immutable hash set. No runtime refresh; a process restart
/// picks up any framework update.
/// </summary>
public sealed class CurrencyCatalog : ICurrencyCatalog
{
    private static readonly ImmutableHashSet<string> Supported = LoadSupported();

    public bool IsSupported(string code) =>
        !string.IsNullOrWhiteSpace(code) && Supported.Contains(code);

    private static ImmutableHashSet<string> LoadSupported()
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                if (!string.IsNullOrWhiteSpace(region.ISOCurrencySymbol))
                {
                    builder.Add(region.ISOCurrencySymbol);
                }
            }
            catch (ArgumentException)
            {
                // Some neutral cultures or custom cultures cannot map to a RegionInfo;
                // skip them — they don't carry a currency code anyway.
            }
        }

        return builder.ToImmutable();
    }
}
