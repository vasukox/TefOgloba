using Permoda.Pay.Domain.Common;

namespace Permoda.Pay.Domain.Payments;

public sealed record TransactionNumber
{
    public const int MaxLength = 20;

    private TransactionNumber(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<TransactionNumber> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<TransactionNumber>.Failure(DomainError.Validation(
                "transaction_number.required",
                "The transaction number is required."));
        }

        var normalizedValue = value.Trim();

        if (normalizedValue.Length > MaxLength)
        {
            return Result<TransactionNumber>.Failure(DomainError.Validation(
                "transaction_number.too_long",
                $"The transaction number cannot exceed {MaxLength} characters."));
        }

        return Result<TransactionNumber>.Success(new TransactionNumber(normalizedValue));
    }

    public override string ToString() => Value;
}
