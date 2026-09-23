using Permoda.Pay.Domain.Common;

namespace Permoda.Pay.Domain.Payments;

public sealed record TerminalId
{
    public const int MaxLength = 15;

    private TerminalId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<TerminalId> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<TerminalId>.Failure(DomainError.Validation(
                "terminal_id.required",
                "The terminal identifier is required."));
        }

        var normalizedValue = value.Trim();

        if (normalizedValue.Length > MaxLength)
        {
            return Result<TerminalId>.Failure(DomainError.Validation(
                "terminal_id.too_long",
                $"The terminal identifier cannot exceed {MaxLength} characters."));
        }

        return Result<TerminalId>.Success(new TerminalId(normalizedValue));
    }

    public override string ToString() => Value;
}
