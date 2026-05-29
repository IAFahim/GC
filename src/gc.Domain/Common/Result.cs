using System;

namespace gc.Domain.Common;

public sealed record Result
{
    private Result(string? error)
    {
        Error = error;
    }

    public string? Error { get; }
    public bool IsSuccess => Error == null;

    public static Result Success()
    {
        return new Result((string?)null);
    }

    public static Result Failure(string error)
    {
        if (string.IsNullOrEmpty(error))
            throw new ArgumentException("Failure result must specify an error message.", nameof(error));
        return new Result(error);
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
    private readonly T? _value;

    private Result(T? value, string? error)
    {
        _value = value;
        Error = error;
    }

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Cannot access Value of a failed Result.");
    public string? Error { get; }
    public bool IsSuccess => Error == null;

    public static Result<T> Success(T value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value), "Success result must have a non-null value.");
        return new Result<T>(value, null);
    }

    public static Result<T> Failure(string error)
    {
        if (string.IsNullOrEmpty(error))
            throw new ArgumentException("Failure result must specify an error message.", nameof(error));
        return new Result<T>(default, error);
    }

    /// <summary>
    ///     Maps the success value to a new Result.
    /// </summary>
    public Result<U> Map<U>(Func<T, U> mapper)
    {
        return IsSuccess ? Result<U>.Success(mapper(Value)) : Result<U>.Failure(Error!);
    }

    /// <summary>
    ///     Binds the success value to a new Result (flatMap).
    /// </summary>
    public Result<U> Bind<U>(Func<T, Result<U>> binder)
    {
        return IsSuccess ? binder(Value) : Result<U>.Failure(Error!);
    }

    /// <summary>
    ///     Executes an action if successful, passing the value.
    /// </summary>
    public Result<T> Tap(Action<T> onSuccess)
    {
        if (IsSuccess) onSuccess(Value);
        return this;
    }

    /// <summary>
    ///     Matches on success/failure.
    /// </summary>
    public U Match<U>(Func<T, U> onSuccess, Func<string, U> onFailure)
    {
        return IsSuccess ? onSuccess(Value) : onFailure(Error!);
    }
}