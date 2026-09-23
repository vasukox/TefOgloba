using Permoda.Pay.Application;
using Xunit;

namespace Permoda.Pay.Application.Tests.Common;

public sealed class CustomerMobileNoBuilderTests
{
    [Fact]
    public void Build_NombreCompletoConTildesYDocumento_ConcatenaSinEspacios()
    {
        var result = CustomerMobileNoBuilder.Build(
            documentNumber: "1011086580",
            fullName: "Andrés Felipe Díaz Bernal");

        Assert.Equal("1011086580-AndresFelipeDiazBernal", result);
    }

    [Fact]
    public void Build_NombreConDiacriticosVariados_LimpiaTodosLosCaracteres()
    {
        var result = CustomerMobileNoBuilder.Build(
            documentNumber: "123456789",
            fullName: "José María Núñez Gámez");

        Assert.Equal("123456789-JoseMariaNunezGamez", result);
    }

    [Fact]
    public void Build_SinSegundoNombre_NoDejaGuionDoble()
    {
        var result = CustomerMobileNoBuilder.Build(
            documentNumber: "123",
            fullName: "Juan Pérez");

        Assert.Equal("123-JuanPerez", result);
    }

    [Fact]
    public void Build_SoloDocumento_DevuelveSoloElDocumento()
    {
        var result = CustomerMobileNoBuilder.Build(
            documentNumber: "1011086580",
            fullName: null);

        Assert.Equal("1011086580", result);
    }

    [Fact]
    public void Build_SoloNombre_DevuelveSoloElNombreNormalizado()
    {
        var result = CustomerMobileNoBuilder.Build(
            documentNumber: null,
            fullName: "maria fernanda lópez");

        Assert.Equal("MariaFernandaLopez", result);
    }

    [Fact]
    public void Build_SinDatos_DevuelveCadenaVacia()
    {
        Assert.Equal(string.Empty, CustomerMobileNoBuilder.Build(null, null));
        Assert.Equal(string.Empty, CustomerMobileNoBuilder.Build(string.Empty, string.Empty));
        Assert.Equal(string.Empty, CustomerMobileNoBuilder.Build("   ", "   "));
    }

    [Fact]
    public void Build_NombreConEspaciosMultiplesYMayusculasMixtas_NormalizaCorrectamente()
    {
        var result = CustomerMobileNoBuilder.Build(
            documentNumber: "555",
            fullName: "  aNa   MaRíA  roDRíguEZ  ");

        Assert.Equal("555-AnaMariaRodriguez", result);
    }

    [Fact]
    public void Build_NombreConCaracteresNoLetales_SeparaCorrectamente()
    {
        // El separador (guión en el input) marca el inicio de la siguiente palabra.
        var result = CustomerMobileNoBuilder.Build(
            documentNumber: "777",
            fullName: "Pérez-García María");

        Assert.Equal("777-PerezGarciaMaria", result);
    }

    [Fact]
    public void Build_ResultadoExcedeMaxLength_TruncaRespetandoDocumentoYGuion()
    {
        // Nombre muy largo: el helper trunca a 100 chars, preservando el documento y el guión.
        var longName = new string('a', 200);
        var result = CustomerMobileNoBuilder.Build("1234567890", longName);

        Assert.Equal(CustomerMobileNoBuilder.MaxLength, result.Length);
        Assert.StartsWith("1234567890-", result);
    }
}
