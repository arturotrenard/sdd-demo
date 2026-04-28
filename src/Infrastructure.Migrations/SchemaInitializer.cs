using DbUp;
using DbUp.Engine;

namespace SddDemo.Ledger.Infrastructure.Migrations;

/// <summary>
/// Applies the embedded <c>Scripts/NNNN_*.sql</c> against a Postgres
/// connection string via DbUp. Used by the AppHost-orchestrated migrator
/// process at dev/deploy time and by integration tests to bring a
/// Testcontainers Postgres up to schema before exercising the gRPC stack.
/// </summary>
public static class SchemaInitializer
{
    public static DatabaseUpgradeResult Apply(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        EnsureDatabase.For.PostgresqlDatabase(connectionString);

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(SchemaInitializer).Assembly)
            .LogToConsole()
            .Build();

        return upgrader.PerformUpgrade();
    }
}
