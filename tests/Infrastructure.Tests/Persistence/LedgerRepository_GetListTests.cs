// T053 (US2) — covers GetByIdAsync + ListAsync against Testcontainers Postgres.
// Asserts owner-scoping, NotFound on missing/cross-owner, deterministic ordering,
// keyset cursor round-trip, and include_archived filter behaviour.

using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Currency;
using SddDemo.Ledger.Domain.Ledgers;
using SddDemo.Ledger.Infrastructure.Currency;
using SddDemo.Ledger.Infrastructure.Migrations;
using SddDemo.Ledger.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Infrastructure.Tests.Persistence;

public class LedgerRepository_GetListTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("ledger")
        .WithUsername("ledger")
        .WithPassword("ledger")
        .Build();

    private NpgsqlDataSource _dataSource = default!;
    private IServiceProvider _services = default!;
    private ILedgerRepository _repo = default!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var cs = _postgres.GetConnectionString();

        var result = SchemaInitializer.Apply(cs);
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"DbUp failed at script '{result.ErrorScript?.Name}'", result.Error);
        }

        _dataSource = NpgsqlDataSource.Create(cs);

        var sc = new ServiceCollection();
        sc.AddSingleton(_dataSource);
        sc.AddSingleton<ICurrencyCatalog, CurrencyCatalog>();
        sc.AddScoped<IAuditRepository, AuditRepository>();
        sc.AddScoped<ILedgerRepository, LedgerRepository>();
        _services = sc.BuildServiceProvider();
        _repo = _services.GetRequiredService<ILedgerRepository>();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task GetByIdAsync_returns_owner_scoped_ledger()
    {
        var owner = Guid.NewGuid();
        var ledger = await SeedAsync(owner, "Operating", LedgerStatus.Active);

        var result = await _repo.GetByIdAsync(ledger.Id, owner, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Operating");
    }

    [Fact]
    public async Task GetByIdAsync_returns_NotFound_when_ledger_missing()
    {
        var owner = Guid.NewGuid();

        var result = await _repo.GetByIdAsync(Guid.NewGuid(), owner, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ledger.not_found");
    }

    [Fact]
    public async Task GetByIdAsync_returns_NotFound_for_cross_owner_read_no_information_leak()
    {
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var ledger = await SeedAsync(ownerA, "Operating", LedgerStatus.Active);

        var result = await _repo.GetByIdAsync(ledger.Id, ownerB, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ledger.not_found");
    }

    [Fact]
    public async Task ListAsync_returns_results_in_deterministic_order_and_excludes_archived_by_default()
    {
        var owner = Guid.NewGuid();
        await SeedAsync(owner, "Alpha", LedgerStatus.Active);
        await Task.Delay(10);
        await SeedAsync(owner, "Beta", LedgerStatus.Active);
        await Task.Delay(10);
        await SeedAsync(owner, "Archived", LedgerStatus.Archived);

        var result = await _repo.ListAsync(owner, includeArchived: false, null, pageSize: 50, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var page = result.Value!;
        page.Items.Should().HaveCount(2);
        // Latest LastModifiedAt first.
        page.Items[0].Name.Should().Be("Beta");
        page.Items[1].Name.Should().Be("Alpha");
        page.NextPageCursor.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_includes_archived_when_requested()
    {
        var owner = Guid.NewGuid();
        await SeedAsync(owner, "Active", LedgerStatus.Active);
        await SeedAsync(owner, "Archived", LedgerStatus.Archived);

        var result = await _repo.ListAsync(owner, includeArchived: true, null, pageSize: 50, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAsync_keyset_cursor_round_trips_two_pages()
    {
        var owner = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            await SeedAsync(owner, $"Ledger-{i}", LedgerStatus.Active);
            await Task.Delay(5);
        }

        var firstPage = await _repo.ListAsync(owner, false, null, pageSize: 2, CancellationToken.None);
        firstPage.IsSuccess.Should().BeTrue();
        firstPage.Value!.Items.Should().HaveCount(2);
        firstPage.Value.NextPageCursor.Should().NotBeNullOrWhiteSpace();

        var secondPage = await _repo.ListAsync(
            owner, false, firstPage.Value.NextPageCursor, pageSize: 2, CancellationToken.None);
        secondPage.IsSuccess.Should().BeTrue();
        secondPage.Value!.Items.Should().HaveCount(2);

        var firstIds = firstPage.Value.Items.Select(l => l.Id).ToHashSet();
        var secondIds = secondPage.Value.Items.Select(l => l.Id).ToHashSet();
        firstIds.Overlaps(secondIds).Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_returns_only_owner_scoped_rows()
    {
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        await SeedAsync(ownerA, "A1", LedgerStatus.Active);
        await SeedAsync(ownerB, "B1", LedgerStatus.Active);

        var result = await _repo.ListAsync(ownerA, false, null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(1);
        result.Value.Items[0].Name.Should().Be("A1");
    }

    private async Task<DomainLedger> SeedAsync(Guid ownerId, string name, LedgerStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var ledger = DomainLedger.Builder()
            .WithId(Guid.CreateVersion7())
            .WithOwnerId(ownerId)
            .WithName(name)
            .WithDescription(null)
            .WithCurrencyCode("USD")
            .WithStatus(status)
            .WithVersion(1)
            .WithTimestamps(now, now)
            .Build(_services).Value!;

        var audit = AuditEntry.Builder()
            .WithActorId(ownerId)
            .WithLedgerId(ledger.Id)
            .WithEventType(AuditEventType.Create)
            .WithEventAt(now)
            .WithPayload(JsonSerializer.Serialize(new { ledger.Id, ledger.Name }))
            .Build().Value!;

        var result = await _repo.CreateAsync(ledger, audit, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }
}
