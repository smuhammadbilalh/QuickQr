namespace QuickQr.Core;

/// <summary>A machine-readable error code plus a user-presentable message.</summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error Validation(string message) => new(ErrorCodes.Validation, message);
    public static Error NotFound(string message) => new(ErrorCodes.NotFound, message);
    public static Error FeatureLocked(string message) => new(ErrorCodes.FeatureLocked, message);
    public static Error Unexpected(string message) => new(ErrorCodes.Unexpected, message);

    public override string ToString() => $"{Code}: {Message}";
}

public static class ErrorCodes
{
    public const string Validation = "validation";
    public const string NotFound = "not_found";
    public const string FeatureLocked = "feature_locked";
    public const string PayloadTooLarge = "payload_too_large";
    public const string UnsupportedFormat = "unsupported_format";
    public const string DecodeFailed = "decode_failed";
    public const string Cancelled = "cancelled";
    public const string Network = "network";
    public const string Unexpected = "unexpected";
}

/// <summary>Outcome of an operation that has no value.</summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new ArgumentException("A successful result cannot carry an error.", nameof(error));
        if (!isSuccess && error == Error.None)
            throw new ArgumentException("A failed result must carry an error.", nameof(error));

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);
}

/// <summary>Outcome of an operation that produces a value on success.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value) : base(true, Error.None) => _value = value;
    private Result(Error error) : base(false, error) { }

    /// <summary>The value. Throws when the result is a failure.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error}).");

    public static Result<T> Success(T value) => new(value);
    public static new Result<T> Failure(Error error) => new(error);

    public Result<TOut> Map<TOut>(Func<T, TOut> map) =>
        IsSuccess ? Result<TOut>.Success(map(Value)) : Result<TOut>.Failure(Error);

    public async Task<Result<TOut>> BindAsync<TOut>(Func<T, Task<Result<TOut>>> bind) =>
        IsSuccess ? await bind(Value).ConfigureAwait(false) : Result<TOut>.Failure(Error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
