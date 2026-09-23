using Permoda.Pay.Domain.Common;

namespace Permoda.Pay.Domain.GiftCards;

public sealed record CardIdentifier
{
    public const int PhysicalCardMaxLength = 60;
    public const int DigitalGencodeMaxLength = 20;

    private CardIdentifier(string value, CardIdentifierKind kind)
    {
        Value = value;
        Kind = kind;
    }

    public string Value { get; }

    public CardIdentifierKind Kind { get; }

    public string MaskedValue => Mask();

    public static Result<CardIdentifier> CreatePhysicalCard(string? value) =>
        Create(value, CardIdentifierKind.PhysicalCard, PhysicalCardMaxLength);

    public static Result<CardIdentifier> CreateDigitalGencode(string? value) =>
        Create(value, CardIdentifierKind.DigitalGencode, DigitalGencodeMaxLength);

    public override string ToString() => MaskedValue;

    private static Result<CardIdentifier> Create(string? value, CardIdentifierKind kind, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<CardIdentifier>.Failure(DomainError.Validation(
                "card_identifier.required",
                "The card identifier is required."));
        }

        var normalizedValue = value.Trim();

        if (normalizedValue.Length > maxLength)
        {
            return Result<CardIdentifier>.Failure(DomainError.Validation(
                "card_identifier.too_long",
                $"The card identifier cannot exceed {maxLength} characters."));
        }

        return Result<CardIdentifier>.Success(new CardIdentifier(normalizedValue, kind));
    }

    private string Mask()
    {
        const int suffixLength = 4;
        const int physicalPrefixLength = 6;

        if (Value.Length <= suffixLength)
        {
            return new string('*', Value.Length);
        }

        var prefixLength = Kind == CardIdentifierKind.PhysicalCard &&
                           Value.Length > physicalPrefixLength + suffixLength
            ? physicalPrefixLength
            : 0;
        var prefix = prefixLength == 0 ? string.Empty : Value[..prefixLength];
        var hiddenLength = Value.Length - prefixLength - suffixLength;

        return $"{prefix}{new string('*', hiddenLength)}{Value[^suffixLength..]}";
    }
}
