using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace SddDemo.Ledger.Api.Caching;

/// <summary>
/// research.md §4 + Constitution Tech Stack > Caching — L1 + L2 (Redis) + Backplane
/// is mandatory; defaulting to "just L1" is forbidden. <c>FactorySoftTimeout</c> is a
/// placeholder pending T081's BenchmarkDotNet substantiation (merge-blocking per the
/// constitution's "never a guess" rule).
/// </summary>
public static class FusionCacheRegistration
{
    public const string CacheName = "ledger";

    public static IServiceCollection AddLedgerFusionCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redis = configuration.GetConnectionString("redis");

        var builder = services.AddFusionCache(CacheName)
            .WithDefaultEntryOptions(new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(10),
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromHours(2),
                FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
                // Placeholder — replaced before merge by T081's measured factory P50.
                FactorySoftTimeout = TimeSpan.FromMilliseconds(200),
                FactoryHardTimeout = TimeSpan.FromSeconds(2),
            })
            .WithSerializer(new FusionCacheSystemTextJsonSerializer());

        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(o => o.Configuration = redis);

            builder.WithDistributedCache(sp =>
                sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>())
                .WithBackplane(new RedisBackplane(new RedisBackplaneOptions { Configuration = redis }));
        }

        // FusionCache OTel instrumentation is wired alongside the OTel pipeline
        // in OpenTelemetryRegistration (TracerProviderBuilder / MeterProviderBuilder
        // extensions) — research.md §10.

        return services;
    }
}
