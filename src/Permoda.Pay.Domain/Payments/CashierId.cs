using Permoda.Pay.Domain.Common;

namespace Permoda.Pay.Domain.Payments;

public sealed record CashierId
{
    public const int MaxLength = 20;

    private CashierId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<CashierId> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<CashierId>.Failure(DomainError.Validation(
                "cashier_id.required",
                "The cashier identifier is required."));
        }

        var normalizedValue = value.Trim();

        if (normalizedValue.Length > MaxLength)
        {
            return Result<CashierId>.Failure(DomainError.Validation(
                "cashier_id.too_long",
                $"The cashier identifier cannot exceed {MaxLength} characters."));
        }

        return Result<CashierId>.Success(new CashierId(normalizedValue));
    }

    public override string ToString() => Value;
}
