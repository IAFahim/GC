namespace gc.Domain.Common;

public sealed record Result
{
    public string? Error { get; init; }
    public bool IsSuccess => Error == null;

    public static Result Success()
    {
        return new Result();
    }

    public static Result Failure(string error)
    {
        return new Result { Error = error };
    }

    /// <summary>
    ///     Maps the success value to a new Result.
    /// </summary>
    public Result Map(Func<Result> next)
    {
        return IsSuccess ? next() : this;
    }

    /// <summary>
    ///     Executes an action if successful.
    /// </summary>
    public Result Tap(Action onSuccess)
    {
        if (IsSuccess) onSuccess();
        return this;
    }

    /// <summary>
    ///     Matches on success/failure.
    /// </summary>
    public T Match<T>(Func<T> onSuccess, Func<string, T> onFailure)
    {
        return IsSuccess ? onSuccess() : onFailure(Error!);
    }
}

public sealed record Result<T>
{
    public T? Value { get; init; }
    public string? Error { get; init; }
    public bool IsSuccess => Error == null;

    public static Result<T> Success(T value)
    {
        return new Result<T> { Value = value };
    }

    public static Result<T> Failure(string error)
    {
        return new Result<T> { Error = error };
    }

    /// <summary>
    ///     Maps the success value to a new Result.
    /// </summary>
    public Result<U> Map<U>(Func<T, U> mapper)
    {
        return IsSuccess ? Result<U>.Success(mapper(Value!)) : Result<U>.Failure(Error!);
    }

    /// <summary>
    ///     Binds the success value to a new Result (flatMap).
    /// </summary>
    public Result<U> Bind<U>(Func<T, Result<U>> binder)
    {
        return IsSuccess ? binder(Value!) : Result<U>.Failure(Error!);
    }

    /// <summary>
    ///     Executes an action if successful, passing the value.
    /// </summary>
    public Result<T> Tap(Action<T> onSuccess)
    {
        if (IsSuccess) onSuccess(Value!);
        return this;
    }

    /// <summary>
    ///     Matches on success/failure.
    /// </summary>
    public U Match<U>(Func<T, U> onSuccess, Func<string, U> onFailure)
    {
        return IsSuccess ? onSuccess(Value!) : onFailure(Error!);
    }
}