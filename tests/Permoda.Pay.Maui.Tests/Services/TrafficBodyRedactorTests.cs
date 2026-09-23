using Permoda.Pay.Application.Abstractions.Logging;

namespace Permoda.Pay.Maui.Tests.Services;

/// <summary>
/// Tapado de datos sensibles en la bitácora de tráfico.
/// <para>
/// El PAN ya se enmascaraba antes de tocar disco en la base cifrada, pero en RAM no: la bitácora
/// guardaba petición y respuesta íntegras —seriales, cédulas y nombres— y es exportable a XLSX
/// desde administración. El mismo dato protegido en la base salía en claro por ahí.
/// </para>
/// </summary>
public sealed class TrafficBodyRedactorTests
{
    /// <summary>
    /// Misma forma que la máscara de la base cifrada, para que un serial se vea igual en los dos
    /// sitios y nadie dude si es el mismo.
    /// </summary>
    [Fact]
    public void El_serial_del_bono_queda_enmascarado_como_en_la_base()
    {
        var body = """{"cardNumber":"1138170025515937","amount":50000}""";

        var redacted = TrafficBodyRedactor.Redact(body);

        Assert.DoesNotContain("1138170025515937", redacted, StringComparison.Ordinal);
        Assert.Contains("113817******5937", redacted, StringComparison.Ordinal);
    }

    /// <summary>La cédula del cliente es un dato personal, no un identificador de operación.</summary>
    [Fact]
    public void La_cedula_del_cliente_queda_enmascarada()
    {
        var redacted = TrafficBodyRedactor.Redact("""{"fiscalId":"1020304050"}""");

        Assert.DoesNotContain("1020304050", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// El nombre no es un número, así que la regla de dígitos no lo alcanza: hay que taparlo por
    /// el nombre del campo. Es donde viaja el cliente en las activaciones virtuales.
    /// </summary>
    [Theory]
    [InlineData("receiverMobileNo")]
    [InlineData("customerName")]
    [InlineData("senderName")]
    [InlineData("email")]
    [InlineData("note")]
    [InlineData("message")]
    public void Los_campos_de_texto_libre_se_tapan(string field)
    {
        var body = $$"""{"{{field}}":"YAID ARIAS MIRANDA"}""";

        var redacted = TrafficBodyRedactor.Redact(body);

        Assert.DoesNotContain("YAID", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("***", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lo que NO es sensible se conserva. Tapar de más deja la bitácora inútil para rastrear un
    /// caso, que es exactamente para lo que existe.
    /// </summary>
    [Fact]
    public void Se_conserva_lo_que_sirve_para_rastrear()
    {
        var body = """{"responseCode":"0","responseMessage":"OK","storeId":"K00036"}""";

        var redacted = TrafficBodyRedactor.Redact(body);

        Assert.Contains("responseCode", redacted, StringComparison.Ordinal);
        Assert.Contains("OK", redacted, StringComparison.Ordinal);
        Assert.Contains("K00036", redacted, StringComparison.Ordinal);
    }

    /// <summary>Montos y códigos cortos no se tocan: son la operación, no el cliente.</summary>
    [Theory]
    [InlineData("50000")]
    [InlineData("113816")]
    [InlineData("0")]
    public void Los_numeros_cortos_no_se_tocan(string number)
    {
        var body = $$"""{"amount":"{{number}}"}""";

        Assert.Contains(number, TrafficBodyRedactor.Redact(body), StringComparison.Ordinal);
    }

    /// <summary>Un cuerpo vacío o nulo no puede tumbar el registro de una operación.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Un_cuerpo_vacio_no_revienta(string? body)
    {
        Assert.Equal(string.Empty, TrafficBodyRedactor.Redact(body));
    }

    /// <summary>Varios seriales en el mismo cuerpo: se tapan todos, no solo el primero.</summary>
    [Fact]
    public void Se_tapan_todas_las_apariciones()
    {
        var body = """{"a":"1138170025515937","b":"1138170025519999"}""";

        var redacted = TrafficBodyRedactor.Redact(body);

        Assert.DoesNotContain("1138170025515937", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("1138170025519999", redacted, StringComparison.Ordinal);
    }
}
