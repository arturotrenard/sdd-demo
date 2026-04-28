// T072 (US4) — end-to-end DeleteLedger against WebApplicationFactory<Program>
// in Production env + Testcontainers. Acceptance Scenarios 4.1–4.4.

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

public class DeleteLedgerEndpointTests : IAsyncLifetime
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

    [Fact]
    public async Task Acceptance_4_1_delete_then_get_returns_NotFound()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var created = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Disposable", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));

        await client.DeleteLedgerAsync(
            new DeleteLedgerRequest { Id = created.Id, VersionToken = created.VersionToken },
            headers: WithOwner(OwnerA));

        var act = async () => await client.GetLedgerAsync(
            new GetLedgerRequest { Id = created.Id }, headers: WithOwner(OwnerA));
        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task Acceptance_4_2_cross_owner_delete_returns_NotFound_and_leaves_ledger_intact()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var created = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Mine", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));

        var act = async () => await client.DeleteLedgerAsync(
            new DeleteLedgerRequest { Id = created.Id, VersionToken = created.VersionToken },
            headers: WithOwner(OwnerB));
        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.NotFound);

        var stillThere = await client.GetLedgerAsync(
            new GetLedgerRequest { Id = created.Id }, headers: WithOwner(OwnerA));
        stillThere.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task Acceptance_4_3_delete_already_deleted_returns_NotFound()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var created = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Twice", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));

        await client.DeleteLedgerAsync(
            new DeleteLedgerRequest { Id = created.Id, VersionToken = created.VersionToken },
            headers: WithOwner(OwnerA));

        var act = async () => await client.DeleteLedgerAsync(
            new DeleteLedgerRequest { Id = created.Id, VersionToken = created.VersionToken },
            headers: WithOwner(OwnerA));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task Acceptance_4_4_delete_archived_returns_InvalidArgument_archived_cannot_delete()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var created = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Frozen", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));

        var archived = await client.UpdateLedgerAsync(
            new UpdateLedgerRequest
            {
                Id = created.Id,
                VersionToken = created.VersionToken,
                Status = LedgerStatus.Archived,
                UpdateMask = new Google.Protobuf.WellKnownTypes.FieldMask { Paths = { "status" } },
            },
            headers: WithOwner(OwnerA));

        var act = async () => await client.DeleteLedgerAsync(
            new DeleteLedgerRequest { Id = archived.Id, VersionToken = archived.VersionToken },
            headers: WithOwner(OwnerA));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        ex.Which.Trailers.GetValue("ledger-error-code").Should().Be("ledger.archived.cannot_delete");
    }

    [Fact]
    public async Task FR_008_delete_then_recreate_same_name_succeeds()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var created = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Recyclable", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));

        await client.DeleteLedgerAsync(
            new DeleteLedgerRequest { Id = created.Id, VersionToken = created.VersionToken },
            headers: WithOwner(OwnerA));

        var recreated = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Recyclable", CurrencyCode = "EUR" },
            headers: WithOwner(OwnerA));

        recreated.Id.Should().NotBe(created.Id);
    }
}
