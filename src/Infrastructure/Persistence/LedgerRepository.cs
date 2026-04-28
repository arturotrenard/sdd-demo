using Dapper;
using Npgsql;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Ledgers;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Infrastructure.Persistence;

/// <summary>
/// data-model.md §5 — Dapper implementation of <see cref="ILedgerRepository"/> over
/// the pooled <see cref="NpgsqlDataSource"/>. Writes the ledger row and the matching
/// audit row inside a single transaction so atomic same-tx invariants hold (SC-005).
/// PostgreSQL <c>23505</c> on <c>ux_ledger_owner_name_lower</c> is mapped at the
/// Infrastructure boundary to <see cref="LedgerErrors.NameAlreadyExists"/>
/// (Constitution Principle VI > library-thrown exceptions caught at the boundary).
/// </summary>
public sealed class LedgerRepository(
    NpgsqlDataSource dataSource,
    IAuditRepository auditRepository) : ILedgerRepository
{
    private const string UniqueViolationSqlState = "23505";
    private const string OwnerNameLowerIndex = "ux_ledger_owner_name_lower";

    private static readonly string InsertSql = EmbeddedSql.Load("Ledger/Insert.sql");

    public async Task<Result<DomainLedger>> CreateAsync(
        DomainLedger ledger,
        AuditEntry audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(audit);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var command = new CommandDefinition(
                InsertSql,
                new
                {
                    ledger.Id,
                    ledger.OwnerId,
                    ledger.Name,
                    ledger.Description,
                    ledger.CurrencyCode,
                    Status = (short)ledger.Status,
                    ledger.Version,
                    ledger.CreatedAt,
                    ledger.LastModifiedAt,
                },
                transaction: transaction,
                cancellationToken: cancellationToken);

            await connection.ExecuteScalarAsync<long>(command).ConfigureAwait(false);

            var auditResult = await auditRepository
                .WriteAsync(audit, connection, transaction, cancellationToken)
                .ConfigureAwait(false);

            if (auditResult.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Result<DomainLedger>.Failure(auditResult.Error!);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result<DomainLedger>.Success(ledger);
        }
        catch (PostgresException ex) when (
            ex.SqlState == UniqueViolationSqlState
            && ex.ConstraintName == OwnerNameLowerIndex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return Result<DomainLedger>.Failure(LedgerErrors.NameAlreadyExists);
        }
        catch (PostgresException ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return Result<DomainLedger>.Failure(new Error(
                "ledger.persistence_failed",
                $"Database error during create: {ex.MessageText}",
                ErrorType.Failure));
        }
    }
}
