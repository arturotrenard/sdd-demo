// T027 (TDD red) — written before T028/T030/T035 (OTel + health + composition root land).
// MUST fail to build / fail at runtime until the Api host exposes /health/live + /health/ready
// and the composition root wires Postgres/Redis from Aspire-injected connection strings.

using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace SddDemo.Ledger.Api.IntegrationTests.Health;

public class HealthEndpointsTests : IAsyncLifetime
{
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
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Per Constitution Principle V > Always-on, /health endpoints are the only HTTP
            // surface that must respond regardless of authentication; integration tests pin
            // Production environment so HSTS / UseHttpsRedirection paths are exercised.
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:ledger", _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:redis", _redis.GetConnectionString());
        });

    [Fact]
    public async Task Live_returns_200()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_returns_200_when_all_dependencies_are_up()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
