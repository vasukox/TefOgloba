using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Domain.Tests.Payments;

public sealed class MoneyTests
{
    [Theory]
    [InlineData(50_000, "COP", "COP")]
    [InlineData(-50_000, "cop", "COP")]
    [InlineData(1, " usd ", "USD")]
    public void Create_WithValidAmountAndCurrency_ReturnsMoney(
        long minorUnits,
        string currency,
        string expectedCurrency)
    {
        var result = Money.Create(minorUnits, currency);

        Assert.True(result.IsSuccess);
        Assert.Equal(minorUnits, result.Value.MinorUnits);
        Assert.Equal(expectedCurrency, result.Value.Currency);
    }

    [Fact]
    public void Create_WithZeroAmount_ReturnsValidMoneyForBalanceRepresentation()
    {
        var result = Money.Create(0);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.MinorUnits);
    }

    [Theory]
    [InlineData(Money.MaxAbsoluteMinorUnits + 1)]
    [InlineData(-Money.MaxAbsoluteMinorUnits - 1)]
    public void Create_WithAmountOutsideFourteenDigits_ReturnsValidationFailure(long minorUnits)
    {
        var result = Money.Create(minorUnits);

        Assert.True(result.IsFailure);
        Assert.Equal("money.out_of_range", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CO")]
    [InlineData("CO1")]
    [InlineData("PESO")]
    public void Create_WithInvalidCurrency_ReturnsValidationFailure(string currency)
    {
        var result = Money.Create(50_000, currency);

        Assert.True(result.IsFailure);
    }
}
