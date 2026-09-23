using System.Net;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;
using Permoda.Pay.Infrastructure.Ogloba;
using Permoda.Pay.Infrastructure.Tests.TestDoubles;

namespace Permoda.Pay.Infrastructure.Tests.Ogloba;

/// <summary>
/// Lo que sale por el cable cuando el ambiente va por el API Management de Permoda.
/// <para>
/// Estos tests miran la petición REAL que arma el proveedor, no la configuración. Es donde se
/// rompe una integración de verdad: la ruta bien calculada pero el header equivocado, o el Basic
/// Auth que se queda pegado y el APIM rechaza.
/// </para>
/// </summary>
public sealed class OglobaApiManagementRequestTests
{
    private const string SubscriptionKey = "LLAVE-FALSA-SOLO-PARA-PRUEBAS-02";

    private static readonly string SuccessBody = """
        {"errorCode":"0","isSuccessful":true,"referenceNumber":"00136544716V","balance":"0"}
        """;

    private sealed record Captured(Uri? Uri, string? SubscriptionKey, string? AuthorizationScheme);

    private static async Task<Captured> RedeemAsync(bool usesApiManagement)
    {
        Captured? captured = null;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            captured = new Captured(
                request.RequestUri,
                request.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out var key)
                    ? key.FirstOrDefault()
                    : null,
                request.Headers.Authorization?.Scheme);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessBody, System.Text.Encoding.UTF8, "application/json")
            });
        });

        var options = usesApiManagement
            ? new OglobaOptions(
                new Uri("https://apim-permoda-prod.azure-api.net"),
                requestTimeout: TimeSpan.FromSeconds(5),
                usesApiManagement: true)
            : new OglobaOptions(
                new Uri("https://co-ts.ogloba.com/gc-restful-gateway/giftCardService"),
                requestTimeout: TimeSpan.FromSeconds(5));

        var provider = new OglobaGiftCardProvider(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            options,
            new StubOglobaCredentialProvider(SubscriptionKey));

        await provider.RedeemAsync(
            new RedemptionRequest(
                TransactionNumber.Create("1756113296").Value,
                StoreId.Create("K00037").Value,
                TerminalId.Create("CAJA-01").Value,
                CashierId.Create("felipe").Value,
                CardIdentifier.CreatePhysicalCard("1138170025515937").Value,
                Money.Create(50_000).Value),
            CancellationToken.None);

        Assert.NotNull(captured);
        return captured!;
    }

    /// <summary>
    /// La URL exacta que se certificó contra la tienda 037 el 2026-09-09. Si este test cambia,
    /// alguien movió la ruta que sabemos que funciona.
    /// </summary>
    [Fact]
    public async Task Por_el_APIM_la_redencion_va_a_la_ruta_certificada()
    {
        var captured = await RedeemAsync(usesApiManagement: true);

        Assert.Equal(
            "https://apim-permoda-prod.azure-api.net/ogloba/redemptionAM",
            captured.Uri?.AbsoluteUri);
    }

    /// <summary>La llave de la tienda viaja en el header que el APIM espera.</summary>
    [Fact]
    public async Task Por_el_APIM_la_llave_viaja_en_Ocp_Apim_Subscription_Key()
    {
        var captured = await RedeemAsync(usesApiManagement: true);

        Assert.Equal(SubscriptionKey, captured.SubscriptionKey);
    }

    /// <summary>
    /// Y NO va Basic Auth. Mandar los dos no es "por si acaso": el APIM guarda la credencial real
    /// de Ogloba, y adjuntar además un Basic con la llave dentro filtraría la llave en un header
    /// que nadie lee y que queda en los logs de la pasarela.
    /// </summary>
    [Fact]
    public async Task Por_el_APIM_no_se_manda_Basic_Auth()
    {
        var captured = await RedeemAsync(usesApiManagement: true);

        Assert.Null(captured.AuthorizationScheme);
    }

    /// <summary>
    /// Una operación que el APIM no publica devuelve un FALLO, no una excepción, y no llega a
    /// salir a la red.
    /// <para>
    /// Esto se descubre en el hilo de un cobro. Una excepción ahí tumbaría la operación con el
    /// cliente enfrente; un fallo con motivo deja al cajero seguir por otro medio. La primera
    /// versión de este cambio lanzaba <c>NotSupportedException</c> fuera del <c>try</c> del
    /// proveedor: habría reventado en producción al primer bono digital.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Una_operacion_no_publicada_devuelve_fallo_y_ni_siquiera_sale_a_la_red()
    {
        var reached = false;

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            reached = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var provider = new OglobaGiftCardProvider(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new OglobaOptions(
                new Uri("https://apim-permoda-prod.azure-api.net"),
                requestTimeout: TimeSpan.FromSeconds(5),
                usesApiManagement: true),
            new StubOglobaCredentialProvider(SubscriptionKey));

        // getProducts no está publicado en el APIM (medido: 404).
        var result = await provider.GetProductsAsync(
            StoreId.Create("K00037").Value, null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("ogloba.operation_not_published", result.Failure.Code);
        Assert.Contains("getProducts", result.Failure.Description, StringComparison.Ordinal);
        Assert.False(reached, "No debe gastarse una llamada de red en algo que no existe.");
    }

    /// <summary>
    /// Sandbox no cambió: sigue con Basic Auth contra el host directo. El cambio de producción no
    /// puede llevarse por delante el ambiente donde se certifica.
    /// </summary>
    [Fact]
    public async Task En_sandbox_todo_sigue_igual()
    {
        var captured = await RedeemAsync(usesApiManagement: false);

        Assert.Equal(
            "https://co-ts.ogloba.com/gc-restful-gateway/giftCardService/redemption",
            captured.Uri?.AbsoluteUri);
        Assert.Equal("Basic", captured.AuthorizationScheme);
        Assert.Null(captured.SubscriptionKey);
    }
}
