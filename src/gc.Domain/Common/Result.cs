namespace gc.Domain.Common;

public sealed record Result<T>
{
    public T? Value { get; init; }
    public string? Error { get; init; }
    public bool IsSuccess => Error == null;

    public static Result<T> Success(T value) => new() { Value = value };
    public static Result<T> Failure(string error) => new() { Error = error };
}

public sealed record Result
{
    public string? Error { get; init; }
    public bool IsSuccess => Error == null;

    public static Result Success() => new();
    public static Result Failure(string error) => new() { Error = error };
}
