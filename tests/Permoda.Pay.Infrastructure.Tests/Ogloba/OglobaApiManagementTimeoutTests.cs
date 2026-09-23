using Permoda.Pay.Infrastructure.Ogloba;

namespace Permoda.Pay.Infrastructure.Tests.Ogloba;

/// <summary>
/// Cuánto esperamos por el API Management, y por qué es menos que contra Ogloba directo.
/// <para>
/// La hoja de parámetros declara <c>Og_WsTimeOut = 50.000 ms</c>. Si la pasarela corta a los 50
/// segundos y nosotros seguimos esperando hasta los 90, lo que vuelve es el error del gateway —
/// que el módulo lee como rechazo. Pero la redención pudo haberse aplicado en Ogloba del otro
/// lado, y habríamos dado por fallido un cobro que sí ocurrió: al cliente se le descontó y la
/// venta dice que no.
/// </para>
/// <para>
/// Cortando nosotros primero, el corte entra por el camino de resultado incierto, que es el que
/// dispara la recuperación al arrancar. "No sé qué pasó, lo averiguo" es recuperable; "falló"
/// cuando en realidad se aplicó, no.
/// </para>
/// </summary>
public sealed class OglobaApiManagementTimeoutTests
{
    private static OglobaOptions Production(TimeSpan? timeout = null) => new(
        new Uri("https://apim-permoda-prod.azure-api.net"),
        requestTimeout: timeout,
        usesApiManagement: true);

    private static OglobaOptions Direct() => new(
        new Uri("https://co-ts.ogloba.com/gc-restful-gateway/giftCardService"));

    /// <summary>Lo esencial: nuestro corte llega ANTES que el de la pasarela.</summary>
    [Fact]
    public void Por_el_APIM_cortamos_antes_que_la_pasarela()
    {
        var gatewayTimeout = TimeSpan.FromMilliseconds(50_000);

        Assert.True(
            Production().RequestTimeout < gatewayTimeout,
            "Si esperamos más que la pasarela, su error tapa nuestro camino de resultado "
            + "incierto y un cobro aplicado puede quedar como fallido.");
    }

    /// <summary>
    /// Y no tan corto como para cortar una operación que iba bien. El manual de Ogloba da ~90 s a
    /// una redención; recortarlo a unos pocos segundos convertiría cada pico de latencia en una
    /// transacción de estado desconocido.
    /// </summary>
    [Fact]
    public void Pero_no_tan_corto_como_para_cortar_una_operacion_sana()
    {
        Assert.True(Production().RequestTimeout >= TimeSpan.FromSeconds(30));
    }

    /// <summary>Contra Ogloba directo no cambia nada: siguen siendo los 90 s del manual.</summary>
    [Fact]
    public void Contra_Ogloba_directo_se_conservan_los_noventa_segundos()
    {
        Assert.Equal(OglobaOptions.DefaultRequestTimeout, Direct().RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(90), Direct().RequestTimeout);
    }

    /// <summary>Un tope explícito manda sobre el del ambiente: es lo que usan las pruebas.</summary>
    [Fact]
    public void Un_tope_explicito_manda_sobre_el_del_ambiente()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), Production(TimeSpan.FromSeconds(5)).RequestTimeout);
    }

    /// <summary>
    /// El tope de las consultas nunca supera al general: una lectura no puede esperar más que un
    /// cobro. Con el APIM el general baja, así que esta invariante hay que revisarla acá.
    /// </summary>
    [Fact]
    public void La_consulta_nunca_espera_mas_que_el_cobro()
    {
        Assert.True(Production().QueryTimeout <= Production().RequestTimeout);
        Assert.True(Direct().QueryTimeout <= Direct().RequestTimeout);
    }
}
