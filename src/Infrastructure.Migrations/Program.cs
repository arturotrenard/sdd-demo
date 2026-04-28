// DbUp migrations runner — Constitution v2.11.0 (Tech Stack > Persistence:
// schema migrations are raw idempotent SQL scripts run by DbUp).
//
// Aspire init-container pattern: a Generic Host hosts a BackgroundService
// that performs the migration in ExecuteAsync (which fires AFTER the host
// transitions to "Running" — without this, Aspire's DCP never sees the
// resource reach Running state and dependent resources stay queued).
// On completion the worker calls IHostApplicationLifetime.StopApplication
// so the host exits cleanly with the right exit code, and the AppHost's
// `api.WaitForCompletion(migrator)` is satisfied.

using DbUp.Engine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SddDemo.Ledger.Infrastructure.Migrations;

const string ConnectionName = "ledger";

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<MigrationRunner>();
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionName)
    ?? throw new InvalidOperationException(
        $"ConnectionStrings:{ConnectionName} is not configured. The Aspire AppHost MUST inject it via WithReference(postgres)."));

await builder.Build().RunAsync().ConfigureAwait(false);

namespace SddDemo.Ledger.Infrastructure.Migrations
{
    internal sealed class MigrationRunner(string connectionString, IHostApplicationLifetime lifetime) : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                DatabaseUpgradeResult result = SchemaInitializer.Apply(connectionString);

                if (!result.Successful)
                {
                    Console.Error.WriteLine($"[migrator] FAILED at script '{result.ErrorScript?.Name}': {result.Error}");
                    Environment.ExitCode = 1;
                }
                else
                {
                    Console.WriteLine($"[migrator] OK — {result.Scripts.Count()} script(s) applied.");
                }
            }
            finally
            {
                lifetime.StopApplication();
            }

            return Task.CompletedTask;
        }
    }
}
