using HealthChecks.NpgSql;
using HealthChecks.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace SddDemo.Ledger.Api.Health;

/// <summary>
/// research.md §10 — /health/live always 200; /health/ready validates Postgres + Redis.
/// Names are tagged so the readiness probe filters out the liveness check.
/// </summary>
public static class HealthChecksRegistration
{
    public const string LiveTag = "live";
    public const string ReadyTag = "ready";

    public static IServiceCollection AddLedgerHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var pg = configuration.GetConnectionString("ledger");
        var redis = configuration.GetConnectionString("redis");

        var builder = services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [LiveTag]);

        if (!string.IsNullOrWhiteSpace(pg))
        {
            builder.AddNpgSql(pg, name: "postgres", tags: [ReadyTag]);
        }

        if (!string.IsNullOrWhiteSpace(redis))
        {
            builder.AddRedis(redis, name: "redis", tags: [ReadyTag]);
        }

        return services;
    }

    public static WebApplication MapLedgerHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(LiveTag),
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag) || check.Tags.Contains(LiveTag),
        });

        return app;
    }
}
