// T080 — perf gate per Constitution Workflow > Performance budgets and
// `dotnet-diag:microbenchmarking`. Boots WebApplicationFactory<Program> once via
// [GlobalSetup] against Testcontainers Postgres + Redis, opens a GrpcChannel, and
// benchmarks every CRUD path the spec calls out.
//
// SC-002: p99 < 1 s for single-ledger CRUD (Create/Get/Update/Delete).
// SC-003: p95 < 1 s for ListLedgers over a 1 000-row owner.
//
// Budget enforcement is in Program.cs (PerfRunner.Main): after the BenchmarkSwitcher
// run completes, every benchmark's percentile statistics are inspected and the
// process exits non-zero if any budget is violated.
//
// Run: `dotnet run -c Release --project tests/Performance`.
// Artifacts land in `benchmarks/results/` (BenchmarkConfig.ArtifactsPath).

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SddDemo.Ledger.Infrastructure.Migrations;
using SddDemo.Ledger.V1;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using LedgersGrpc = SddDemo.Ledger.V1.Ledgers;

namespace SddDemo.Ledger.Performance.Benchmarks;

public sealed class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        ArtifactsPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "benchmarks", "results");

        AddJob(Job.ShortRun
            .WithStrategy(RunStrategy.Throughput)
            .WithWarmupCount(2)
            .WithIterationCount(5)
            .WithLaunchCount(1));
    }
}

[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class LedgerCrudBenchmarks
{
    private const string OwnerHeader = "X-Owner-Id";
    private static readonly Guid OwnerHot = Guid.Parse("aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa");

    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _http = null!;
    private GrpcChannel _channel = null!;
    private LedgersGrpc.LedgersClient _client = null!;

    private string _seedLedgerId = null!;
    private ByteString _seedVersionToken = null!;
    private int _createSeq;
    private string _updateLedgerId = null!;
    private ByteString _updateVersionToken = null!;
    private string _deleteLedgerId = null!;
    private ByteString _deleteVersionToken = null!;

    [GlobalSetup]
    public async Task GlobalSetupAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("ledger").WithUsername("ledger").WithPassword("ledger")
            .Build();
        _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();

        await _postgres.StartAsync();
        await _redis.StartAsync();

        var migration = SchemaInitializer.Apply(_postgres.GetConnectionString());
        if (!migration.Successful)
        {
            throw new InvalidOperationException(
                $"DbUp failed at script '{migration.ErrorScript?.Name}'", migration.Error);
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("ConnectionStrings:ledger", _postgres.GetConnectionString());
            b.UseSetting("ConnectionStrings:redis", _redis.GetConnectionString());
            b.UseSetting("Hardening:EnforceHttpsRedirection", "false");
        });
        _http = _factory.CreateClient();
        _channel = GrpcChannel.ForAddress(_http.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = _http,
            DisposeHttpClient = false,
        });
        _client = new LedgersGrpc.LedgersClient(_channel);

        // Cache-hit seed (single ledger touched repeatedly).
        var seed = await _client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Seed-Hit", CurrencyCode = "USD" },
            headers: WithOwner(OwnerHot));
        _seedLedgerId = seed.Id;
        _seedVersionToken = seed.VersionToken;

        // 999 more for the SC-003 ListLedgers_1000 benchmark (1 000 total under OwnerHot).
        for (var i = 0; i < 999; i++)
        {
            await _client.CreateLedgerAsync(
                new CreateLedgerRequest
                {
                    Name = $"List-{i:D4}",
                    CurrencyCode = "USD",
                },
                headers: WithOwner(OwnerHot));
        }
    }

    [GlobalCleanup]
    public async Task GlobalCleanupAsync()
    {
        await _channel.ShutdownAsync();
        _http.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private static Metadata WithOwner(Guid o) => new() { { OwnerHeader, o.ToString() } };

    /// <summary>SC-002 — Create p99 &lt; 1 s. Each iteration uses a unique name.</summary>
    [Benchmark]
    public async Task<LedgerView> CreateLedger()
    {
        var n = Interlocked.Increment(ref _createSeq);
        return await _client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = $"Bench-Create-{n}", CurrencyCode = "USD" },
            headers: WithOwner(OwnerHot));
    }

    /// <summary>SC-002 — Get cache-hit path (FusionCache L1).</summary>
    [Benchmark]
    public Task<LedgerView> GetLedger_CacheHit() =>
        _client.GetLedgerAsync(
            new GetLedgerRequest { Id = _seedLedgerId },
            headers: WithOwner(OwnerHot)).ResponseAsync;

    /// <summary>
    /// SC-002 — Get cache-miss path. Each iteration evicts via a no-op tag invalidation
    /// (UpdateLedger forces a re-load) so the next Get traverses the inner repo.
    /// </summary>
    [IterationSetup(Target = nameof(GetLedger_CacheMiss))]
    public void EvictForGet()
    {
        // A no-op rename to itself bumps the version + invalidates the ledger:{id} tag.
        var current = _client.GetLedger(
            new GetLedgerRequest { Id = _seedLedgerId },
            headers: WithOwner(OwnerHot));
        var updated = _client.UpdateLedger(
            new UpdateLedgerRequest
            {
                Id = current.Id,
                VersionToken = current.VersionToken,
                Description = $"miss-{Random.Shared.Next()}",
            },
            headers: WithOwner(OwnerHot));
        _seedVersionToken = updated.VersionToken;
    }

    [Benchmark]
    public Task<LedgerView> GetLedger_CacheMiss() =>
        _client.GetLedgerAsync(
            new GetLedgerRequest { Id = _seedLedgerId },
            headers: WithOwner(OwnerHot)).ResponseAsync;

    /// <summary>SC-003 — list a single page of 50 against a 1 000-row owner.</summary>
    [Benchmark]
    public Task<ListLedgersResponse> ListLedgers_1000() =>
        _client.ListLedgersAsync(
            new ListLedgersRequest { PageSize = 50, IncludeArchived = false },
            headers: WithOwner(OwnerHot)).ResponseAsync;

    /// <summary>SC-002 — Update path. Per-iteration setup creates a fresh ledger.</summary>
    [IterationSetup(Target = nameof(UpdateLedger))]
    public void SetupUpdate()
    {
        var n = Interlocked.Increment(ref _createSeq);
        var fresh = _client.CreateLedger(
            new CreateLedgerRequest { Name = $"Bench-U-{n}", CurrencyCode = "USD" },
            headers: WithOwner(OwnerHot));
        _updateLedgerId = fresh.Id;
        _updateVersionToken = fresh.VersionToken;
    }

    [Benchmark]
    public Task<LedgerView> UpdateLedger() =>
        _client.UpdateLedgerAsync(
            new UpdateLedgerRequest
            {
                Id = _updateLedgerId,
                VersionToken = _updateVersionToken,
                Description = "bench-update",
            },
            headers: WithOwner(OwnerHot)).ResponseAsync;

    /// <summary>SC-002 — Delete path. Per-iteration setup creates a fresh ledger.</summary>
    [IterationSetup(Target = nameof(DeleteLedger))]
    public void SetupDelete()
    {
        var n = Interlocked.Increment(ref _createSeq);
        var fresh = _client.CreateLedger(
            new CreateLedgerRequest { Name = $"Bench-D-{n}", CurrencyCode = "USD" },
            headers: WithOwner(OwnerHot));
        _deleteLedgerId = fresh.Id;
        _deleteVersionToken = fresh.VersionToken;
    }

    [Benchmark]
    public Task<DeleteLedgerResponse> DeleteLedger() =>
        _client.DeleteLedgerAsync(
            new DeleteLedgerRequest
            {
                Id = _deleteLedgerId,
                VersionToken = _deleteVersionToken,
            },
            headers: WithOwner(OwnerHot)).ResponseAsync;
}
