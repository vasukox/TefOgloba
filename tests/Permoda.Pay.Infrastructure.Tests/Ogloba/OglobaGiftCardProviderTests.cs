using System.Net;
using System.Text;
using System.Text.Json;
using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;
using Permoda.Pay.Infrastructure.Ogloba;
using Permoda.Pay.Infrastructure.Ogloba.Authentication;
using Permoda.Pay.Infrastructure.Tests.TestDoubles;

namespace Permoda.Pay.Infrastructure.Tests.Ogloba;

public sealed class OglobaGiftCardProviderTests
{
    [Fact]
    public async Task RedeemAsync_SendsRequiredHeadersAuthenticationAndJsonContract()
    {
        var captured = new CapturedRequest();
        var handler = CreateHandler(
            captured,
            JsonResponse("""
                {
                  "isSuccessful": true,
                  "referenceNumber": "00136544716V",
                  "balance": 0,
                  "currency": "COP"
                }
                """));
        var credentials = new StubOglobaCredentialProvider("test-password");
        var provider = CreateProvider(handler, credentials);

        var result = await provider.RedeemAsync(CreateRedemptionRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("00136544716V", result.Value.ReferenceNumber.Value);
        Assert.Equal(0, result.Value.RemainingBalance.MinorUnits);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal(
            "https://ogloba.test/giftCardService/redemption",
            captured.Uri?.AbsoluteUri);
        Assert.Equal("2.18", captured.ApiVersion);
        Assert.Equal("Basic", captured.AuthorizationScheme);
        Assert.Equal(
            "K00036:test-password",
            DecodeBasicCredential(captured.AuthorizationParameter));
        Assert.Equal(["K00036"], credentials.RequestedStoreIds);

        using var body = JsonDocument.Parse(captured.Body!);
        var root = body.RootElement;

        Assert.Equal("K00036", root.GetProperty("merchantId").GetString());
        Assert.Equal("caja-5", root.GetProperty("terminalId").GetString());
        Assert.Equal("operador-123", root.GetProperty("cashierId").GetString());
        Assert.Equal("1756113296", root.GetProperty("transactionNumber").GetString());
        // Confirmado en vivo contra sandbox (2026-07-28, store K00036, gencode 113815): Ogloba
        // espera el monto en PESOS tal cual, no en centavos — enviar x100 causa errorCode 73
        // "Wrong activation amount". Money.MinorUnits = 50_000 pesos, se envía sin transformar.
        Assert.Equal(50_000, root.GetProperty("amount").GetInt64());
        Assert.Equal("COP", root.GetProperty("currency").GetString());
        Assert.Equal("1138170025515937", root.GetProperty("cardNumber").GetString());
        Assert.DoesNotContain("test-password", captured.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmAsync_SerializesConfirmContract()
    {
        var captured = new CapturedRequest();
        var handler = CreateHandler(captured, JsonResponse("""{"isSuccessful":true}"""));
        var provider = CreateProvider(handler);
        var request = CreateTransactionActionRequest();

        var result = await provider.ConfirmAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/confirmTransaction", captured.Uri?.AbsoluteUri, StringComparison.Ordinal);

        using var body = JsonDocument.Parse(captured.Body!);
        var root = body.RootElement;

        // Postman oficial ("Ogloba - GiftCard integration", Step 2): confirmTransaction solo
        // lleva merchantId/terminalId/cashierId/referenceNumber — sin amount ni
        // transactionNumber (esos campos son de voidTransaction/reversal).
        Assert.Equal("K00036", root.GetProperty("merchantId").GetString());
        Assert.Equal("caja-5", root.GetProperty("terminalId").GetString());
        Assert.Equal("operador-123", root.GetProperty("cashierId").GetString());
        Assert.Equal("00136544716V", root.GetProperty("referenceNumber").GetString());
        Assert.False(root.TryGetProperty("amount", out _));
        Assert.False(root.TryGetProperty("transactionNumber", out _));
    }

    [Fact]
    public async Task GetBalanceAsync_SendsAllRequiredFieldsFromPostmanDocSection314()
    {
        // docs/OGLOBA_API_REFERENCE.md §3.14: el root cause del HTTP 400 que vimos en UAT
        // fue enviar solo merchantId + cardNumber. El contrato exige 6 campos; verificamos
        // que estén todos, porque sin terminalId/cashierId/transactionNumber Ogloba rechaza.
        var captured = new CapturedRequest();
        var handler = CreateHandler(
            captured,
            JsonResponse("""
                {
                  "isSuccessful": true,
                  "balance": 50000,
                  "currency": "COP",
                  "cardStatus": "ACTIVE",
                  "expireDate": "20270101"
                }
                """));
        var provider = CreateProvider(handler);

        var result = await provider.GetBalanceAsync(
            new BalanceQuery(
                CreateStoreId(),
                CreateTerminalId(),
                CreateCashierId(),
                CreateTransactionNumber(),
                CardIdentifier.CreatePhysicalCard("1138170025515937").Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var body = JsonDocument.Parse(captured.Body!);
        var root = body.RootElement;

        Assert.Equal("K00036", root.GetProperty("merchantId").GetString());
        Assert.Equal("caja-5", root.GetProperty("terminalId").GetString());
        Assert.Equal("operador-123", root.GetProperty("cashierId").GetString());
        Assert.Equal("1756113296", root.GetProperty("transactionNumber").GetString());
        Assert.Equal("1138170025515937", root.GetProperty("cardNumber").GetString());
        Assert.False(root.TryGetProperty("pinCode", out _));
    }

    [Fact]
    public async Task RedeemAsync_SendsOptionalPinNoteAndTrack2WhenProvided()
    {
        // docs/OGLOBA_API_REFERENCE.md §3.2: pinCode protege contra el bloqueo por 3
        // intentos incorrectos; note se guarda en el servidor; track2Data aplica cuando
        // la tarjeta pasa por un lector de banda magnética.
        var captured = new CapturedRequest();
        var handler = CreateHandler(
            captured,
            JsonResponse("""{"isSuccessful":true,"referenceNumber":"00136544716V","balance":0,"currency":"COP"}"""));
        var provider = CreateProvider(handler);

        var result = await provider.RedeemAsync(
            new RedemptionRequest(
                CreateTransactionNumber(),
                CreateStoreId(),
                CreateTerminalId(),
                CreateCashierId(),
                CardIdentifier.CreatePhysicalCard("1138170025515937").Value,
                Money.Create(50_000).Value,
                PinCode: "1234",
                Note: "Venta #1234",
                Track2Data: ";1138170025515937=2512101123456789?"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var body = JsonDocument.Parse(captured.Body!);
        var root = body.RootElement;

        Assert.Equal("1234", root.GetProperty("pinCode").GetString());
        Assert.Equal("Venta #1234", root.GetProperty("note").GetString());
        Assert.Equal(";1138170025515937=2512101123456789?", root.GetProperty("track2Data").GetString());
    }

    [Fact]
    public async Task RedeemAsync_DeserializesRequestedCurrencyAndRedeemShareDet()
    {
        // docs/OGLOBA_API_REFERENCE.md §3.2: /redemption puede traer requestedCurrency (cuando
        // la moneda del bono ≠ transacción) y redeemShareDet (split entre saldo + recarga).
        // System.Text.Json los ignoraba antes por no estar declarados en el DTO.
        var captured = new CapturedRequest();
        var handler = CreateHandler(
            captured,
            JsonResponse("""
                {
                  "isSuccessful": true,
                  "referenceNumber": "00136544716V",
                  "balance": 0,
                  "currency": "COP",
                  "toBeChargedAmt": 50000,
                  "requestedCurrency": {
                    "currency": "USD",
                    "exchangeRate": 0.00024,
                    "balance": 12,
                    "previousBalance": 24,
                    "initialBalance": 0
                  },
                  "redeemShareDet": [
                    { "sharedAmt": 30000, "reloadSource": "0" },
                    { "sharedAmt": 20000, "reloadSource": "1" }
                  ]
                }
                """));
        var provider = CreateProvider(handler);

        var result = await provider.RedeemAsync(CreateRedemptionRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(50_000, result.Value.ToBeChargedAmt);

        Assert.NotNull(result.Value.RequestedCurrency);
        Assert.Equal("USD", result.Value.RequestedCurrency!.Currency);
        Assert.Equal(0.00024, result.Value.RequestedCurrency.ExchangeRate);
        Assert.Equal(12, result.Value.RequestedCurrency.Balance);

        Assert.NotNull(result.Value.RedeemShareDet);
        Assert.Equal(2, result.Value.RedeemShareDet!.Count);
        Assert.Equal(30_000, result.Value.RedeemShareDet[0].SharedAmt);
        Assert.Equal("0", result.Value.RedeemShareDet[0].ReloadSource);
        Assert.Equal(20_000, result.Value.RedeemShareDet[1].SharedAmt);
        Assert.Equal("1", result.Value.RedeemShareDet[1].ReloadSource);
    }

    [Fact]
    public async Task ReverseAsync_UsesSameTransactionNumberAndOmitsUnknownReference()
    {
        var captured = new CapturedRequest();
        var handler = CreateHandler(captured, JsonResponse("""{"isSuccessful":true}"""));
        var provider = CreateProvider(handler);
        var request = new ReversalRequest(
            CreateTransactionNumber(),
            null,
            CreateStoreId(),
            CreateTerminalId(),
            CreateCashierId(),
            OriginalMerchantId: "K00036",
            OriginalTerminalId: "caja-5",
            OriginalCashierId: "operador-123",
            OriginalTransactionNumber: "1756113295");

        var result = await provider.ReverseAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/reversal", captured.Uri?.AbsoluteUri, StringComparison.Ordinal);

        using var body = JsonDocument.Parse(captured.Body!);
        var root = body.RootElement;

        Assert.Equal("1756113296", root.GetProperty("transactionNumber").GetString());
        Assert.False(root.TryGetProperty("referenceNumber", out _));
        Assert.Equal("K00036", root.GetProperty("originalMerchantId").GetString());
        Assert.Equal("caja-5", root.GetProperty("originalTerminalId").GetString());
        Assert.Equal("operador-123", root.GetProperty("originalCashierId").GetString());
        Assert.Equal("1756113295", root.GetProperty("originalTransNumber").GetString());
    }

    [Theory]
    [InlineData(ReconciliationFinalStatus.Successful, "Y")]
    [InlineData(ReconciliationFinalStatus.Failed, "N")]
    public async Task ReconcileAsync_MapsTransactionAndFinalStatusCodes(
        ReconciliationFinalStatus finalStatus,
        string expectedFinalStatus)
    {
        var captured = new CapturedRequest();
        var handler = CreateHandler(captured, JsonResponse("""{"isSuccessful":true}"""));
        var provider = CreateProvider(handler);
        var request = new ReconciliationRequest(
            CreateTransactionNumber(),
            CreateReferenceNumber(),
            CreateStoreId(),
            CreateTerminalId(),
            CreateCashierId(),
            CardIdentifier.CreatePhysicalCard("1138170025515937").Value,
            Money.Create(50_000).Value,
            GiftCardOperation.Redemption,
            finalStatus);

        var result = await provider.ReconcileAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/reconciliation", captured.Uri?.AbsoluteUri, StringComparison.Ordinal);

        using var body = JsonDocument.Parse(captured.Body!);
        // Postman oficial ("Ogloba - GiftCard integration", Step 3): businessDate al nivel
        // superior, terminalId/cashierId/cardNumber/currency por registro (no un solo
        // terminalId de tienda como antes).
        Assert.True(body.RootElement.TryGetProperty("businessDate", out _));
        var detail = body.RootElement.GetProperty("reconciliationRecords")[0];

        Assert.Equal("P", detail.GetProperty("transactionType").GetString());
        Assert.Equal(expectedFinalStatus, detail.GetProperty("finalStatus").GetString());
        Assert.Equal("00136544716V", detail.GetProperty("referenceNumber").GetString());
        Assert.Equal("caja-5", detail.GetProperty("terminalId").GetString());
        Assert.Equal("operador-123", detail.GetProperty("cashierId").GetString());
        // PAN ya no se envía en /reconciliation: no hace falta en Ogloba (el ejemplo
        // del Postman no lo incluye) y, como la BD local solo guarda PAN enmascarado,
        // mandarlo arrastraría el serial enmascarado al servidor.
        Assert.False(detail.TryGetProperty("cardNumber", out _));
    }

    [Fact]
    public async Task ProviderError_MapsNumericCodeAsBusinessRejection()
    {
        var handler = CreateHandler(
            new CapturedRequest(),
            JsonResponse("""
                {
                  "isSuccessful": false,
                  "errorCode": 53,
                  "errorMessage": "Insufficient balance"
                }
                """));
        var provider = CreateProvider(handler);

        var result = await provider.RedeemAsync(CreateRedemptionRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PortFailureType.Rejected, result.Failure.Type);
        Assert.Equal("53", result.Failure.Code);
        Assert.Equal("Insufficient balance", result.Failure.Description);
    }

    [Fact]
    public async Task UnauthorizedHttpResponse_MapsAuthenticationFailure()
    {
        var handler = CreateHandler(
            new CapturedRequest(),
            new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var provider = CreateProvider(handler);

        var result = await provider.ConfirmAsync(
            CreateTransactionActionRequest(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PortFailureType.Authentication, result.Failure.Type);
        Assert.Equal("ogloba.authentication_failed", result.Failure.Code);
    }

    [Fact]
    public async Task ServerError_MapsIndeterminateFailure()
    {
        var handler = CreateHandler(
            new CapturedRequest(),
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var provider = CreateProvider(handler);

        var result = await provider.RedeemAsync(CreateRedemptionRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PortFailureType.Indeterminate, result.Failure.Type);
        Assert.Equal("ogloba.server_error", result.Failure.Code);
    }

    [Fact]
    public async Task MalformedSuccessResponse_MapsIndeterminateFailure()
    {
        var handler = CreateHandler(
            new CapturedRequest(),
            JsonResponse("not-json"));
        var provider = CreateProvider(handler);

        var result = await provider.ConfirmAsync(
            CreateTransactionActionRequest(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PortFailureType.Indeterminate, result.Failure.Type);
        Assert.Equal("ogloba.malformed_response", result.Failure.Code);
    }

    [Fact]
    public async Task TransportCancellationWithoutCallerCancellation_MapsTimeout()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new OperationCanceledException());
        var provider = CreateProvider(handler);

        var result = await provider.RedeemAsync(CreateRedemptionRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PortFailureType.Timeout, result.Failure.Type);
        Assert.Equal("ogloba.timeout", result.Failure.Code);
    }

    [Fact]
    public async Task NetworkException_MapsIndeterminateFailure()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new HttpRequestException("Network unavailable."));
        var provider = CreateProvider(handler);

        var result = await provider.RedeemAsync(CreateRedemptionRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PortFailureType.Indeterminate, result.Failure.Type);
        Assert.Equal("ogloba.network_error", result.Failure.Code);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagatedWithoutSendingHttpRequest()
    {
        var handler = CreateHandler(
            new CapturedRequest(),
            JsonResponse("""{"isSuccessful":true}"""));
        var provider = CreateProvider(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.RedeemAsync(CreateRedemptionRequest(), cancellation.Token));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CredentialFailure_StopsBeforeSendingHttpRequest()
    {
        var handler = CreateHandler(
            new CapturedRequest(),
            JsonResponse("""{"isSuccessful":true}"""));
        var credentials = new StubOglobaCredentialProvider(new PortFailure(
            "credentials.unavailable",
            "Store credentials are unavailable.",
            PortFailureType.Authentication));
        var provider = CreateProvider(handler, credentials);

        var result = await provider.RedeemAsync(CreateRedemptionRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("credentials.unavailable", result.Failure.Code);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void Options_RejectNonHttpsBaseAddress()
    {
        Assert.Throws<ArgumentException>(() =>
            new OglobaOptions(new Uri("http://ogloba.test/giftCardService")));
    }

    [Fact]
    public void Credential_ToStringNeverExposesPassword()
    {
        var credential = new OglobaStoreCredential("test-password");

        Assert.Equal("[REDACTED]", credential.ToString());
        Assert.DoesNotContain("test-password", credential.ToString(), StringComparison.Ordinal);
    }

    private static OglobaGiftCardProvider CreateProvider(
        StubHttpMessageHandler handler,
        StubOglobaCredentialProvider? credentials = null)
    {
        var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var options = new OglobaOptions(
            new Uri("https://ogloba.test/giftCardService"),
            requestTimeout: TimeSpan.FromSeconds(5));

        return new OglobaGiftCardProvider(
            httpClient,
            options,
            credentials ?? new StubOglobaCredentialProvider("test-password"));
    }

    private static StubHttpMessageHandler CreateHandler(
        CapturedRequest captured,
        HttpResponseMessage response) =>
        new(async (request, cancellationToken) =>
        {
            captured.Method = request.Method;
            captured.Uri = request.RequestUri;
            captured.ApiVersion = request.Headers.GetValues("X-WSRG-API-Version").Single();
            captured.AuthorizationScheme = request.Headers.Authorization?.Scheme;
            captured.AuthorizationParameter = request.Headers.Authorization?.Parameter;
            captured.Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        });

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static RedemptionRequest CreateRedemptionRequest() =>
        new(
            CreateTransactionNumber(),
            CreateStoreId(),
            CreateTerminalId(),
            CreateCashierId(),
            CardIdentifier.CreatePhysicalCard("1138170025515937").Value,
            Money.Create(50_000).Value);

    private static TransactionActionRequest CreateTransactionActionRequest() =>
        new(
            CreateTransactionNumber(),
            CreateReferenceNumber(),
            CreateStoreId(),
            CreateTerminalId(),
            CreateCashierId(),
            Money.Create(50_000).Value,
            OriginalMerchantId: "K00036",
            OriginalTerminalId: "caja-5",
            OriginalCashierId: "operador-123");

    private static TransactionNumber CreateTransactionNumber() =>
        TransactionNumber.Create("1756113296").Value;

    private static ReferenceNumber CreateReferenceNumber() =>
        ReferenceNumber.Create("00136544716V").Value;

    private static StoreId CreateStoreId() => StoreId.Create("K00036").Value;

    private static TerminalId CreateTerminalId() => TerminalId.Create("caja-5").Value;

    private static CashierId CreateCashierId() => CashierId.Create("operador-123").Value;

    private static string DecodeBasicCredential(string? parameter)
    {
        Assert.False(string.IsNullOrWhiteSpace(parameter));
        return Encoding.UTF8.GetString(Convert.FromBase64String(parameter));
    }

    private sealed class CapturedRequest
    {
        public HttpMethod? Method { get; set; }

        public Uri? Uri { get; set; }

        public string? ApiVersion { get; set; }

        public string? AuthorizationScheme { get; set; }

        public string? AuthorizationParameter { get; set; }

        public string? Body { get; set; }
    }
}
