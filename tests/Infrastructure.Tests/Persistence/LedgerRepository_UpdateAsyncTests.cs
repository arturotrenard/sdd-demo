// T063 (US3) — covers LedgerRepository.UpdateAsync against Testcontainers Postgres.
// Asserts version increment, audit row in same tx within 5 s (SC-005), Conflict
// on stale expected version (no audit row written), and NameAlreadyExists on
// duplicate-rename collision.

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

public class LedgerRepository_UpdateAsyncTests : IAsyncLifetime
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
        var apply = SchemaInitializer.Apply(cs);
        if (!apply.Successful)
        {
            throw new InvalidOperationException(
                $"DbUp failed at script '{apply.ErrorScript?.Name}'", apply.Error);
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
    public async Task UpdateAsync_increments_version_and_writes_one_update_audit_row_within_5s()
    {
        var owner = Guid.NewGuid();
        var seeded = await SeedAsync(owner, "Original");

        var operationStart = DateTimeOffset.UtcNow;

        var renamed = WithName(seeded, "Renamed", version: seeded.Version + 1);
        var audit = NewAudit(renamed, AuditEventType.Update);

        var result = await _repo.UpdateAsync(renamed, seeded.Version, audit, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Version.Should().Be(seeded.Version + 1);

        await using var conn = await _dataSource.OpenConnectionAsync();
        var auditRows = (await conn.QueryAsync<(short event_type, DateTimeOffset event_at)>(
            "SELECT event_type, event_at FROM ledger_audit WHERE ledger_id = @Id AND event_type = 2",
            new { seeded.Id })).ToList();

        auditRows.Should().HaveCount(1);
        (auditRows[0].event_at - operationStart).Should().BeLessThan(
            TimeSpan.FromSeconds(5),
            "SC-005 — audit row visible within 5 s");
    }

    [Fact]
    public async Task UpdateAsync_returns_Conflict_on_stale_expected_and_writes_no_audit_row()
    {
        var owner = Guid.NewGuid();
        var seeded = await SeedAsync(owner, "X");

        var renamed = WithName(seeded, "X2", version: seeded.Version + 1);
        var staleExpected = seeded.Version + 99;

        var result = await _repo.UpdateAsync(
            renamed, staleExpected, NewAudit(renamed, AuditEventType.Update), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ledger.conflict");

        await using var conn = await _dataSource.OpenConnectionAsync();
        var updateAuditCount = await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM ledger_audit WHERE ledger_id = @Id AND event_type = 2",
            new { seeded.Id });
        updateAuditCount.Should().Be(0, "atomic same-tx — no audit row when update is rejected");
    }

    [Fact]
    public async Task UpdateAsync_returns_NameAlreadyExists_on_rename_to_duplicate()
    {
        var owner = Guid.NewGuid();
        await SeedAsync(owner, "Alpha");
        var second = await SeedAsync(owner, "Beta");

        var collide = WithName(second, "alpha", version: second.Version + 1);
        var result = await _repo.UpdateAsync(
            collide, second.Version, NewAudit(collide, AuditEventType.Update), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ledger.name_already_exists");
    }

    private async Task<DomainLedger> SeedAsync(Guid ownerId, string name)
    {
        var now = DateTimeOffset.UtcNow;
        var ledger = DomainLedger.Builder()
            .WithId(Guid.CreateVersion7())
            .WithOwnerId(ownerId)
            .WithName(name)
            .WithDescription(null)
            .WithCurrencyCode("USD")
            .WithStatus(LedgerStatus.Active)
            .WithVersion(1)
            .WithTimestamps(now, now)
            .Build(_services).Value!;

        var result = await _repo.CreateAsync(ledger, NewAudit(ledger, AuditEventType.Create), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    private DomainLedger WithName(DomainLedger source, string name, long version) =>
        DomainLedger.Builder()
            .WithId(source.Id)
            .WithOwnerId(source.OwnerId)
            .WithName(name)
            .WithDescription(source.Description)
            .WithCurrencyCode(source.CurrencyCode)
            .WithStatus(source.Status)
            .WithVersion(version)
            .WithTimestamps(source.CreatedAt, DateTimeOffset.UtcNow)
            .Build(_services).Value!;

    private static AuditEntry NewAudit(DomainLedger ledger, AuditEventType type) =>
        AuditEntry.Builder()
            .WithActorId(ledger.OwnerId)
            .WithLedgerId(ledger.Id)
            .WithEventType(type)
            .WithEventAt(DateTimeOffset.UtcNow)
            .WithPayload(JsonSerializer.Serialize(new { ledger.Id, ledger.Name, ledger.Version }))
            .Build().Value!;
}
