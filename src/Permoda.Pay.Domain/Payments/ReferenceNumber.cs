using Permoda.Pay.Domain.Common;

namespace Permoda.Pay.Domain.Payments;

public sealed record ReferenceNumber
{
    // docs/OGLOBA_API_REFERENCE.md §3 changelog (v2.12): elevado de 20 a 40 caracteres.
    public const int MaxLength = 40;

    private ReferenceNumber(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<ReferenceNumber> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<ReferenceNumber>.Failure(DomainError.Validation(
                "reference_number.required",
                "The reference number is required."));
        }

        var normalizedValue = value.Trim();

        if (normalizedValue.Length > MaxLength)
        {
            return Result<ReferenceNumber>.Failure(DomainError.Validation(
                "reference_number.too_long",
                $"The reference number cannot exceed {MaxLength} characters."));
        }

        return Result<ReferenceNumber>.Success(new ReferenceNumber(normalizedValue));
    }

    public override string ToString() => Value;
}
