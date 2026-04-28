// T065 (US3) — end-to-end UpdateLedger against WebApplicationFactory<Program>
// in Production env + Testcontainers. Acceptance Scenarios 3.1–3.6.

using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
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

public class UpdateLedgerEndpointTests : IAsyncLifetime
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
    public async Task Acceptance_3_1_active_description_change_persists_and_bumps_version()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var created = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Ops", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));

        var updated = await client.UpdateLedgerAsync(
            new UpdateLedgerRequest
            {
                Id = created.Id,
                VersionToken = created.VersionToken,
                Description = "primary",
                UpdateMask = new FieldMask { Paths = { "description" } },
            },
            headers: WithOwner(OwnerA));

        updated.Description.Should().Be("primary");
        updated.VersionToken.Should().NotEqual(created.VersionToken);
    }

    [Fact]
    public async Task Acceptance_3_3_cross_owner_update_returns_NotFound()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var created = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "MineOnly", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));

        var act = async () => await client.UpdateLedgerAsync(
            new UpdateLedgerRequest
            {
                Id = created.Id,
                VersionToken = created.VersionToken,
                Description = "hijack",
                UpdateMask = new FieldMask { Paths = { "description" } },
            },
            headers: WithOwner(OwnerB));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task Acceptance_3_4_archived_rename_returns_InvalidArgument_archived_read_only()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var created = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Arc", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));

        var archived = await client.UpdateLedgerAsync(
            new UpdateLedgerRequest
            {
                Id = created.Id,
                VersionToken = created.VersionToken,
                Status = LedgerStatus.Archived,
                UpdateMask = new FieldMask { Paths = { "status" } },
            },
            headers: WithOwner(OwnerA));

        var act = async () => await client.UpdateLedgerAsync(
            new UpdateLedgerRequest
            {
                Id = archived.Id,
                VersionToken = archived.VersionToken,
                Name = "RenamedWhileArchived",
                UpdateMask = new FieldMask { Paths = { "name" } },
            },
            headers: WithOwner(OwnerA));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        ex.Which.Trailers.GetValue("ledger-error-code").Should().Be("ledger.archived.read_only");
    }

    [Fact]
    public async Task Acceptance_3_5_un_archive_restores_visibility_in_default_list()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var created = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Una", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));

        var archived = await client.UpdateLedgerAsync(
            new UpdateLedgerRequest
            {
                Id = created.Id,
                VersionToken = created.VersionToken,
                Status = LedgerStatus.Archived,
                UpdateMask = new FieldMask { Paths = { "status" } },
            },
            headers: WithOwner(OwnerA));

        var unarchived = await client.UpdateLedgerAsync(
            new UpdateLedgerRequest
            {
                Id = archived.Id,
                VersionToken = archived.VersionToken,
                Status = LedgerStatus.Active,
                UpdateMask = new FieldMask { Paths = { "status" } },
            },
            headers: WithOwner(OwnerA));

        unarchived.Status.Should().Be(LedgerStatus.Active);

        var list = await client.ListLedgersAsync(
            new ListLedgersRequest(), headers: WithOwner(OwnerA));
        list.Ledgers.Should().Contain(l => l.Id == created.Id);
    }

    [Fact]
    public async Task Acceptance_3_6_stale_version_token_returns_AlreadyExists()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var created = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Conc", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));

        // First writer wins.
        var winner = await client.UpdateLedgerAsync(
            new UpdateLedgerRequest
            {
                Id = created.Id,
                VersionToken = created.VersionToken,
                Description = "first",
                UpdateMask = new FieldMask { Paths = { "description" } },
            },
            headers: WithOwner(OwnerA));

        // Second writer carries the now-stale token.
        var act = async () => await client.UpdateLedgerAsync(
            new UpdateLedgerRequest
            {
                Id = created.Id,
                VersionToken = created.VersionToken,
                Description = "second",
                UpdateMask = new FieldMask { Paths = { "description" } },
            },
            headers: WithOwner(OwnerA));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.AlreadyExists);
        ex.Which.Trailers.GetValue("ledger-error-code").Should().Be("ledger.conflict");

        // Winner's update is intact.
        winner.Description.Should().Be("first");
    }
}
