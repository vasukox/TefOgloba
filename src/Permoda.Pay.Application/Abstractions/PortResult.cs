namespace Permoda.Pay.Application.Abstractions;

public sealed class PortResult<T>
{
    private readonly T? _value;
    private readonly PortFailure? _failure;

    private PortResult(bool isSuccess, T? value, PortFailure? failure)
    {
        IsSuccess = isSuccess;
        _value = value;
        _failure = failure;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed port result does not contain a value.");

    public PortFailure Failure => IsFailure
        ? _failure!
        : throw new InvalidOperationException("A successful port result does not contain a failure.");

    public static PortResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new PortResult<T>(true, value, null);
    }

    public static PortResult<T> Failed(PortFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new PortResult<T>(false, default, failure);
    }
}
