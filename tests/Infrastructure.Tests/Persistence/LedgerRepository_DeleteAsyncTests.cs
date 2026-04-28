// T071 (US4) — covers LedgerRepository.DeleteAsync against Testcontainers Postgres.
// Asserts row removal, audit row in same tx within 5 s (SC-005), audit row remains
// after delete (FR-008), and that re-creating a ledger with the same name succeeds
// immediately afterward.

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

public class LedgerRepository_DeleteAsyncTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("ledger").WithUsername("ledger").WithPassword("ledger").Build();

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
    public async Task DeleteAsync_removes_row_and_writes_one_delete_audit_within_5s_audit_remains_after()
    {
        var owner = Guid.NewGuid();
        var seeded = await SeedAsync(owner, "ToDelete");

        var operationStart = DateTimeOffset.UtcNow;
        var audit = NewAudit(seeded, AuditEventType.Delete);

        var result = await _repo.DeleteAsync(seeded.Id, owner, seeded.Version, audit, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await using var conn = await _dataSource.OpenConnectionAsync();
        var ledgerCount = await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM ledger WHERE id = @Id", new { seeded.Id });
        ledgerCount.Should().Be(0);

        var auditRows = (await conn.QueryAsync<(short event_type, DateTimeOffset event_at)>(
            "SELECT event_type, event_at FROM ledger_audit WHERE ledger_id = @Id AND event_type = 3",
            new { seeded.Id })).ToList();

        auditRows.Should().HaveCount(1, "FR-008 — audit row remains after delete");
        (auditRows[0].event_at - operationStart).Should().BeLessThan(
            TimeSpan.FromSeconds(5),
            "SC-005 — audit row visible within 5 s");
    }

    [Fact]
    public async Task DeleteAsync_followed_by_create_with_same_name_succeeds()
    {
        var owner = Guid.NewGuid();
        var seeded = await SeedAsync(owner, "Recyclable");

        var del = await _repo.DeleteAsync(
            seeded.Id, owner, seeded.Version, NewAudit(seeded, AuditEventType.Delete), CancellationToken.None);
        del.IsSuccess.Should().BeTrue();

        var recreate = await SeedAttemptAsync(owner, "Recyclable");
        recreate.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_returns_Conflict_on_stale_version()
    {
        var owner = Guid.NewGuid();
        var seeded = await SeedAsync(owner, "Stable");

        var stale = seeded.Version + 99;
        var result = await _repo.DeleteAsync(
            seeded.Id, owner, stale, NewAudit(seeded, AuditEventType.Delete), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ledger.conflict");
    }

    private async Task<DomainLedger> SeedAsync(Guid ownerId, string name)
    {
        var attempt = await SeedAttemptAsync(ownerId, name);
        attempt.IsSuccess.Should().BeTrue();
        return attempt.Value!;
    }

    private async Task<Result<DomainLedger>> SeedAttemptAsync(Guid ownerId, string name)
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

        return await _repo.CreateAsync(ledger, NewAudit(ledger, AuditEventType.Create), CancellationToken.None);
    }

    private static AuditEntry NewAudit(DomainLedger ledger, AuditEventType type) =>
        AuditEntry.Builder()
            .WithActorId(ledger.OwnerId)
            .WithLedgerId(ledger.Id)
            .WithEventType(type)
            .WithEventAt(DateTimeOffset.UtcNow)
            .WithPayload(JsonSerializer.Serialize(new { ledger.Id, ledger.Name, ledger.Version }))
            .Build().Value!;
}
