using Permoda.Pay.Domain.GiftCards;

namespace Permoda.Pay.Domain.Tests.GiftCards;

public sealed class CardIdentifierTests
{
    [Fact]
    public void PhysicalCard_MasksBinAndLastFourAccordingToHiPosContract()
    {
        var result = CardIdentifier.CreatePhysicalCard("1138170025515937");

        Assert.True(result.IsSuccess);
        Assert.Equal("113817******5937", result.Value.MaskedValue);
        Assert.Equal(result.Value.MaskedValue, result.Value.ToString());
    }

    [Fact]
    public void DigitalGencode_MasksAllButLastFourCharacters()
    {
        var result = CardIdentifier.CreateDigitalGencode("3214567008158");

        Assert.True(result.IsSuccess);
        Assert.Equal("*********8158", result.Value.MaskedValue);
        Assert.DoesNotContain(result.Value.Value, result.Value.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void IdentifierWithFourCharacters_MasksEntireValue()
    {
        var result = CardIdentifier.CreatePhysicalCard("1234");

        Assert.True(result.IsSuccess);
        Assert.Equal("****", result.Value.MaskedValue);
    }

    [Fact]
    public void DigitalGencode_RejectsValuesLongerThanTwentyCharacters()
    {
        var result = CardIdentifier.CreateDigitalGencode(
            new string('1', CardIdentifier.DigitalGencodeMaxLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("card_identifier.too_long", result.Error.Code);
    }
}
