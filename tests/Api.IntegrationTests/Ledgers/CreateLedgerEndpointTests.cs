// T041 (TDD red) — written before T051 (LedgersService.CreateLedger).
// WebApplicationFactory<Program> in Production env (so non-anonymous header rejection
// + HSTS / UseHttpsRedirection paths run) + Testcontainers Postgres + Redis.
//
// Acceptance Scenarios 1.1 / 1.2 / 1.3 + Edge: unsupported currency + FR-009/FR-010
// missing X-Owner-Id rejected as malformed input (Validation → InvalidArgument).
// Authn/authz are deferred per Constitution Principle V — there is no auth concept here.

using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using FluentAssertions;
using SddDemo.Ledger.Infrastructure.Migrations;
using SddDemo.Ledger.V1;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;
using LedgersGrpc = SddDemo.Ledger.V1.Ledgers;

namespace SddDemo.Ledger.Api.IntegrationTests.Ledgers;

public class CreateLedgerEndpointTests : IAsyncLifetime
{
    private const string OwnerHeader = "X-Owner-Id";
    private static readonly Guid OwnerA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

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
            // Production env disables the AnonymousCurrentUser DevOwnerId fallback so
            // a missing X-Owner-Id header is rejected as malformed input (FR-010 — Validation).
            // Hardening:EnforceHttpsRedirection=false keeps UseHttpsRedirection out of the
            // pipeline because the in-memory test server has no HTTPS endpoint and gRPC
            // would hang waiting for a redirect target.
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:ledger", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:redis", _redis.GetConnectionString());
            builder.UseSetting("Hardening:EnforceHttpsRedirection", "false");
        });

    private static GrpcChannel GrpcChannelOver(HttpClient client) =>
        GrpcChannel.ForAddress(client.BaseAddress!, new GrpcChannelOptions { HttpClient = client });

    private static Metadata WithOwner(Guid owner) =>
        new() { { OwnerHeader, owner.ToString() } };

    [Fact]
    public async Task Acceptance_1_1_creates_ledger_for_authenticated_owner()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var view = await client.CreateLedgerAsync(
            new CreateLedgerRequest
            {
                Name = "Operating Account",
                Description = "Primary operating ledger",
                CurrencyCode = "USD",
            },
            headers: WithOwner(OwnerA));

        view.Id.Should().NotBeNullOrWhiteSpace();
        view.Name.Should().Be("Operating Account");
        view.CurrencyCode.Should().Be("USD");
        view.Status.Should().Be(LedgerStatus.Active);
        view.VersionToken.Should().NotBeNull();
        view.VersionToken.Length.Should().Be(8);
    }

    [Fact]
    public async Task Acceptance_1_2_duplicate_name_within_owner_returns_AlreadyExists()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var owner = Guid.NewGuid();

        await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "Dup", CurrencyCode = "USD" },
            headers: WithOwner(owner));

        var act = async () => await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "DUP", CurrencyCode = "USD" },
            headers: WithOwner(owner));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.AlreadyExists);
        ex.Which.Trailers.GetValue("ledger-error-code").Should().Be("ledger.name_already_exists");
    }

    [Fact]
    public async Task Acceptance_1_3_missing_required_field_returns_InvalidArgument()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var act = async () => await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = string.Empty, CurrencyCode = "USD" },
            headers: WithOwner(Guid.NewGuid()));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task Edge_unsupported_currency_returns_InvalidArgument()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var act = async () => await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "X", CurrencyCode = "XYZ" },
            headers: WithOwner(Guid.NewGuid()));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task FR_010_missing_owner_header_in_production_is_rejected_with_InvalidArgument()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();
        using var channel = GrpcChannelOver(http);
        var client = new LedgersGrpc.LedgersClient(channel);

        var act = async () => await client.CreateLedgerAsync(
            new CreateLedgerRequest { Name = "X", CurrencyCode = "USD" });
        // No owner header — handler MUST short-circuit with a Validation failure.

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        ex.Which.Trailers.GetValue("ledger-error-code").Should().Be("identity.missing_owner");
    }
}
