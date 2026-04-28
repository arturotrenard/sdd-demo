// T055 (US2) — end-to-end Get/List against WebApplicationFactory<Program> in
// Production env + Testcontainers. Acceptance Scenarios 2.1–2.4 + FR-009/SC-004
// cross-owner read returns NOT_FOUND identical to a missing ledger.

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

public class GetAndListLedgersEndpointTests : IAsyncLifetime
{
    private const string OwnerHeader = "X-Owner-Id";
    private static readonly Guid OwnerA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OwnerB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("ledger")
        .WithUsername("ledger")
        .WithPassword("ledger")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        var result = SchemaInitializer.Apply(_postgres.GetConnectionString());
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"DbUp failed at script '{result.ErrorScript?.Name}'", result.Error);
        }
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:ledger", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:redis", _redis.GetConnectionString());
            builder.UseSetting("Hardening:EnforceHttpsRedirection", "false");
        });

    private static GrpcChannel GrpcChannelOver(HttpClient client) =>
        GrpcChannel.ForAddress(client.BaseAddress!, new GrpcChannelOptions { HttpClient = client });

    private static Metadata WithOwner(Guid owner) => new() { { OwnerHeader, owner.ToString() } };

    [Fact]
    public async Task Acceptance_2_1_lists_owned_ledgers_with_summary_fields()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "L1", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));
        await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "L2", CurrencyCode = "EUR" },
            headers: WithOwner(OwnerA));

        var response = await client.ListLedgersAsync(
            new ListLedgersRequest { PageSize = 50 },
            headers: WithOwner(OwnerA));

        response.Ledgers.Should().HaveCount(2);
        response.Ledgers.Should().AllSatisfy(l =>
        {
            l.Id.Should().NotBeNullOrWhiteSpace();
            l.VersionToken.Length.Should().Be(8);
        });
    }

    [Fact]
    public async Task Acceptance_2_2_get_returns_full_details_for_owner()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var created = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Op", Description = "primary", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));

        var fetched = await client.GetLedgerAsync(
            new GetLedgerRequest { Id = created.Id },
            headers: WithOwner(OwnerA));

        fetched.Id.Should().Be(created.Id);
        fetched.Name.Should().Be("Op");
        fetched.Description.Should().Be("primary");
    }

    [Fact]
    public async Task Acceptance_2_3_get_unknown_returns_NotFound_no_information_leak()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var act = async () => await client.GetLedgerAsync(
            new GetLedgerRequest { Id = Guid.NewGuid().ToString() },
            headers: WithOwner(OwnerA));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task FR_009_cross_owner_get_returns_NotFound_indistinguishable_from_missing()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var created = await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "MineOnly", CurrencyCode = "USD" },
            headers: WithOwner(OwnerA));

        var act = async () => await client.GetLedgerAsync(
            new GetLedgerRequest { Id = created.Id },
            headers: WithOwner(OwnerB));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task Acceptance_2_4_paginates_with_keyset_cursor()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        for (var i = 0; i < 4; i++)
        {
            await client.CreateLedgerAsync(
                new CreateLedgerRequest { Name = $"P{i}", CurrencyCode = "USD" },
                headers: WithOwner(OwnerA));
            await Task.Delay(10);
        }

        var first = await client.ListLedgersAsync(
            new ListLedgersRequest { PageSize = 2 },
            headers: WithOwner(OwnerA));

        first.Ledgers.Should().HaveCount(2);
        first.NextPageCursor.Should().NotBeNullOrWhiteSpace();

        var second = await client.ListLedgersAsync(
            new ListLedgersRequest { PageSize = 2, PageCursor = first.NextPageCursor },
            headers: WithOwner(OwnerA));

        second.Ledgers.Should().HaveCount(2);

        var firstIds = first.Ledgers.Select(l => l.Id).ToHashSet();
        var secondIds = second.Ledgers.Select(l => l.Id).ToHashSet();
        firstIds.Overlaps(secondIds).Should().BeFalse();
    }
}
