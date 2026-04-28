using Dapper;
using Npgsql;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;

namespace SddDemo.Ledger.Infrastructure.Persistence;

/// <summary>
/// data-model.md §5 + research.md §6 — same-transaction writer for the audit log,
/// plus the daily retention purge driver. <see cref="WriteAsync"/> takes the live
/// <c>NpgsqlConnection</c>+<c>NpgsqlTransaction</c> from the calling repository so
/// the audit row commits or rolls back atomically with the state change (SC-005).
/// </summary>
public sealed class AuditRepository(NpgsqlDataSource dataSource) : IAuditRepository
{
    private static readonly string InsertSql = EmbeddedSql.Load("Audit/Insert.sql");
    private static readonly string PurgeSql = EmbeddedSql.Load("Audit/PurgeOlderThan.sql");

    public async Task<Result> WriteAsync(
        AuditEntry entry,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        try
        {
            var command = new CommandDefinition(
                InsertSql,
                new
                {
                    entry.ActorId,
                    entry.LedgerId,
                    EventType = (short)entry.EventType,
                    entry.EventAt,
                    entry.Payload,
                },
                transaction: transaction,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(command).ConfigureAwait(false);
            return Result.Success();
        }
        catch (PostgresException ex)
        {
            return Result.Failure(new Error(
                "audit.write_failed",
                $"Failed to write audit entry: {ex.MessageText}",
                ErrorType.Failure));
        }
    }

    public async Task<Result<int>> PurgeOlderThanAsync(
        TimeSpan retention,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            var cutoff = DateTimeOffset.UtcNow - retention;

            var command = new CommandDefinition(
                PurgeSql,
                new { Cutoff = cutoff },
                cancellationToken: cancellationToken);

            var rows = await connection.ExecuteAsync(command).ConfigureAwait(false);
            return Result<int>.Success(rows);
        }
        catch (PostgresException ex)
        {
            return Result<int>.Failure(new Error(
                "audit.purge_failed",
                $"Failed to purge audit rows: {ex.MessageText}",
                ErrorType.Failure));
        }
    }
}
