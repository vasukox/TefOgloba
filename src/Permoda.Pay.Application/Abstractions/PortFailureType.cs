namespace Permoda.Pay.Application.Abstractions;

public enum PortFailureType
{
    Rejected = 1,
    Timeout = 2,
    Authentication = 3,
    Technical = 4,
    Indeterminate = 5
}
