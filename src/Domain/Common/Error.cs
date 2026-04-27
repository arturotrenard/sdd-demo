namespace SddDemo.Ledger.Domain.Common;

public sealed record Error(string Code, string Message, ErrorType Type);
