namespace SddDemo.Ledger.Domain.Common;

/// <summary>
/// Non-generic Result for void-shaped operations.
/// Constitution Principle VI > Result pattern — Domain/Application/Infrastructure
/// MUST return Result for any operation that can fail; only the API boundary translates
/// to RpcException via ResultToRpcExceptionMapper.
/// </summary>
public readonly record struct Result
{
    public Error? Error { get; }

    public bool IsSuccess => Error is null;
    public bool IsFailure => Error is not null;

    private Result(Error? error) => Error = error;

    public static Result Success() => new(null);
    public static Result Failure(Error error) => new(error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>
/// Generic Result carrying a success value of <typeparamref name="T"/> or an Error.
/// </summary>
public readonly record struct Result<T>
{
    public T? Value { get; }
    public Error? Error { get; }

    public bool IsSuccess => Error is null;
    public bool IsFailure => Error is not null;

    private Result(T? value, Error? error)
    {
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Failure(Error error) => new(default, error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);

    public Result<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        IsFailure ? Result<TOut>.Failure(Error!) : Result<TOut>.Success(mapper(Value!));

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> binder) =>
        IsFailure ? Result<TOut>.Failure(Error!) : binder(Value!);
}
