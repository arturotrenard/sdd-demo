using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SddDemo.Ledger.Infrastructure.Persistence;

/// <summary>
/// research.md §3 — pooled <see cref="NpgsqlDataSource"/> registered as a singleton via
/// <c>services.AddNpgsqlDataSource(...)</c>. The connection string is read from
/// <c>ConnectionStrings:ledger</c> (the AppHost-injected name per research.md §14).
/// </summary>
public static class DataSourceFactory
{
    public const string ConnectionStringName = "ledger";

    public static IServiceCollection AddLedgerDataSource(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddNpgsqlDataSource(connectionString, builder =>
        {
            // Defensive defaults — Npgsql's pool is well-tuned out of the box;
            // explicit keepalive helps with on-prem stateful connections.
            builder.ConnectionStringBuilder.KeepAlive = 30;
        });

        return services;
    }
}
