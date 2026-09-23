using Permoda.Pay.Domain.Common;

namespace Permoda.Pay.Domain.Payments;

public sealed record Money
{
    public const string DefaultCurrency = "COP";
    public const long MaxAbsoluteMinorUnits = 99_999_999_999_999;

    private Money(long minorUnits, string currency)
    {
        MinorUnits = minorUnits;
        Currency = currency;
    }

    public long MinorUnits { get; }

    public string Currency { get; }

    public static Result<Money> Create(long minorUnits, string currency = DefaultCurrency)
    {
        if (minorUnits is > MaxAbsoluteMinorUnits or < -MaxAbsoluteMinorUnits)
        {
            return Result<Money>.Failure(DomainError.Validation(
                "money.out_of_range",
                "The amount exceeds the supported 14-digit range."));
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            return Result<Money>.Failure(DomainError.Validation(
                "money.currency_required",
                "The currency is required."));
        }

        var normalizedCurrency = currency.Trim().ToUpperInvariant();

        if (normalizedCurrency.Length != 3 || !normalizedCurrency.All(char.IsAsciiLetter))
        {
            return Result<Money>.Failure(DomainError.Validation(
                "money.currency_invalid",
                "The currency must be a three-letter ISO 4217 code."));
        }

        return Result<Money>.Success(new Money(minorUnits, normalizedCurrency));
    }
}
