using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SddDemo.Ledger.Application.Abstractions.Observability;
using ZiggyCreatures.Caching.Fusion.OpenTelemetry;

namespace SddDemo.Ledger.Api.Observability;

/// <summary>
/// research.md §10 + Constitution Principle IV — single <c>UseOtlpExporter()</c>;
/// resource attrs <c>service.name=ledger-service</c>, <c>service.version=&lt;gitsha&gt;</c>;
/// AspNetCore + gRPC client + Http instrumentation; FusionCache OTel registered
/// alongside (Phase 2 task T031). Explicit histogram buckets per research.md §10.
/// /health/* + /metrics filtered out of tracing.
/// </summary>
public static class OpenTelemetryRegistration
{
    public const string ServiceName = "ledger-service";
    public const string ActivitySourceGlob = "SddDemo.Ledger.*";

    private static readonly double[] OperationDurationBuckets =
    [
        0.010, 0.025, 0.050, 0.100, 0.250, 0.500, 1.0, 2.5, 5.0, 10.0,
    ];

    public static IServiceCollection AddLedgerOpenTelemetry(this IServiceCollection services)
    {
        services.AddSingleton<LedgerMetrics>();
        services.AddSingleton<ILedgerMetrics>(sp => sp.GetRequiredService<LedgerMetrics>());

        services.AddOpenTelemetry()
            .ConfigureResource(rb => rb
                .AddService(serviceName: ServiceName, serviceVersion: ResolveVersion()))
            .WithTracing(tracing => tracing
                .AddSource(ActivitySourceGlob)
                .AddAspNetCoreInstrumentation(opts =>
                {
                    opts.Filter = ctx =>
                    {
                        var path = ctx.Request.Path.Value ?? string.Empty;
                        return !path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase)
                               && !path.Equals("/metrics", StringComparison.OrdinalIgnoreCase);
                    };
                })
                .AddGrpcClientInstrumentation()
                .AddHttpClientInstrumentation()
                .AddFusionCacheInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(LedgerMetrics.MeterName)
                .AddMeter("Microsoft.AspNetCore.Hosting")
                .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                .AddMeter("System.Net.Http")
                .AddFusionCacheInstrumentation()
                .AddView(
                    instrumentName: "ledger.operation.duration",
                    new ExplicitBucketHistogramConfiguration { Boundaries = OperationDurationBuckets })
                .AddOtlpExporter())
            .WithLogging(logging => logging.AddOtlpExporter());

        return services;
    }

    private static string ResolveVersion() =>
        Environment.GetEnvironmentVariable("SERVICE_VERSION")
            ?? typeof(OpenTelemetryRegistration).Assembly.GetName().Version?.ToString()
            ?? "unknown";
}
