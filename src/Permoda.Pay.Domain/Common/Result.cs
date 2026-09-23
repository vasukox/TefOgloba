namespace Permoda.Pay.Domain.Common;

public sealed class Result
{
    private Result(bool isSuccess, DomainError error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError Error { get; }

    public static Result Success() => new(true, DomainError.None);

    public static Result Failure(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error == DomainError.None)
        {
            throw new ArgumentException("A failure result requires an error.", nameof(error));
        }

        return new Result(false, error);
    }
}

public sealed class Result<T>
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, DomainError error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result does not contain a value.");

    public DomainError Error { get; }

    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(true, value, DomainError.None);
    }

    public static Result<T> Failure(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error == DomainError.None)
        {
            throw new ArgumentException("A failure result requires an error.", nameof(error));
        }

        return new Result<T>(false, default, error);
    }
}
