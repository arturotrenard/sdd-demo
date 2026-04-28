using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Dapper;
using Npgsql;
using SddDemo.Ledger.Application.Abstractions.Persistence;
using SddDemo.Ledger.Application.Features.Ledgers.Queries.ListLedgers;
using SddDemo.Ledger.Domain.Auditing;
using SddDemo.Ledger.Domain.Common;
using SddDemo.Ledger.Domain.Ledgers;
using DomainLedger = SddDemo.Ledger.Domain.Ledgers.Ledger;

namespace SddDemo.Ledger.Infrastructure.Persistence;

/// <summary>
/// data-model.md §5 — Dapper implementation of <see cref="ILedgerRepository"/> over
/// the pooled <see cref="NpgsqlDataSource"/>. State-changing methods write the
/// matching audit row inside a single transaction so atomic same-tx invariants
/// hold (SC-005). PostgreSQL <c>23505</c> on <c>ux_ledger_owner_name_lower</c> is
/// mapped to <see cref="LedgerErrors.NameAlreadyExists"/> at the boundary.
/// </summary>
public sealed class LedgerRepository(
    NpgsqlDataSource dataSource,
    IAuditRepository auditRepository) : ILedgerRepository
{
    private const string UniqueViolationSqlState = "23505";
    private const string OwnerNameLowerIndex = "ux_ledger_owner_name_lower";

    private static readonly string InsertSql = EmbeddedSql.Load("Ledger/Insert.sql");
    private static readonly string GetByIdSql = EmbeddedSql.Load("Ledger/GetById.sql");
    private static readonly string ListKeysetSql = EmbeddedSql.Load("Ledger/ListKeyset.sql");
    private static readonly string UpdateSql = EmbeddedSql.Load("Ledger/UpdateOptimistic.sql");
    private static readonly string DeleteSql = EmbeddedSql.Load("Ledger/DeleteOptimistic.sql");

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

    public async Task<Result<DomainLedger>> GetByIdAsync(
        Guid ledgerId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            var command = new CommandDefinition(
                GetByIdSql,
                new { Id = ledgerId, OwnerId = ownerId },
                cancellationToken: cancellationToken);

            var row = await connection.QuerySingleOrDefaultAsync<LedgerRow?>(command)
                .ConfigureAwait(false);

            if (row is null)
            {
                return Result<DomainLedger>.Failure(LedgerErrors.NotFound);
            }

            return Result<DomainLedger>.Success(BuildLedger(row));
        }
        catch (PostgresException ex)
        {
            return Result<DomainLedger>.Failure(new Error(
                "ledger.persistence_failed",
                $"Database error during get: {ex.MessageText}",
                ErrorType.Failure));
        }
    }

    public async Task<Result<LedgerListPage>> ListAsync(
        Guid ownerId,
        bool includeArchived,
        string? pageCursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var cursorResult = Cursor.Decode(pageCursor);
        if (cursorResult.IsFailure)
        {
            return Result<LedgerListPage>.Failure(cursorResult.Error!);
        }

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            var (cursorTimestamp, cursorId) = cursorResult.Value;

            var command = new CommandDefinition(
                ListKeysetSql,
                new
                {
                    OwnerId = ownerId,
                    IncludeArchived = includeArchived,
                    CursorTimestamp = cursorTimestamp,
                    CursorId = cursorId,
                    Limit = pageSize + 1,
                },
                cancellationToken: cancellationToken);

            var rows = (await connection.QueryAsync<LedgerRow>(command).ConfigureAwait(false))
                .ToList();

            string? nextCursor = null;
            if (rows.Count > pageSize)
            {
                var lastReturned = rows[pageSize - 1];
                nextCursor = Cursor.Encode(lastReturned.LastModifiedAt, lastReturned.Id);
                rows = rows.Take(pageSize).ToList();
            }

            var ledgers = rows.Select(BuildLedger).ToList();
            return Result<LedgerListPage>.Success(new LedgerListPage(ledgers, nextCursor));
        }
        catch (PostgresException ex)
        {
            return Result<LedgerListPage>.Failure(new Error(
                "ledger.persistence_failed",
                $"Database error during list: {ex.MessageText}",
                ErrorType.Failure));
        }
    }

    public async Task<Result<DomainLedger>> UpdateAsync(
        DomainLedger updated,
        long expectedVersion,
        AuditEntry audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updated);
        ArgumentNullException.ThrowIfNull(audit);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var command = new CommandDefinition(
                UpdateSql,
                new
                {
                    updated.Id,
                    updated.OwnerId,
                    updated.Name,
                    updated.Description,
                    Status = (short)updated.Status,
                    updated.LastModifiedAt,
                    ExpectedVersion = expectedVersion,
                },
                transaction: transaction,
                cancellationToken: cancellationToken);

            var newVersion = await connection
                .ExecuteScalarAsync<long?>(command)
                .ConfigureAwait(false);

            if (newVersion is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Result<DomainLedger>.Failure(LedgerErrors.Conflict);
            }

            var auditResult = await auditRepository
                .WriteAsync(audit, connection, transaction, cancellationToken)
                .ConfigureAwait(false);

            if (auditResult.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Result<DomainLedger>.Failure(auditResult.Error!);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            // Re-build with the new (server-assigned) version so the caller surfaces
            // the freshly-incremented value in the response. Skip re-validation —
            // the row that just won the optimistic UPDATE is by definition valid.
            var withNewVersion = DomainLedger.Builder()
                .WithId(updated.Id)
                .WithOwnerId(updated.OwnerId)
                .WithName(updated.Name)
                .WithDescription(updated.Description)
                .WithCurrencyCode(updated.CurrencyCode)
                .WithStatus(updated.Status)
                .WithVersion(newVersion.Value)
                .WithTimestamps(updated.CreatedAt, updated.LastModifiedAt)
                .BuildFromPersistence();

            return Result<DomainLedger>.Success(withNewVersion);
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
                $"Database error during update: {ex.MessageText}",
                ErrorType.Failure));
        }
    }

    public async Task<Result> DeleteAsync(
        Guid ledgerId,
        Guid ownerId,
        long expectedVersion,
        AuditEntry audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audit);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var command = new CommandDefinition(
                DeleteSql,
                new
                {
                    Id = ledgerId,
                    OwnerId = ownerId,
                    ExpectedVersion = expectedVersion,
                },
                transaction: transaction,
                cancellationToken: cancellationToken);

            var deletedId = await connection
                .ExecuteScalarAsync<Guid?>(command)
                .ConfigureAwait(false);

            if (deletedId is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Result.Failure(LedgerErrors.Conflict);
            }

            var auditResult = await auditRepository
                .WriteAsync(audit, connection, transaction, cancellationToken)
                .ConfigureAwait(false);

            if (auditResult.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Result.Failure(auditResult.Error!);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (PostgresException ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return Result.Failure(new Error(
                "ledger.persistence_failed",
                $"Database error during delete: {ex.MessageText}",
                ErrorType.Failure));
        }
    }

    // Reconstitutes a Ledger from a persisted row without re-running Domain validation.
    // The database's check constraints (data-model.md §2) and the original Build-time
    // validation are the source of truth for these rows; re-validating here would
    // require ICurrencyCatalog wiring at the Infrastructure boundary for no benefit.
    private static DomainLedger BuildLedger(LedgerRow row) =>
        DomainLedger.Builder()
            .WithId(row.Id)
            .WithOwnerId(row.OwnerId)
            .WithName(row.Name)
            .WithDescription(row.Description)
            .WithCurrencyCode(row.CurrencyCode)
            .WithStatus((LedgerStatus)row.Status)
            .WithVersion(row.Version)
            .WithTimestamps(row.CreatedAt, row.LastModifiedAt)
            .BuildFromPersistence();

    /// <summary>
    /// Internal projection from the SQL row layout to the Domain builder.
    /// Public for Dapper's type mapper.
    /// </summary>
    public sealed class LedgerRow
    {
        public Guid Id { get; init; }
        public Guid OwnerId { get; init; }
        public string Name { get; init; } = default!;
        public string? Description { get; init; }
        public string CurrencyCode { get; init; } = default!;
        public short Status { get; init; }
        public long Version { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset LastModifiedAt { get; init; }
    }

    /// <summary>
    /// Opaque base64 cursor encoding <c>(timestamp UTC ticks, ledger id)</c>.
    /// Decoded back to nullable values so the SQL <c>OR @CursorTimestamp IS NULL</c>
    /// branch fires for the first page.
    /// </summary>
    internal static class Cursor
    {
        public static string Encode(DateTimeOffset timestamp, Guid id)
        {
            var raw = string.Create(
                CultureInfo.InvariantCulture,
                $"{timestamp.UtcTicks}|{id:D}");
            var bytes = Encoding.UTF8.GetBytes(raw);
            return Convert.ToBase64String(bytes);
        }

        public static Result<(DateTimeOffset? Timestamp, Guid? Id)> Decode(string? cursor)
        {
            if (string.IsNullOrWhiteSpace(cursor))
            {
                return Result<(DateTimeOffset?, Guid?)>.Success((null, null));
            }

            try
            {
                var bytes = Convert.FromBase64String(cursor);
                var raw = Encoding.UTF8.GetString(bytes);
                var pipe = raw.IndexOf('|', StringComparison.Ordinal);
                if (pipe <= 0 || pipe == raw.Length - 1)
                {
                    return InvalidCursor();
                }

                var ticksPart = raw.AsSpan(0, pipe);
                var idPart = raw.AsSpan(pipe + 1);

                if (!long.TryParse(ticksPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
                    || !Guid.TryParse(idPart, out var id))
                {
                    return InvalidCursor();
                }

                var ts = new DateTimeOffset(ticks, TimeSpan.Zero);
                return Result<(DateTimeOffset?, Guid?)>.Success((ts, id));
            }
            catch (FormatException)
            {
                return InvalidCursor();
            }
        }

        private static Result<(DateTimeOffset?, Guid?)> InvalidCursor() =>
            Result<(DateTimeOffset?, Guid?)>.Failure(new Error(
                "ledger.invalid_cursor",
                "page_cursor is not a valid keyset cursor.",
                ErrorType.Validation));
    }
}
