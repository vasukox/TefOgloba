namespace Permoda.Pay.Application.Errors;

public sealed record ApplicationError
{
    public static readonly ApplicationError None = new(
        string.Empty,
        string.Empty,
        ApplicationErrorType.None);

    private ApplicationError(string code, string description, ApplicationErrorType type)
    {
        Code = code;
        Description = description;
        Type = type;
    }

    public string Code { get; }

    public string Description { get; }

    public ApplicationErrorType Type { get; }

    public static ApplicationError Validation(string code, string description) =>
        new(code, description, ApplicationErrorType.Validation);

    public static ApplicationError Declined(string code, string description) =>
        new(code, description, ApplicationErrorType.Declined);

    public static ApplicationError Unknown(string code, string description) =>
        new(code, description, ApplicationErrorType.Unknown);

    public static ApplicationError Technical(string code, string description) =>
        new(code, description, ApplicationErrorType.Technical);
}
