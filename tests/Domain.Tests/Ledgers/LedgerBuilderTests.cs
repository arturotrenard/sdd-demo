// T038 (TDD red) — written before T044 (Ledger.cs lands).
// Asserts the LedgerBuilder happy-path + every documented validation failure
// from data-model.md §1.1 / §3.1.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Currency;
using SddDemo.Ledger.Domain.Ledgers;
using SddDemo.Ledger.Infrastructure.Currency;
using Xunit;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Domain.Tests.Ledgers;

public class LedgerBuilderTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset CreatedAt = new(2026, 4, 27, 12, 0, 0, TimeSpan.Zero);

    private static IServiceProvider Services()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<ICurrencyCatalog, CurrencyCatalog>();
        return sc.BuildServiceProvider();
    }

    private static DomainLedger.LedgerBuilder ValidBuilder() =>
        DomainLedger.Builder()
            .WithId(Guid.CreateVersion7())
            .WithOwnerId(OwnerId)
            .WithName("Operating Account")
            .WithDescription("Primary operating ledger")
            .WithCurrencyCode("USD")
            .WithStatus(LedgerStatus.Active)
            .WithVersion(1)
            .WithTimestamps(CreatedAt, CreatedAt);

    [Fact]
    public void Build_returns_success_for_valid_input()
    {
        var result = ValidBuilder().Build(Services());

        result.IsSuccess.Should().BeTrue();
        var ledger = result.Value!;
        ledger.Name.Should().Be("Operating Account");
        ledger.OwnerId.Should().Be(OwnerId);
        ledger.CurrencyCode.Should().Be("USD");
        ledger.Status.Should().Be(LedgerStatus.Active);
        ledger.Version.Should().Be(1);
    }

    [Fact]
    public void Build_returns_failure_when_name_is_whitespace_only()
    {
        var result = ValidBuilder().WithName("   ").Build(Services());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("validation");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Build_returns_failure_when_currency_unsupported()
    {
        var result = ValidBuilder().WithCurrencyCode("XYZ").Build(Services());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("validation");
    }

    [Fact]
    public void Build_returns_failure_when_description_exceeds_500_chars()
    {
        var oversize = new string('x', 501);

        var result = ValidBuilder().WithDescription(oversize).Build(Services());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("validation");
    }

    [Fact]
    public void Build_returns_failure_when_CreatedAt_is_after_LastModifiedAt()
    {
        var later = CreatedAt.AddSeconds(10);

        var result = ValidBuilder().WithTimestamps(later, CreatedAt).Build(Services());

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Contain("LastModifiedAt");
    }

    [Fact]
    public void Build_returns_failure_when_version_is_zero()
    {
        var result = ValidBuilder().WithVersion(0).Build(Services());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("validation");
    }

    [Fact]
    public void Build_returns_failure_when_name_exceeds_100_chars()
    {
        var oversize = new string('x', 101);

        var result = ValidBuilder().WithName(oversize).Build(Services());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("validation");
    }
}
