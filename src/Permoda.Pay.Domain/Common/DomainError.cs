namespace Permoda.Pay.Domain.Common;

public sealed record DomainError
{
    public static readonly DomainError None = new(string.Empty, string.Empty, DomainErrorType.None);

    private DomainError(string code, string description, DomainErrorType type)
    {
        Code = code;
        Description = description;
        Type = type;
    }

    public string Code { get; }

    public string Description { get; }

    public DomainErrorType Type { get; }

    public static DomainError Validation(string code, string description) =>
        new(code, description, DomainErrorType.Validation);

    public static DomainError Conflict(string code, string description) =>
        new(code, description, DomainErrorType.Conflict);
}
