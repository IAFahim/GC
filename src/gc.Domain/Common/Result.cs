namespace gc.Domain.Common;

public sealed record Result
{
    public string? Error { get; init; }
    public bool IsSuccess => Error == null;

    public static Result Success() => new();
    public static Result Failure(string error) => new() { Error = error };

    /// <summary>
    /// Maps the success value to a new Result.
    /// </summary>
    public Result Map(Func<Result> next) => IsSuccess ? next() : this;

    /// <summary>
    /// Executes an action if successful.
    /// </summary>
    public Result Tap(Action onSuccess) { if (IsSuccess) onSuccess(); return this; }

    /// <summary>
    /// Matches on success/failure.
    /// </summary>
    public T Match<T>(Func<T> onSuccess, Func<string, T> onFailure) =>
        IsSuccess ? onSuccess() : onFailure(Error!);
}

public sealed record Result<T>
{
    public T? Value { get; init; }
    public string? Error { get; init; }
    public bool IsSuccess => Error == null;

    public static Result<T> Success(T value) => new() { Value = value };
    public static Result<T> Failure(string error) => new() { Error = error };

    /// <summary>
    /// Maps the success value to a new Result.
    /// </summary>
    public Result<U> Map<U>(Func<T, U> mapper) =>
        IsSuccess ? Result<U>.Success(mapper(Value!)) : Result<U>.Failure(Error!);

    /// <summary>
    /// Binds the success value to a new Result (flatMap).
    /// </summary>
    public Result<U> Bind<U>(Func<T, Result<U>> binder) =>
        IsSuccess ? binder(Value!) : Result<U>.Failure(Error!);

    /// <summary>
    /// Executes an action if successful, passing the value.
    /// </summary>
    public Result<T> Tap(Action<T> onSuccess) { if (IsSuccess) onSuccess(Value!); return this; }

    /// <summary>
    /// Matches on success/failure.
    /// </summary>
    public U Match<U>(Func<T, U> onSuccess, Func<string, U> onFailure) =>
        IsSuccess ? onSuccess(Value!) : onFailure(Error!);
}
