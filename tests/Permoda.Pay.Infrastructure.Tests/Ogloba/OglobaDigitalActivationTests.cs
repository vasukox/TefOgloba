using System.Net;
using System.Text.Json;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;
using Permoda.Pay.Infrastructure.Ogloba;
using Permoda.Pay.Infrastructure.Tests.TestDoubles;

namespace Permoda.Pay.Infrastructure.Tests.Ogloba;

/// <summary>
/// Activación de una tarjeta DIGITAL en tienda física: por <c>/activation</c>, con
/// <c>gencode</c> y <c>email</c>.
/// <para>
/// Confirmado por Ogloba el 2026-09-09, respondiendo a nuestra solicitud de que publicaran Order
/// management en el APIM: «<i>orderCreation, orderConfirm, orderStatus y orderCancel normalmente
/// se utilizan para generar lotes de tarjetas (alto volumen). Para la activación de una tarjeta
/// digital en tienda física deben usar la API activation e incluir dos parámetros: gencode y
/// email... Una vez que se confirme la activación, la tarjeta será enviada al beneficiario según
/// la dirección de correo electrónico que se haya colocado.</i>»
/// </para>
/// <para>
/// El módulo usaba Order management, y como el APIM no lo publica dimos el bono digital por
/// imposible en producción. No lo era: el camino correcto siempre estuvo publicado.
/// </para>
/// </summary>
public sealed class OglobaDigitalActivationTests
{
    private const string DigitalGencode = "113811";
    private const string CustomerEmail = "cliente@correo.com";

    private static readonly string SuccessBody = """
        {"errorCode":"0","isSuccessful":true,"referenceNumber":"00136544716V",
         "cardNumber":"1138110025515937","balance":"50000"}
        """;

    private static async Task<JsonElement> ActivateAsync(
        CardIdentifier card,
        string? email)
    {
        string? body = null;

        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessBody, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var provider = new OglobaGiftCardProvider(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new OglobaOptions(
                new Uri("https://ogloba.test/giftCardService"),
                requestTimeout: TimeSpan.FromSeconds(5)),
            new StubOglobaCredentialProvider("test-password"));

        await provider.ActivateAsync(
            new RedemptionRequest(
                TransactionNumber.Create("1788968903").Value,
                StoreId.Create("K00037").Value,
                TerminalId.Create("CAJA-01").Value,
                CashierId.Create("felipe").Value,
                card,
                Money.Create(50_000).Value,
                Email: email),
            CancellationToken.None);

        Assert.NotNull(body);
        return JsonDocument.Parse(body!).RootElement;
    }

    private static string? Read(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    /// <summary>
    /// La forma exacta del ejemplo de Ogloba: <c>gencode</c> con el producto, <c>cardNumber</c>
    /// vacío y el correo del beneficiario.
    /// </summary>
    [Fact]
    public async Task La_activacion_digital_manda_gencode_y_email()
    {
        var payload = await ActivateAsync(
            CardIdentifier.CreateDigitalGencode(DigitalGencode).Value,
            CustomerEmail);

        Assert.Equal(DigitalGencode, Read(payload, "gencode"));
        Assert.Equal(CustomerEmail, Read(payload, "email"));
        Assert.Equal(string.Empty, Read(payload, "cardNumber"));
    }

    /// <summary>
    /// Sin el correo, Ogloba no tiene a dónde enviar el bono: el cliente paga y no recibe nada.
    /// Este test existe para que nadie lo quite "por simplificar".
    /// </summary>
    [Fact]
    public async Task Sin_el_correo_el_bono_digital_no_tendria_a_donde_llegar()
    {
        var payload = await ActivateAsync(
            CardIdentifier.CreateDigitalGencode(DigitalGencode).Value,
            CustomerEmail);

        Assert.False(
            string.IsNullOrWhiteSpace(Read(payload, "email")),
            "El correo es lo que hace que Ogloba entregue el bono digital.");
    }

    /// <summary>
    /// En una tarjeta FÍSICA no se manda correo: el cliente ya tiene el plástico en la mano y no
    /// hay nada que enviar. Mandarlo le pediría a Ogloba una entrega que nadie espera.
    /// </summary>
    [Fact]
    public async Task La_activacion_fisica_no_manda_correo()
    {
        var payload = await ActivateAsync(
            CardIdentifier.CreatePhysicalCard("1138170025515937").Value,
            email: CustomerEmail);

        Assert.Null(Read(payload, "email"));
        Assert.Equal("1138170025515937", Read(payload, "cardNumber"));
    }

    /// <summary>
    /// Y en una REDENCIÓN tampoco, aunque venga el campo lleno: redimir no emite nada.
    /// </summary>
    [Fact]
    public async Task La_redencion_no_manda_correo()
    {
        string? body = null;

        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessBody, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var provider = new OglobaGiftCardProvider(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new OglobaOptions(
                new Uri("https://ogloba.test/giftCardService"),
                requestTimeout: TimeSpan.FromSeconds(5)),
            new StubOglobaCredentialProvider("test-password"));

        await provider.RedeemAsync(
            new RedemptionRequest(
                TransactionNumber.Create("1788968903").Value,
                StoreId.Create("K00037").Value,
                TerminalId.Create("CAJA-01").Value,
                CashierId.Create("felipe").Value,
                CardIdentifier.CreatePhysicalCard("1138170025515937").Value,
                Money.Create(50_000).Value),
            CancellationToken.None);

        var payload = JsonDocument.Parse(body!).RootElement;

        Assert.Null(Read(payload, "email"));
    }
}
