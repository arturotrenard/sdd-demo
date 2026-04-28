// T081 — measures LedgerRepository.GetByIdAsync factory P50 under realistic
// Testcontainers conditions. The measured P50 is the value FusionCache's
// FactorySoftTimeout MUST be derived from per Constitution Tech Stack > Caching
// (no unsubstantiated guesses). If the measured P50 != current 200 ms value in
// FusionCacheRegistration (T031), update the registration before merge.
//
// This benchmark deliberately bypasses CachingLedgerRepository — we time the
// direct repository hit (the FusionCache "factory") so the measurement is the
// cache-miss cost the soft timeout has to be larger than.
//
// Run: `dotnet run -c Release --project tests/Performance -- --filter *GetById*`.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Dapper;
using Npgsql;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Infrastructure.Migrations;
using SddDemo.Ledger.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SddDemo.Ledger.Performance.Benchmarks;

public sealed class GetByIdBenchmarkConfig : ManualConfig
{
    public GetByIdBenchmarkConfig()
    {
        ArtifactsPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "benchmarks", "results");

        AddJob(Job.ShortRun
            .WithStrategy(RunStrategy.Throughput)
            .WithWarmupCount(2)
            .WithIterationCount(10)
            .WithLaunchCount(1));
    }
}

[Config(typeof(GetByIdBenchmarkConfig))]
[MemoryDiagnoser]
public class LedgerRepositoryGetByIdBenchmark
{
    private PostgreSqlContainer _postgres = null!;
    private NpgsqlDataSource _dataSource = null!;
    private LedgerRepository _repo = null!;

    private Guid _ownerId = Guid.NewGuid();
    private Guid _ledgerId = Guid.NewGuid();

    [GlobalSetup]
    public async Task GlobalSetupAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("ledger").WithUsername("ledger").WithPassword("ledger")
            .Build();
        await _postgres.StartAsync();

        var migration = SchemaInitializer.Apply(_postgres.GetConnectionString());
        if (!migration.Successful)
        {
            throw new InvalidOperationException(
                $"DbUp failed at script '{migration.ErrorScript?.Name}'", migration.Error);
        }

        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());

        IAuditRepository auditRepo = new AuditRepository(_dataSource);
        _repo = new LedgerRepository(_dataSource, auditRepo);

        // Seed a single row directly via SQL — Builder().Build() validates the currency
        // catalog which is wired via DI; raw INSERT keeps this benchmark dependency-free.
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO ledger (id, owner_id, name, description, currency_code, status, version, created_at, last_modified_at)
            VALUES (@Id, @OwnerId, @Name, @Description, @CurrencyCode, @Status, @Version, @CreatedAt, @LastModifiedAt);
            """,
            new
            {
                Id = _ledgerId,
                OwnerId = _ownerId,
                Name = "Bench-GetById-Seed",
                Description = (string?)"benchmark fixture",
                CurrencyCode = "USD",
                Status = (short)1,
                Version = 1L,
                CreatedAt = DateTimeOffset.UtcNow,
                LastModifiedAt = DateTimeOffset.UtcNow,
            });
    }

    [GlobalCleanup]
    public async Task GlobalCleanupAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// FusionCache factory (cache miss) — the path FusionCache calls when populating
    /// L1/L2 from the inner repository. The reported P50 is the value
    /// FusionCacheRegistration.DefaultEntryOptions.FactorySoftTimeout MUST be tuned
    /// against (Constitution Tech Stack > Caching).
    /// </summary>
    [Benchmark]
    public Task GetByIdAsync_FactoryPath() =>
        _repo.GetByIdAsync(_ledgerId, _ownerId, CancellationToken.None);
}
