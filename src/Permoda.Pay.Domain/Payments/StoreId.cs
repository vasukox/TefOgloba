using Permoda.Pay.Domain.Common;

namespace Permoda.Pay.Domain.Payments;

public sealed record StoreId
{
    public const int MaxLength = 20;

    private StoreId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<StoreId> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<StoreId>.Failure(DomainError.Validation(
                "store_id.required",
                "The store identifier is required."));
        }

        var normalizedValue = value.Trim();

        if (normalizedValue.Length > MaxLength)
        {
            return Result<StoreId>.Failure(DomainError.Validation(
                "store_id.too_long",
                $"The store identifier cannot exceed {MaxLength} characters."));
        }

        return Result<StoreId>.Success(new StoreId(normalizedValue));
    }

    public override string ToString() => Value;
}
