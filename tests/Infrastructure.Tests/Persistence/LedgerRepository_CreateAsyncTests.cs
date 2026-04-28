// T040 (TDD red) — written before T049 (LedgerRepository.CreateAsync).
// Testcontainers Postgres + DbUp migrations applied at fixture init; verifies
// the happy path, FR-003 case-insensitive duplicate-name → NameAlreadyExists,
// SC-005 audit timing < 5 s, and same-tx atomicity (failed insert → no audit row).

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

public class LedgerRepository_CreateAsyncTests : IAsyncLifetime
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

        var connectionString = _postgres.GetConnectionString();

        // Apply migrations via DbUp (Constitution v2.11.0).
        var result = SchemaInitializer.Apply(connectionString);
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"DbUp failed at script '{result.ErrorScript?.Name}'", result.Error);
        }

        _dataSource = NpgsqlDataSource.Create(connectionString);

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

    private Result<DomainLedger> NewLedger(Guid ownerId, string name, string currency = "USD")
    {
        var now = DateTimeOffset.UtcNow;
        return DomainLedger.Builder()
            .WithId(Guid.CreateVersion7())
            .WithOwnerId(ownerId)
            .WithName(name)
            .WithDescription(null)
            .WithCurrencyCode(currency)
            .WithStatus(LedgerStatus.Active)
            .WithVersion(1)
            .WithTimestamps(now, now)
            .Build(_services);
    }

    private static AuditEntry NewAudit(DomainLedger ledger)
    {
        var payload = JsonSerializer.Serialize(new
        {
            ledger.Id,
            ledger.OwnerId,
            ledger.Name,
            ledger.CurrencyCode,
            Status = (int)ledger.Status,
            ledger.Version,
        });

        return AuditEntry.Builder()
            .WithActorId(ledger.OwnerId)
            .WithLedgerId(ledger.Id)
            .WithEventType(AuditEventType.Create)
            .WithEventAt(DateTimeOffset.UtcNow)
            .WithPayload(payload)
            .Build()
            .Value!;
    }

    [Fact]
    public async Task CreateAsync_inserts_ledger_with_version_one_and_one_audit_row_within_5s()
    {
        var ownerId = Guid.NewGuid();
        var ledger = NewLedger(ownerId, "Operating Account").Value!;
        var audit = NewAudit(ledger);

        var operationStart = DateTimeOffset.UtcNow;
        var result = await _repo.CreateAsync(ledger, audit, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Version.Should().Be(1);

        await using var conn = await _dataSource.OpenConnectionAsync();
        var auditRows = (await conn.QueryAsync<(short event_type, DateTimeOffset event_at)>(
            "SELECT event_type, event_at FROM ledger_audit WHERE ledger_id = @Id",
            new { ledger.Id })).ToList();

        auditRows.Should().HaveCount(1);
        auditRows[0].event_type.Should().Be((short)AuditEventType.Create);
        (auditRows[0].event_at - operationStart).Should().BeLessThan(
            TimeSpan.FromSeconds(5),
            "SC-005 — audit row visible within 5 s of the operation");
    }

    [Fact]
    public async Task CreateAsync_returns_NameAlreadyExists_for_duplicate_name_case_insensitive()
    {
        var ownerId = Guid.NewGuid();

        var first = NewLedger(ownerId, "Operating Account").Value!;
        var firstResult = await _repo.CreateAsync(first, NewAudit(first), CancellationToken.None);
        firstResult.IsSuccess.Should().BeTrue();

        var second = NewLedger(ownerId, "operating ACCOUNT").Value!;
        var secondResult = await _repo.CreateAsync(second, NewAudit(second), CancellationToken.None);

        secondResult.IsFailure.Should().BeTrue();
        secondResult.Error!.Code.Should().Be("ledger.name_already_exists");
        secondResult.Error.Type.Should().Be(ErrorType.Conflict);

        // Atomic same-tx — the failed second insert MUST NOT leave an audit row.
        await using var conn = await _dataSource.OpenConnectionAsync();
        var auditCount = await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM ledger_audit WHERE ledger_id = @Id",
            new { second.Id });
        auditCount.Should().Be(0, "atomic same-tx invariant — no audit row when ledger insert fails");
    }

    [Fact]
    public async Task CreateAsync_isolates_owners_and_allows_same_name_per_owner()
    {
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();

        var ledgerA = NewLedger(ownerA, "Operating Account").Value!;
        var ledgerB = NewLedger(ownerB, "Operating Account").Value!;

        (await _repo.CreateAsync(ledgerA, NewAudit(ledgerA), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await _repo.CreateAsync(ledgerB, NewAudit(ledgerB), CancellationToken.None)).IsSuccess.Should().BeTrue();
    }
}
