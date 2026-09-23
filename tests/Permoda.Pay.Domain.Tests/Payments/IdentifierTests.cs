using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Domain.Tests.Payments;

public sealed class IdentifierTests
{
    [Fact]
    public void TransactionNumber_Create_TrimsValidValue()
    {
        var result = TransactionNumber.Create(" 1756113296 ");

        Assert.True(result.IsSuccess);
        Assert.Equal("1756113296", result.Value.Value);
    }

    [Fact]
    public void TransactionNumber_Create_RejectsValuesLongerThanTwentyCharacters()
    {
        var result = TransactionNumber.Create(new string('1', TransactionNumber.MaxLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("transaction_number.too_long", result.Error.Code);
    }

    [Fact]
    public void StoreId_Create_RejectsMissingValue()
    {
        var result = StoreId.Create(" ");

        Assert.True(result.IsFailure);
        Assert.Equal("store_id.required", result.Error.Code);
    }

    [Fact]
    public void ReferenceNumber_Create_ReturnsNormalizedValue()
    {
        var result = ReferenceNumber.Create(" 00136544716V ");

        Assert.True(result.IsSuccess);
        Assert.Equal("00136544716V", result.Value.Value);
    }

    [Fact]
    public void TerminalId_Create_RejectsValuesLongerThanFifteenCharacters()
    {
        var result = TerminalId.Create(new string('T', TerminalId.MaxLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("terminal_id.too_long", result.Error.Code);
    }

    [Fact]
    public void CashierId_Create_RejectsValuesLongerThanTwentyCharacters()
    {
        var result = CashierId.Create(new string('C', CashierId.MaxLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("cashier_id.too_long", result.Error.Code);
    }
}
