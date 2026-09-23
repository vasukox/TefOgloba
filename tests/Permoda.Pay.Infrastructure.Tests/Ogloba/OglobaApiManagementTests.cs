using Permoda.Pay.Infrastructure.Ogloba;

namespace Permoda.Pay.Infrastructure.Tests.Ogloba;

/// <summary>
/// Producción no va directo a Ogloba: va por el API Management de Permoda.
/// <para>
/// El host que la configuración daba por oficial, <c>co-prod.ogloba.com</c>, <b>no resuelve en
/// DNS</b> — comprobado el 2026-09-08 desde una red donde <c>co-ts.ogloba.com</c> sí resuelve.
/// Nunca habría funcionado en una tienda.
/// </para>
/// <para>
/// Las rutas reales salen de la hoja PARAMETROS-OGLOBA del documento de credenciales, y la
/// conexión quedó certificada el 2026-09-09 contra la tienda 037: <c>/ogloba/balanceAM</c> y
/// <c>/ogloba/redemptionAM</c> devolvieron 200 con el código de negocio de Ogloba.
/// </para>
/// </summary>
public sealed class OglobaApiManagementTests
{
    private static OglobaOptions Production() => new(
        new Uri("https://apim-permoda-prod.azure-api.net"),
        usesApiManagement: true);

    private static OglobaOptions Sandbox() => new(
        new Uri("https://co-ts.ogloba.com/gc-restful-gateway/giftCardService"));

    /// <summary>Las once operaciones publicadas, con el nombre exacto de la hoja oficial.</summary>
    [Theory]
    [InlineData("activation", "ogloba/activationAM")]
    [InlineData("redemption", "ogloba/redemptionAM")]
    [InlineData("confirmTransaction", "ogloba/confirmTransactionAM")]
    [InlineData("balance", "ogloba/balanceAM")]
    [InlineData("voidTransaction", "ogloba/voidTransactionAM")]
    [InlineData("cancelTransaction", "ogloba/cancelTransactionAM")]
    [InlineData("reload", "ogloba/reloadAM")]
    [InlineData("reversal", "ogloba/reversalAM")]
    [InlineData("reconciliation", "ogloba/reconciliationAM")]
    [InlineData("queryTransactionsHistory", "ogloba/queryTransactionsHistoryAM")]
    public void Cada_operacion_se_traduce_a_su_recurso_del_APIM(string operation, string expected)
    {
        Assert.Equal(expected, Production().ResolvePath(operation));
    }

    /// <summary>En sandbox la operación ES la ruta: el APIM no existe ahí.</summary>
    [Theory]
    [InlineData("balance")]
    [InlineData("redemption")]
    [InlineData("orderCreation")]
    [InlineData("getProducts")]
    public void En_sandbox_la_ruta_no_se_traduce(string operation)
    {
        Assert.Equal(operation, Sandbox().ResolvePath(operation));
    }

    /// <summary>
    /// Lo que el APIM NO publica falla acá, con el nombre de lo que falta.
    /// <para>
    /// Salir a la red y volver con un 404 sería peor: el módulo lo leería como "respuesta
    /// malformada" y el cajero vería un error que no dice nada. Fallar antes, con el nombre de la
    /// operación, es lo que permite decirle "esto todavía no está disponible".
    /// </para>
    /// <para>
    /// Los cinco <c>order*</c> son la activación de bonos VIRTUALES. Mientras no estén
    /// publicados, esa funcionalidad no opera en producción.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("orderCreation")]
    [InlineData("orderConfirm")]
    [InlineData("orderStatus")]
    [InlineData("orderCancel")]
    [InlineData("orderReturn")]
    [InlineData("getProducts")]
    [InlineData("getBuInfo")]
    [InlineData("test")]
    public void Lo_que_el_APIM_no_publica_falla_con_el_nombre_de_la_operacion(string operation)
    {
        var error = Assert.Throws<NotSupportedException>(() => Production().ResolvePath(operation));

        Assert.Contains(operation, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// La ruta compone bien contra la base. Es donde una barra de más o de menos manda la
    /// petición a un 404 sin que nada en el código se vea mal.
    /// </summary>
    [Fact]
    public void La_ruta_compone_la_URL_completa_que_se_certifico()
    {
        var options = Production();
        var url = new Uri(options.BaseAddress, options.ResolvePath("balance"));

        Assert.Equal("https://apim-permoda-prod.azure-api.net/ogloba/balanceAM", url.AbsoluteUri);
    }

    /// <summary>Sandbox sigue componiendo igual que siempre: este cambio no lo tocó.</summary>
    [Fact]
    public void Sandbox_compone_la_URL_de_siempre()
    {
        var options = Sandbox();
        var url = new Uri(options.BaseAddress, options.ResolvePath("balance"));

        Assert.Equal(
            "https://co-ts.ogloba.com/gc-restful-gateway/giftCardService/balance",
            url.AbsoluteUri);
    }

    /// <summary>Por omisión NADIE usa el APIM: solo el ambiente que lo pide explícitamente.</summary>
    [Fact]
    public void El_APIM_no_se_activa_solo()
    {
        Assert.False(Sandbox().UsesApiManagement);
        Assert.True(Production().UsesApiManagement);
    }

    /// <summary>
    /// El APIM también exige HTTPS. La validación del constructor no se relajó al agregar el
    /// modo nuevo: una URL en claro sigue siendo un error de programación, no una opción.
    /// </summary>
    [Fact]
    public void El_APIM_tampoco_admite_trafico_en_claro()
    {
        Assert.Throws<ArgumentException>(() =>
            new OglobaOptions(new Uri("http://apim-permoda-prod.azure-api.net"), usesApiManagement: true));
    }
}
