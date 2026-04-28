using SddDemo.Ledger.Domain.Common;

namespace SddDemo.Ledger.Domain.Ledgers;

/// <summary>
/// data-model.md §3.2 — canonical Error instances raised across the ledger lifecycle.
/// Translated to gRPC status codes by <c>ResultToRpcExceptionMapper</c> at the API boundary.
/// </summary>
public static class LedgerErrors
{
    public static readonly Error NameAlreadyExists = new(
        "ledger.name_already_exists",
        "A ledger with that name already exists for this owner.",
        ErrorType.Conflict);

    public static readonly Error NotFound = new(
        "ledger.not_found",
        "Ledger not found.",
        ErrorType.NotFound);

    public static readonly Error Conflict = new(
        "ledger.conflict",
        "The ledger has been modified since you last read it. Re-fetch and retry.",
        ErrorType.Conflict);

    public static readonly Error ArchivedReadOnly = new(
        "ledger.archived.read_only",
        "Archived ledgers may only be un-archived; other attribute changes are not permitted.",
        ErrorType.Validation);

    public static readonly Error ArchivedCannotDelete = new(
        "ledger.archived.cannot_delete",
        "Archived ledgers cannot be deleted; un-archive first.",
        ErrorType.Validation);
}
