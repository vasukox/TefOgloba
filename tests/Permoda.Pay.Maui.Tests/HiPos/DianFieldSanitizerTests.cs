using Permoda.Pay.Maui.HiPos.Results;

namespace Permoda.Pay.Maui.Tests.HiPos;

/// <summary>
/// Reglas del manual de ICG para el único campo del TEF que llega al módulo fiscal y de ahí a
/// la DIAN. Si no se cumplen, el fiscal rechaza el documento — por eso están cubiertas.
/// </summary>
public sealed class DianFieldSanitizerTests
{
    [Fact]
    public void AuthorizationId_StripsHyphens()
    {
        // Un GUID crudo es rechazo directo: el campo no admite separadores.
        var result = DianFieldSanitizer.AuthorizationId("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

        Assert.Equal("3f2504e04f8911d39a0c0305e82c3301", result);
        Assert.DoesNotContain('-', result);
    }

    [Fact]
    public void AuthorizationId_StripsSpaces()
    {
        Assert.Equal("00136544716V", DianFieldSanitizer.AuthorizationId("  0013 6544 716V  "));
    }

    [Fact]
    public void AuthorizationId_TruncatesToFortyCharacters()
    {
        var result = DianFieldSanitizer.AuthorizationId(new string('A', 60));

        Assert.Equal(DianFieldSanitizer.AuthorizationIdMaxLength, result.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void AuthorizationId_WhenThereIsNoIdentifier_FallsBackToPaddedConsecutive(string? identifier)
    {
        // Vacío también lo rechaza el fiscal, así que se cae al consecutivo con padding a 6.
        Assert.Equal("004321", DianFieldSanitizer.AuthorizationId(identifier, 4321));
    }

    [Fact]
    public void AuthorizationId_WhenThereIsNeitherIdentifierNorConsecutive_StillReturnsSixDigits()
    {
        var result = DianFieldSanitizer.AuthorizationId(null, null);

        Assert.Equal("000000", result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void AuthorizationId_PrefersTheIdentifierOverTheConsecutive()
    {
        // El consecutivo es respaldo, no reemplazo: si hay referencia de Ogloba, manda esa.
        Assert.Equal("00136544716V", DianFieldSanitizer.AuthorizationId("00136544716V", 4321));
    }
}
