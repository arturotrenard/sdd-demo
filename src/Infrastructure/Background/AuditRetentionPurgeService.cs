using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SddDemo.Ledger.Application.Abstractions.Persistence;

namespace SddDemo.Ledger.Infrastructure.Background;

/// <summary>
/// research.md §12 — daily purge at 03:00 UTC. Runs once at startup (catch-up) and
/// then on a 24-hour cadence via <see cref="PeriodicTimer"/>. Unhandled exceptions
/// crash the host (the default .NET 6+ behaviour), per Constitution Principle VI's
/// "exceptions still allowed > BackgroundService failures" rule.
/// </summary>
public sealed class AuditRetentionPurgeService(
    IServiceProvider serviceProvider,
    ILogger<AuditRetentionPurgeService> logger) : BackgroundService
{
    public static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(365);
    public static readonly TimeSpan PurgePeriod = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial catch-up.
        await PurgeAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(PurgePeriod);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await PurgeAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var auditRepo = scope.ServiceProvider.GetRequiredService<IAuditRepository>();

        var result = await auditRepo.PurgeOlderThanAsync(RetentionWindow, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Audit purge failed: {ErrorCode} {ErrorMessage}",
                result.Error!.Code,
                result.Error.Message);
            return;
        }

        logger.LogInformation("Audit purge removed {Count} rows older than {Retention}.",
            result.Value,
            RetentionWindow);
    }
}
