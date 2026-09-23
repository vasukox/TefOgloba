using Permoda.Pay.Application.Features.Provisioning;

namespace Permoda.Pay.Application.Tests.Features.Provisioning;

/// <summary>
/// El sobre que una caja le pasa a otra. La regla que vigilan estos tests: lo que es de la TIENDA
/// se copia, lo que es de la CAJA no.
/// </summary>
public sealed class TerminalConfigurationEnvelopeTests
{
    private static TerminalConfigurationEnvelope Sample(
        IReadOnlyList<string>? assigned = null) =>
        new(
            StoreId: "K00537",
            SubscriptionKey: "LLAVE-FALSA-SOLO-PARA-PRUEBAS-01",
            BaseUrl: "https://apim-permoda-prod.azure-api.net",
            ApiVersion: "2.18",
            StoreName: "KOAJ EVENTOS",
            AdminPinHash: "ABC123",
            Cashiers:
            [
                new ReplicatedCashier("felipe", "HASH1", IsEnabled: true),
                new ReplicatedCashier("yaid", "HASH2", IsEnabled: false)
            ],
            AssignedTerminalIds: assigned ?? ["CAJA-01"]);

    [Fact]
    public void El_sobre_sobrevive_la_ida_y_vuelta()
    {
        var restored = TerminalConfigurationEnvelope.FromJson(Sample().ToJson());

        Assert.NotNull(restored);
        Assert.Equal("K00537", restored.StoreId);
        Assert.Equal("LLAVE-FALSA-SOLO-PARA-PRUEBAS-01", restored.SubscriptionKey);
        Assert.Equal("2.18", restored.ApiVersion);
        Assert.Equal("ABC123", restored.AdminPinHash);
    }

    /// <summary>
    /// Un cajero desactivado tiene que llegar desactivado. Si no, el que el administrador sacó de
    /// circulación vuelve a entrar por la caja de al lado.
    /// </summary>
    [Fact]
    public void Los_cajeros_viajan_con_su_hash_y_su_estado()
    {
        var restored = TerminalConfigurationEnvelope.FromJson(Sample().ToJson());

        Assert.NotNull(restored);
        Assert.Equal(2, restored.Cashiers.Count);

        var yaid = restored.Cashiers.Single(cashier => cashier.Id == "yaid");
        Assert.Equal("HASH2", yaid.PasswordHash);
        Assert.False(yaid.IsEnabled);
    }

    /// <summary>
    /// LA regla del diseño. Si el TerminalId se copiara, dos cajas firmarían igual sus operaciones
    /// contra Ogloba y la bitácora dejaría de decir dónde ocurrió cada cobro.
    /// </summary>
    [Fact]
    public void El_sobre_no_lleva_un_TerminalId_para_adoptar()
    {
        var properties = typeof(TerminalConfigurationEnvelope)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("TerminalId", properties);
    }

    [Fact]
    public void Propone_la_primera_caja_libre()
    {
        Assert.Equal("CAJA-02", Sample(["CAJA-01"]).SuggestNextTerminalId());
        Assert.Equal("CAJA-03", Sample(["CAJA-01", "CAJA-02"]).SuggestNextTerminalId());
        Assert.Equal("CAJA-04", Sample(["CAJA-01", "CAJA-02", "CAJA-03"]).SuggestNextTerminalId());
    }

    /// <summary>
    /// Un hueco en la numeración se reutiliza: si la CAJA-02 se dio de baja, la siguiente entra
    /// ahí en vez de dejar el hueco para siempre.
    /// </summary>
    [Fact]
    public void Aprovecha_los_huecos_de_numeracion()
    {
        Assert.Equal("CAJA-02", Sample(["CAJA-01", "CAJA-03"]).SuggestNextTerminalId());
    }

    [Fact]
    public void La_comparacion_de_cajas_ignora_mayusculas_y_espacios()
    {
        var envelope = Sample(["CAJA-01", "caja-02"]);

        Assert.True(envelope.IsTerminalIdTaken("CAJA-02"));
        Assert.True(envelope.IsTerminalIdTaken("  caja-01  "));
        Assert.False(envelope.IsTerminalIdTaken("CAJA-09"));
        Assert.Equal("CAJA-03", envelope.SuggestNextTerminalId());
    }

    [Fact]
    public void Un_nombre_vacio_no_cuenta_como_tomado()
    {
        var envelope = Sample();

        Assert.False(envelope.IsTerminalIdTaken(null));
        Assert.False(envelope.IsTerminalIdTaken("   "));
    }

    /// <summary>
    /// Lo que llega acá viene de la red. Un formato roto termina en "configura a mano", nunca en
    /// un crash a mitad de la instalación de una tienda.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no es json")]
    [InlineData("{")]
    [InlineData("[]")]
    public void Una_entrada_rota_devuelve_null_en_vez_de_lanzar(string? json)
    {
        Assert.Null(TerminalConfigurationEnvelope.FromJson(json));
    }

    /// <summary>
    /// Media configuración es peor que ninguna: se guarda, parece que funcionó, y falla después
    /// con un error de Ogloba que no apunta acá.
    /// </summary>
    [Theory]
    [InlineData("StoreId")]
    [InlineData("SubscriptionKey")]
    [InlineData("BaseUrl")]
    [InlineData("ApiVersion")]
    public void Un_sobre_al_que_le_falta_lo_esencial_se_rechaza(string missingField)
    {
        // Se vacía el campo en el objeto y se serializa, en vez de editar el JSON a mano: así el
        // test no depende del formato exacto que produzca el serializador.
        var broken = missingField switch
        {
            "StoreId" => Sample() with { StoreId = "" },
            "SubscriptionKey" => Sample() with { SubscriptionKey = "" },
            "BaseUrl" => Sample() with { BaseUrl = "" },
            _ => Sample() with { ApiVersion = "" }
        };

        Assert.Null(TerminalConfigurationEnvelope.FromJson(broken.ToJson()));
    }

    /// <summary>
    /// Las 512 tiendas no se actualizan el mismo día: una caja con el APK nuevo se va a encontrar
    /// cajas con el viejo. Un sobre de otra versión se RECHAZA en vez de interpretarse a medias.
    /// </summary>
    [Fact]
    public void Un_sobre_de_otra_version_se_rechaza()
    {
        var json = Sample().ToJson().Replace("\"v\":1", "\"v\":2", StringComparison.Ordinal);

        Assert.Null(TerminalConfigurationEnvelope.FromJson(json));
    }
}
