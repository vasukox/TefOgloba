namespace Permoda.Pay.Application.Abstractions;

public sealed record PortFailure(string Code, string Description, PortFailureType Type)
{
    public bool IsUncertain => Type is PortFailureType.Timeout or PortFailureType.Indeterminate;
}
