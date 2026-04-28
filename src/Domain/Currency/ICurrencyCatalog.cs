namespace SddDemo.Ledger.Domain.Currency;

/// <summary>
/// FR-004 — supported ISO 4217 alphabetic currency codes.
/// The runtime implementation lives in Infrastructure (CurrencyCatalog) per Principle VI
/// (Domain owns the abstraction; Infrastructure implements it).
/// </summary>
public interface ICurrencyCatalog
{
    bool IsSupported(string code);
}
