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

        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(o => o.Configuration = redis);
        }

        // Default (unnamed) registration so consumers can inject IFusionCache directly.
        // TryWithAutoSetup picks up the L2 IDistributedCache + a backplane registered
        // alongside (FusionCache 2.x — see DependencyInjection.md in the package).
        var builder = services.AddFusionCache()
            .WithDefaultEntryOptions(new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(10),
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromHours(2),
                FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
                // Placeholder — replaced before merge by T081's measured factory P50.
                FactorySoftTimeout = TimeSpan.FromMilliseconds(200),
                FactoryHardTimeout = TimeSpan.FromSeconds(2),
                // Distributed cache + backplane operations must fail fast so a slow Redis
                // never blocks the calling write path (cache invalidation is best-effort).
                DistributedCacheSoftTimeout = TimeSpan.FromMilliseconds(200),
                DistributedCacheHardTimeout = TimeSpan.FromSeconds(1),
                AllowBackgroundDistributedCacheOperations = true,
                AllowBackgroundBackplaneOperations = true,
            })
            .WithSerializer(new FusionCacheSystemTextJsonSerializer())
            .TryWithAutoSetup();

        if (!string.IsNullOrWhiteSpace(redis))
        {
            builder.WithBackplane(new RedisBackplane(new RedisBackplaneOptions { Configuration = redis }));
        }

        // FusionCache OTel instrumentation is wired alongside the OTel pipeline
        // in OpenTelemetryRegistration (TracerProviderBuilder / MeterProviderBuilder
        // extensions) — research.md §10.

        return services;
    }
}
