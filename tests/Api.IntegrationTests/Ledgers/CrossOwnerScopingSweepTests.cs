// T079 — aggregate cross-owner scoping sweep proving SC-004 ("100 % of cross-owner
// attempts rejected") at the suite level. Per-RPC tests in T041/T055/T065/T072 already
// cover individual surfaces; this single sweep proves the property as one signal across
// the full Create/Get/List/Update/Delete surface.
//
// Production env so the AnonymousCurrentUser DevOwnerId fallback is disabled —
// owner identity comes only from the X-Owner-Id header (FR-009 / FR-010).

using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SddDemo.Ledger.Infrastructure.Migrations;
using SddDemo.Ledger.V1;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;
using LedgersGrpc = SddDemo.Ledger.V1.Ledgers;

namespace SddDemo.Ledger.Api.IntegrationTests.Ledgers;

public class CrossOwnerScopingSweepTests : IAsyncLifetime
{
    private const string OwnerHeader = "X-Owner-Id";
    private static readonly Guid OwnerA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OwnerB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("ledger").WithUsername("ledger").WithPassword("ledger").Build();

    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        var apply = SchemaInitializer.Apply(_postgres.GetConnectionString());
        if (!apply.Successful)
        {
            throw new InvalidOperationException(
                $"DbUp failed at script '{apply.ErrorScript?.Name}'", apply.Error);
        }
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("ConnectionStrings:ledger", _postgres.GetConnectionString());
            b.UseSetting("ConnectionStrings:redis", _redis.GetConnectionString());
            b.UseSetting("Hardening:EnforceHttpsRedirection", "false");
        });

    private static GrpcChannel GrpcChannelOver(HttpClient c) =>
        GrpcChannel.ForAddress(c.BaseAddress!, new GrpcChannelOptions { HttpClient = c });

    private static Metadata WithOwner(Guid o) => new() { { OwnerHeader, o.ToString() } };

    /// <summary>
    /// Owner A creates a ledger; Owner B then attempts every read/write RPC against
    /// it and MUST receive NotFound (or NotFound-equivalent for Get/Delete/Update —
    /// no information leak; FR-009). List MUST NOT include the foreign row.
    /// </summary>
    [Fact]
    public async Task SC_004_owner_b_cannot_observe_or_mutate_owner_a_ledger()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        // Owner A seeds a ledger.
        var aLedger = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "A's Cash", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));

        aLedger.Id.Should().NotBeNullOrWhiteSpace();

        // GetLedger as Owner B → NotFound (no leak between owners).
        var getAct = async () => await client.GetLedgerAsync(
            new GetLedgerRequest { Id = aLedger.Id },
            headers: WithOwner(OwnerB));
        (await getAct.Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.NotFound);

        // ListLedgers as Owner B → page MUST NOT include A's ledger id.
        var listAsB = await client.ListLedgersAsync(
            new ListLedgersRequest { PageSize = 200, IncludeArchived = true },
            headers: WithOwner(OwnerB));
        listAsB.Ledgers.Should().NotContain(v => v.Id == aLedger.Id);

        // UpdateLedger as Owner B against A's id → NotFound.
        var updAct = async () => await client.UpdateLedgerAsync(
            new UpdateLedgerRequest
            {
                Id = aLedger.Id,
                VersionToken = aLedger.VersionToken,
                Description = "owned by B",
            },
            headers: WithOwner(OwnerB));
        (await updAct.Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.NotFound);

        // DeleteLedger as Owner B against A's id → NotFound.
        var delAct = async () => await client.DeleteLedgerAsync(
            new DeleteLedgerRequest { Id = aLedger.Id, VersionToken = aLedger.VersionToken },
            headers: WithOwner(OwnerB));
        (await delAct.Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.NotFound);

        // Owner A's ledger MUST still be visible to A — proves nothing was mutated/deleted.
        var stillThere = await client.GetLedgerAsync(
            new GetLedgerRequest { Id = aLedger.Id },
            headers: WithOwner(OwnerA));
        stillThere.Id.Should().Be(aLedger.Id);
    }

    /// <summary>
    /// Same-name ledgers across owners MUST be allowed (uniqueness is owner-scoped per
    /// FR-009). This complements the cross-owner reject sweep above by proving that
    /// owner-scoping only restricts visibility — not legal naming for a different owner.
    /// </summary>
    [Fact]
    public async Task SC_004_same_name_allowed_across_owners()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var fromA = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Shared Name", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));

        var fromB = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Shared Name", CurrencyCode = "USD" },
            headers: WithOwner(OwnerB));

        fromA.Id.Should().NotBe(fromB.Id);
    }
}
