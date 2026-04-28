// T015 (TDD red) — written before T016/T017/T018 (Currency types land).
// MUST fail to build until SddDemo.Ledger.Infrastructure.Currency.CurrencyCatalog exists.

using FluentAssertions;
using SddDemo.Ledger.Infrastructure.Currency;
using Xunit;

namespace SddDemo.Ledger.Infrastructure.Tests.Currency;

public class CurrencyCatalogTests
{
    private readonly CurrencyCatalog _catalog = new();

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("JPY")]
    [InlineData("GBP")]
    [InlineData("CAD")]
    public void IsSupported_returns_true_for_common_iso_codes(string code)
    {
        _catalog.IsSupported(code).Should().BeTrue();
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("ABC")]
    [InlineData("ZZZ")]
    [InlineData("usd")]                    // lower-case is not the canonical ISO 4217 form
    public void IsSupported_returns_false_for_unknown_codes(string code)
    {
        _catalog.IsSupported(code).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void IsSupported_returns_false_for_empty_or_null(string? code)
    {
        _catalog.IsSupported(code!).Should().BeFalse();
    }
}
