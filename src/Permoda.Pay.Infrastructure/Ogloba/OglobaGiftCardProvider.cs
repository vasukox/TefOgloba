using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Permoda.Pay.Application;
using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Logging;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;
using Permoda.Pay.Infrastructure.Ogloba.Authentication;
using Permoda.Pay.Infrastructure.Ogloba.Contracts;
using Permoda.Pay.Infrastructure.Ogloba.Errors;

namespace Permoda.Pay.Infrastructure.Ogloba;

public sealed class OglobaGiftCardProvider : IGiftCardProvider
{
    private const string ApiVersionHeader = "X-WSRG-API-Version";

    // Confirmado en vivo (2026-07-28): Ogloba localiza errorMessage cuando se manda este header
    // (ej. "Wrong activation amount" -> "Monto de activación no válido"). Con esto el cajero ve
    // los mensajes de Ogloba en español directamente, sin mantener una tabla de 80+ códigos.
    private const string AcceptLanguageHeader = "es-co";
    private const string ActivationPath = "activation";
    private const string RedemptionPath = "redemption";
    private const string ReloadPath = "reload";
    private const string ConfirmationPath = "confirmTransaction";
    private const string ReversalPath = "reversal";

    // docs/OGLOBA_API_REFERENCE.md §3.8. "voidTransaction", NO "void": el nombre corto viene de
    // nuestra doc interna vieja y el Postman oficial de Ogloba lo desmiente.
    private const string VoidPath = "voidTransaction";
    private const string ReconciliationPath = "reconciliation";
    private const string BalancePath = "balance";
    private const string TestPath = "test";
    private const string BusinessUnitInfoPath = "getBuInfo";
    private const string OrderCreationPath = "orderCreation";
    private const string OrderConfirmPath = "orderConfirm";
    private const string OrderCancelPath = "orderCancel";
    private const string OrderStatusPath = "orderStatus";
    private const string OrderReturnPath = "orderReturn";
    private const string GetProductsPath = "getProducts";

    // docs/OGLOBA_API_REFERENCE.md §3.9: "MA" = Batch card activation (el otro valor,
    // "WL", es para recargar wallets de miembros — no aplica a un bono nuevo).
    private const string BatchCardActivationSalesType = "MA";

    // §3.9: deliverType "3" = eMail. Es el único valor con el que Ogloba envía el bono
    // automáticamente al correo del cliente sin pasar por SMS/PDF/API.
    // Los otros: 0 = por API (lo envía el comercio), 1 = SMS, 2 = PDF, 4 = SMS + eMail.
    private const string EmailDeliverType = "3";

    // §3.10: paymentType por defecto del contrato.
    private const string DefaultOrderPaymentType = "01";

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly OglobaOptions _options;
    private readonly IOglobaCredentialProvider _credentials;
    private readonly IOglobaTrafficLog? _trafficLog;

    public OglobaGiftCardProvider(
        HttpClient httpClient,
        OglobaOptions options,
        IOglobaCredentialProvider credentials,
        IOglobaTrafficLog? trafficLog = null)
    {
        _httpClient = httpClient;
        _options = options;
        _credentials = credentials;
        // null en tests (no hay host de runtime). En MAUI lo inyecta MauiProgram.
        _trafficLog = trafficLog;
    }

    // Diagnóstico: las llamadas a Ogloba se loguean para auditoría y debug.
    // En el host MAUI, `_logSink` se inyecta desde MauiProgram y enruta a ILogger
    // (visible en logcat bajo el tag "TefOgloba"). En tests, es null y caemos a
    // System.Diagnostics.Debug que sigue funcionando.
    private const string LogTag = "TefOgloba.Ogloba";

    private static Action<string>? _logSink;

    public static void ConfigureLogging(Action<string> sink) => _logSink = sink;

    private static void Log(string message)
    {
        var line = $"[{LogTag}] {message}";
        if (_logSink is not null)
        {
            _logSink(line);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(line);
        }
    }

    public Task<PortResult<RedemptionAuthorization>> RedeemAsync(
        RedemptionRequest request,
        CancellationToken cancellationToken) =>
        AuthorizeAsync(RedemptionPath, request, cancellationToken);

    public Task<PortResult<RedemptionAuthorization>> ActivateAsync(
        RedemptionRequest request,
        CancellationToken cancellationToken) =>
        AuthorizeAsync(ActivationPath, request, cancellationToken);

    public Task<PortResult<RedemptionAuthorization>> ReloadAsync(
        RedemptionRequest request,
        CancellationToken cancellationToken) =>
        AuthorizeAsync(ReloadPath, request, cancellationToken);

    private async Task<PortResult<RedemptionAuthorization>> AuthorizeAsync(
        string relativePath,
        RedemptionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Ogloba espera 'gencode' para bonos digitales y 'cardNumber' para físicos.
        var isDigital = request.CardIdentifier.Kind == CardIdentifierKind.DigitalGencode;

        // CONFIRMADO en vivo contra sandbox (2026-07-28, store K00036, gencode 113815):
        // `amount` va en PESOS tal cual, NO en centavos. Un intento previo multiplicaba por 100
        // (comentario decía que el doc lo exigía — el doc no dice tal cosa en ninguna parte) y
        // Ogloba rechazaba con errorCode 73 "Wrong activation amount"; con el valor sin multiplicar
        // la misma tienda/gencode respondió isSuccessful=true. No reintroducir el x100.
        // PAN enmascarado (113817******5937) en logs: nunca el serial completo en logcat.
        Log($"Ogloba {relativePath} request: merchantId={request.StoreId.Value}, terminalId={request.TerminalId.Value}, amount={request.Amount.MinorUnits}pesos, isDigital={isDigital}, cardId={request.CardIdentifier.MaskedValue}");

        var responseResult = await PostAsync(
            relativePath,
            new OglobaRedemptionRequest(
                request.StoreId.Value,
                request.TerminalId.Value,
                request.CashierId.Value,
                request.TransactionNumber.Value,
                request.Amount.MinorUnits,
                request.Amount.Currency,
                // La doc de Ogloba envía cardNumber="" (presente pero vacío) para bonos
                // digitales, junto al gencode. Para físicos va el PAN en cardNumber.
                CardNumber: isDigital ? string.Empty : request.CardIdentifier.Value,
                Gencode: isDigital ? request.CardIdentifier.Value : null,
                PinCode: request.PinCode,
                Note: request.Note,
                Track2Data: request.Track2Data,

                // Solo en activación digital: es la dirección a la que Ogloba envía el bono
                // cuando se confirma. Va junto al gencode; para una tarjeta física no aplica y
                // mandarlo sería pedirle a Ogloba que envíe algo que el cliente ya tiene en la
                // mano.
                Email: isDigital ? NullIfBlank(request.Email) : null),
            request.StoreId,
            cancellationToken);

        if (responseResult.IsFailure)
        {
            return PortResult<RedemptionAuthorization>.Failed(responseResult.Failure);
        }

        var response = responseResult.Value;

        if (string.IsNullOrWhiteSpace(response.ReferenceNumber) ||
            response.Balance is null ||
            string.IsNullOrWhiteSpace(response.Currency))
        {
            return PortResult<RedemptionAuthorization>.Failed(
                OglobaFailureMapper.MalformedResponse());
        }

        var referenceNumberResult = ReferenceNumber.Create(response.ReferenceNumber);
        var balanceResult = Money.Create(response.Balance.Value, response.Currency);

        if (referenceNumberResult.IsFailure || balanceResult.IsFailure)
        {
            return PortResult<RedemptionAuthorization>.Failed(
                OglobaFailureMapper.MalformedResponse());
        }

        return PortResult<RedemptionAuthorization>.Success(new RedemptionAuthorization(
            referenceNumberResult.Value,
            balanceResult.Value,
            response.CardNumber,
            response.EGiftCardUrl,
            response.Gencode,
            response.ToBeChargedAmt,
            MapRequestedCurrency(response.RequestedCurrency),
            MapRedeemShareDet(response.RedeemShareDet)));
    }

    private static RequestedCurrencyInfo? MapRequestedCurrency(OglobaRequestedCurrency? source) =>
        source is null
            || string.IsNullOrWhiteSpace(source.Currency)
            || !source.ExchangeRate.HasValue
            || !source.Balance.HasValue
            || !source.PreviousBalance.HasValue
            || !source.InitialBalance.HasValue
            ? null
            : new RequestedCurrencyInfo(
                source.Currency,
                source.ExchangeRate.Value,
                source.Balance.Value,
                source.PreviousBalance.Value,
                source.InitialBalance.Value);

    private static IReadOnlyList<RedeemShareDetail>? MapRedeemShareDet(
        IReadOnlyList<OglobaRedeemShareDetail>? source)
    {
        if (source is null || source.Count == 0)
        {
            return null;
        }

        var mapped = new List<RedeemShareDetail>(source.Count);
        foreach (var item in source)
        {
            if (!item.SharedAmt.HasValue || string.IsNullOrWhiteSpace(item.ReloadSource))
            {
                continue;
            }

            mapped.Add(new RedeemShareDetail(item.SharedAmt.Value, item.ReloadSource));
        }

        return mapped.Count == 0 ? null : mapped;
    }

    public async Task<PortResult<Unit>> ConfirmAsync(
        TransactionActionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await PostAsync(
            ConfirmationPath,
            CreateConfirmCancelPayload(request),
            request.StoreId,
            cancellationToken);

        return ToUnitResult(response);
    }

    public async Task<PortResult<Unit>> ReverseAsync(
        ReversalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await PostAsync(
            ReversalPath,
            new OglobaReversalRequest(
                request.StoreId.Value,
                request.TerminalId.Value,
                request.CashierId.Value,
                request.TransactionNumber.Value,
                request.ReferenceNumber?.Value,
                request.OriginalMerchantId,
                request.OriginalTerminalId,
                request.OriginalCashierId,
                request.OriginalTransactionNumber),
            request.StoreId,
            cancellationToken);
        return ToUnitResult(response);
    }

    /// <summary>
    /// POST /voidTransaction — anula una operación confirmada y le devuelve el saldo al bono.
    /// <para>
    /// Va con <see cref="OglobaOptions.RequestTimeout"/> (el largo, no el de consultas): mueve
    /// dinero, y cortar por timeout una anulación deja al cliente sin saldo y sin venta.
    /// </para>
    /// </summary>
    public async Task<PortResult<Unit>> VoidAsync(
        VoidTransactionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Log($"Ogloba {VoidPath} request: merchantId={request.StoreId.Value}, "
            + $"terminalId={request.TerminalId.Value}, reference={request.ReferenceNumber.Value}");

        var response = await PostAsync(
            VoidPath,
            new OglobaVoidRequest(
                request.StoreId.Value,
                request.TerminalId.Value,
                request.CashierId.Value,
                request.OriginalMerchantId,
                request.OriginalTerminalId,
                request.OriginalCashierId,
                request.ReferenceNumber.Value,
                request.TransactionNumber?.Value,
                request.Note,
                request.Reason,
                // Formato exacto del contrato (§3.8): YYYY-MM-DD hh24:mi:ss. Hora local de la
                // terminal, que es la que Ogloba coteja contra el plazo de anulación.
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture)),
            request.StoreId,
            cancellationToken);

        return ToUnitResult(response);
    }

    public async Task<PortResult<Unit>> ReconcileAsync(
        ReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Postman oficial ("Ogloba - GiftCard integration", Step 3): reconciliamos una
        // transacción a la vez (terminalTxNo/lineCount fijos en "1"/1), businessDate = hoy
        // en la tienda (formato libre según ejemplo del Postman: usamos yyyyMMdd, el más común
        // en el resto del contrato Ogloba).
        var response = await PostAsync(
            ReconciliationPath,
            new OglobaReconciliationRequest(
                request.StoreId.Value,
                DateTime.UtcNow.ToString("yyyyMMdd"),
                [
                    new OglobaReconciliationDetail(
                        TerminalTxNo: "1",
                        LineCount: 1,
                        request.TerminalId.Value,
                        request.CashierId.Value,
                        request.TransactionNumber.Value,
                        request.ReferenceNumber.Value,
                        ToTransactionType(request.Operation),
                        request.Amount.Currency,
                        // Pesos tal cual — mismo formato confirmado en vivo para /activation,
                        // /redemption y /orderCreation (ver nota en AuthorizeAsync).
                        request.Amount.MinorUnits,
                        ToFinalStatus(request.FinalStatus))
                ]),
            request.StoreId,
            cancellationToken);

        return ToUnitResult(response);
    }

    public async Task<PortResult<GiftCardOrderCreated>> CreateOrderAsync(
        CreateGiftCardOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Mismo formato de `amount` que /activation y /redemption: pesos tal cual, sin
        // multiplicar (ver nota en AuthorizeAsync — confirmado en vivo contra sandbox).
        Log($"Ogloba orderCreation request: merchantId={request.StoreId.Value}, itemCode={request.ItemCode}, faceAmount={request.FaceAmount.MinorUnits}pesos, receiverEmail={request.ReceiverEmail}");

        // Cédula + nombre del cliente concatenados: /orderCreation no tiene un campo dedicado
        // para datos del destinatario que Ogloba persista de forma estructurada en el bono,
        // así que el "message" del item carga el dato del cliente como texto libre. Vacío si
        // el cajero no capturó cliente.
        // La identificación del cliente va en receiverMobileNo, NO en message.
        //
        // /orderCreation no tiene campo `note` —el que usa la activación física— así que en
        // virtual hace falta otro. Se probó con `message` y el dato no apareció en el reporte de
        // Ogloba (activación MA2609000004 del 2026-09-02: columna Note vacía). `receiverMobileNo`
        // sí sale, bajo la columna "Phone No.".
        //
        // Y `message` era además el campo equivocado: es el mensaje del regalo, y termina a la
        // vista del cliente en el correo que le manda Ogloba. Se deja para lo que es.
        //
        // Tope de 60 caracteres por contrato (§3.9); el builder admite hasta 100.
        var customerNote = Truncate(
            CustomerMobileNoBuilder.Build(request.CustomerDocumentNumber, request.CustomerName),
            OrderReceiverMobileNoMaxLength);

        var responseResult = await PostAsync<OglobaOrderCreationRequest, OglobaOrderCreationResponse>(
            OrderCreationPath,
            new OglobaOrderCreationRequest(
                request.StoreId.Value,
                request.TerminalId.Value,
                request.CashierId.Value,
                request.ClientOrderNumber,
                BatchCardActivationSalesType,
                [
                    new OglobaOrderItem(
                        request.ItemCode,
                        request.FaceAmount.MinorUnits,
                        Quantity: 1,
                        EmailDeliverType,
                        request.ReceiverEmail,
                        Message: NullIfBlank(request.Message),
                        SenderName: NullIfBlank(request.SenderName),
                        ReceiverMobileNo: NullIfBlank(customerNote))
                ]),
            request.StoreId,
            static response => response.IsSuccessful,
            static response => OglobaFailureMapper.FromRejectedResponse(response.GetErrorCode(), response.ErrorMessage),
            cancellationToken);

        if (responseResult.IsFailure)
        {
            return PortResult<GiftCardOrderCreated>.Failed(responseResult.Failure);
        }

        var response = responseResult.Value;

        if (string.IsNullOrWhiteSpace(response.OrderNo))
        {
            return PortResult<GiftCardOrderCreated>.Failed(OglobaFailureMapper.MalformedResponse());
        }

        var orderAmountResult = Money.Create(response.OrderAmount ?? request.FaceAmount.MinorUnits, request.FaceAmount.Currency);

        return PortResult<GiftCardOrderCreated>.Success(new GiftCardOrderCreated(
            response.OrderNo,
            orderAmountResult.IsSuccess ? orderAmountResult.Value : request.FaceAmount));
    }

    public async Task<PortResult<GiftCardOrderConfirmation>> ConfirmOrderAsync(
        ConfirmGiftCardOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var responseResult = await PostAsync<OglobaOrderConfirmRequest, OglobaOrderConfirmResponse>(
            OrderConfirmPath,
            new OglobaOrderConfirmRequest(
                request.StoreId.Value,
                request.TerminalId.Value,
                request.CashierId.Value,
                request.OrderNumber,
                ShippingFee: 0,
                CardFee: 0,
                ReturnFee: 0,
                // El pedido se cobra en la caja de HiPOS, no en Ogloba: declaramos un único pago
                // del tipo por defecto ("01") con un id único derivado del número de pedido, que
                // es la forma en que Ogloba lo espera. Sin fees porque el bono se vende a su
                // valor facial.
                PaymentList:
                [
                    new OglobaOrderPayment(DefaultOrderPaymentType, $"X-{request.OrderNumber}")
                ]),
            request.StoreId,
            static response => response.IsSuccessful,
            static response => OglobaFailureMapper.FromRejectedResponse(response.GetErrorCode(), response.ErrorMessage),
            cancellationToken);

        if (responseResult.IsFailure)
        {
            return PortResult<GiftCardOrderConfirmation>.Failed(responseResult.Failure);
        }

        var response = responseResult.Value;

        if (string.IsNullOrWhiteSpace(response.OrderStatus))
        {
            return PortResult<GiftCardOrderConfirmation>.Failed(OglobaFailureMapper.MalformedResponse());
        }

        return PortResult<GiftCardOrderConfirmation>.Success(
            ToOrderConfirmation(response, request.OrderNumber));
    }

    public async Task<PortResult<Unit>> CancelOrderAsync(
        ConfirmGiftCardOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await PostAsync(
            OrderCancelPath,
            new OglobaOrderRequest(
                request.StoreId.Value,
                request.TerminalId.Value,
                request.CashierId.Value,
                request.OrderNumber),
            request.StoreId,
            cancellationToken);

        return ToUnitResult(response);
    }

    public async Task<PortResult<GiftCardOrderConfirmation>> GetOrderStatusAsync(
        ConfirmGiftCardOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // docs/OGLOBA_API_REFERENCE.md §3.12: la respuesta trae más campos por tarjeta
        // (email, deliveryTracking, cardPdfUrl, etc.) que /orderConfirm, pero reutilizamos
        // OglobaOrderConfirmResponse/IssuedGiftCard — System.Text.Json ignora los campos extra
        // que no modelamos y hoy ningún consumidor los necesita.
        var responseResult = await PostAsync<OglobaOrderRequest, OglobaOrderConfirmResponse>(
            OrderStatusPath,
            new OglobaOrderRequest(
                request.StoreId.Value,
                request.TerminalId.Value,
                request.CashierId.Value,
                request.OrderNumber),
            request.StoreId,
            static response => response.IsSuccessful,
            static response => OglobaFailureMapper.FromRejectedResponse(response.GetErrorCode(), response.ErrorMessage),
            cancellationToken);

        if (responseResult.IsFailure)
        {
            return PortResult<GiftCardOrderConfirmation>.Failed(responseResult.Failure);
        }

        var response = responseResult.Value;

        if (string.IsNullOrWhiteSpace(response.OrderStatus))
        {
            return PortResult<GiftCardOrderConfirmation>.Failed(OglobaFailureMapper.MalformedResponse());
        }

        return PortResult<GiftCardOrderConfirmation>.Success(
            ToOrderConfirmation(response, request.OrderNumber));
    }

    public async Task<PortResult<OrderReturnResult>> ReturnOrderAsync(
        ReturnGiftCardOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // docs/OGLOBA_API_REFERENCE.md §3.13: partialReturnDetails requerido si returnType='P'.
        // Si es 'F' mandamos array vacío (Ogloba lo acepta).
        List<OglobaPartialReturnDetail> details;
        if (request.PartialReturnDetails is { Count: > 0 } prd)
        {
            details = prd
                .Select(d => new OglobaPartialReturnDetail(
                    d.CardNumberBegin,
                    d.CardNumberEnd,
                    d.FaceValueMinorUnits,
                    d.ItemCode,
                    d.Quantity))
                .ToList();
        }
        else
        {
            details = new List<OglobaPartialReturnDetail>();
        }

        var responseResult = await PostAsync<OglobaOrderReturnRequest, OglobaOrderReturnResponse>(
            OrderReturnPath,
            new OglobaOrderReturnRequest(
                request.StoreId.Value,
                request.TerminalId.Value,
                request.CashierId.Value,
                request.OrderNumber,
                request.ReturnType,
                request.ReturnFee,
                request.CardFee,
                request.ShippingFee,
                details),
            request.StoreId,
            static response => response.IsSuccessful,
            static response => OglobaFailureMapper.FromRejectedResponse(OglobaErrorCode.Read(response.ErrorCode), response.ErrorMessage),
            cancellationToken);

        if (responseResult.IsFailure)
        {
            return PortResult<OrderReturnResult>.Failed(responseResult.Failure);
        }

        var response = responseResult.Value;
        if (string.IsNullOrWhiteSpace(response.ReturnNo) || string.IsNullOrWhiteSpace(response.Status))
        {
            return PortResult<OrderReturnResult>.Failed(OglobaFailureMapper.MalformedResponse());
        }

        return PortResult<OrderReturnResult>.Success(
            new OrderReturnResult(request.OrderNumber, response.ReturnNo, response.Status));
    }

    public async Task<PortResult<IReadOnlyList<GiftCardProduct>>> GetProductsAsync(
        StoreId storeId,
        string? itemCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storeId);

        var responseResult = await PostAsync<OglobaGetProductsRequest, OglobaGetProductsResponse>(
            GetProductsPath,
            new OglobaGetProductsRequest(storeId.Value, itemCode),
            storeId,
            static response => response.IsSuccessful,
            static response => OglobaFailureMapper.FromRejectedResponse(OglobaErrorCode.Read(response.ErrorCode), response.ErrorMessage),
            cancellationToken);

        if (responseResult.IsFailure)
        {
            return PortResult<IReadOnlyList<GiftCardProduct>>.Failed(responseResult.Failure);
        }

        var products = (responseResult.Value.ProductList ?? [])
            .Select(p => new GiftCardProduct(
                ItemCode: p.ItemCode ?? string.Empty,
                ProductName: p.ProductName ?? string.Empty,
                Currency: p.Currency ?? "COP",
                IsFixValue: p.IsFixValue == 1,
                FaceValueMinorUnits: p.FaceValue,
                ActivationMinAmtMinorUnits: p.ActivationMinAmt,
                ActivationMaxAmtMinorUnits: p.ActivationMaxAmt,
                AllowedActivate: p.AllowedActivate == 1,
                AllowedReload: p.AllowedReload == 1,
                AllowedRedeem: p.AllowedRedeem == 1,
                IsRefundable: p.IsRefundable == 1,
                ProductType: p.ProductType ?? string.Empty,
                CardValidityMonths: p.CardValidity ?? 0))
            .ToList();

        return PortResult<IReadOnlyList<GiftCardProduct>>.Success(products);
    }

    private static GiftCardOrderConfirmation ToOrderConfirmation(
        OglobaOrderConfirmResponse response,
        string fallbackOrderNumber)
    {
        var cards = (response.ListOfCards ?? [])
            .Select(card => new IssuedGiftCard(
                card.CardNumber,
                card.Gencode,
                card.PinCode1,
                card.ExpiryDate,
                card.CardBalance,
                card.ShortCardNumber,
                card.EGiftCardUrl))
            .ToList();

        return new GiftCardOrderConfirmation(
            response.OrderNo ?? fallbackOrderNumber,
            response.OrderStatus!,
            cards);
    }

    public async Task<PortResult<IReadOnlyList<OglobaTransactionRecord>>> QueryTransactionsHistoryAsync(
        StoreId storeId,
        string? transDateFrom,
        string? transDateTo,
        int pageNo,
        int numberOfPage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storeId);

        // docs/OGLOBA_API_REFERENCE.md §3.15: solo `pageNo` y `numberOfPage` son mandatory
        // (defaults 1 y 10). Todos los demás filtros son opcionales ("at least one criteria").
        // Construimos el body solo con lo que el caller nos pasa.
        var responseResult = await PostAsync<OglobaQueryTransactionsHistoryRequest, OglobaQueryTransactionsHistoryResponse>(
            "queryTransactionsHistory",
            new OglobaQueryTransactionsHistoryRequest(
                MerchantId: storeId.Value,
                TerminalId: null,
                CashierId: null,
                TransDateFrom: transDateFrom,
                TransDateTo: transDateTo,
                CardNumber: null,
                TransType: null,
                TransactionStatus: null,
                ReconciliationStatus: null,
                OrderNo: null,
                ReferenceNumber: null,
                PageNo: pageNo,
                NumberOfPage: numberOfPage),
            storeId,
            static response => response.IsSuccessful,
            static response => OglobaFailureMapper.FromRejectedResponse(
                OglobaErrorCode.Read(response.ErrorCode),
                response.ErrorMessage),
            cancellationToken);

        if (responseResult.IsFailure)
        {
            return PortResult<IReadOnlyList<OglobaTransactionRecord>>.Failed(responseResult.Failure);
        }

        var response = responseResult.Value;
        // La doc muestra el array como `transactionRecord` (singular) y `transactionRecords`
        // (plural) — distintos endpoints. /queryTransactionsHistory usa `transactionRecord`
        // según el ejemplo en §3.15; leemos ambos por si Ogloba cambia la convención.
        var records = response.TransactionRecord ?? response.TransactionRecords ?? [];

        var mapped = records
            .Select(r => new OglobaTransactionRecord(
                MerchantId: r.MerchantId ?? string.Empty,
                TerminalId: r.TerminalId ?? string.Empty,
                CashierId: r.CashierId ?? string.Empty,
                TransactionTime: r.TransactionTime ?? string.Empty,
                TransactionType: r.TransactionType ?? string.Empty,
                TransactionNumber: r.TransactionNumber ?? string.Empty,
                ReferenceNumber: r.ReferenceNumber ?? string.Empty,
                PreviousBalanceMinorUnits: r.PreviousBalance,
                Currency: r.Currency ?? "COP",
                TransactionAmountMinorUnits: r.TransactionAmount,
                BalanceMinorUnits: r.Balance,
                CardNumber: r.CardNumber ?? string.Empty,
                Gencode: r.Gencode,
                TransactionStatus: r.TransactionStatus ?? string.Empty,
                ReconciliationResult: r.ReconciliationResult ?? string.Empty,
                ExpireDate: r.ExpireDate))
            .ToList();

        return PortResult<IReadOnlyList<OglobaTransactionRecord>>.Success(mapped);
    }

    public async Task<PortResult<CardBalance>> GetBalanceAsync(
        BalanceQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isDigital = request.CardIdentifier.Kind == CardIdentifierKind.DigitalGencode;

        var responseResult = await PostAsync(
            BalancePath,
            new OglobaBalanceRequest(
                request.StoreId.Value,
                request.TerminalId.Value,
                request.CashierId.Value,
                request.TransactionNumber.Value,
                CardNumber: isDigital ? string.Empty : request.CardIdentifier.Value,
                Gencode: isDigital ? request.CardIdentifier.Value : null,
                PinCode: request.PinCode),
            request.StoreId,
            cancellationToken,
            // Consulta: no mueve plata. Con el tope de 90 s, un bono escaneado contra un Ogloba
            // lento dejaba la caja detenida minuto y medio sin haber cobrado nada.
            _options.QueryTimeout);

        if (responseResult.IsFailure)
        {
            return PortResult<CardBalance>.Failed(responseResult.Failure);
        }

        var response = responseResult.Value;

        if (response.Balance is null || string.IsNullOrWhiteSpace(response.Currency))
        {
            return PortResult<CardBalance>.Failed(OglobaFailureMapper.MalformedResponse());
        }

        return PortResult<CardBalance>.Success(new CardBalance(
            response.Balance.Value,
            response.Currency,
            response.CardStatus ?? "UNKNOWN",
            response.ExpireDate));
    }

    /// <summary>
    /// Comprueba que la terminal alcanza a Ogloba y que su credencial sirve.
    /// <para>
    /// Directo contra Ogloba se usa <c>GET /test</c>, que existe para esto. El API Management de
    /// Permoda <b>no lo publica</b>, así que ahí se consulta el saldo de una tarjeta que no
    /// existe: la petición recorre exactamente el mismo camino —pasarela, autenticación, Ogloba—
    /// y vuelve con el código de negocio 52 ("la tarjeta no existe").
    /// </para>
    /// <para>
    /// Ese rechazo <b>es</b> la prueba: solo puede producirlo Ogloba después de haber recibido y
    /// entendido la petición. Es el mismo método con el que se certificó la conexión de la tienda
    /// 037 el 2026-09-09. Y no mueve nada: consultar el saldo de una tarjeta inexistente no
    /// escribe en ningún lado.
    /// </para>
    /// </summary>
    public async Task<PortResult<Unit>> CheckConnectivityAsync(
        StoreId storeId,
        CancellationToken cancellationToken)
    {
        if (_options.UsesApiManagement)
        {
            return await CheckConnectivityThroughBalanceAsync(storeId, cancellationToken);
        }

        var responseResult = await GetAsync(TestPath, storeId, cancellationToken);

        if (responseResult.IsFailure)
        {
            return PortResult<Unit>.Failed(responseResult.Failure);
        }

        try
        {
            var body = JsonSerializer.Deserialize<OglobaResponse>(responseResult.Value, SerializerOptions);
            return body is { IsSuccessful: true }
                ? PortResult<Unit>.Success(Unit.Value)
                : PortResult<Unit>.Failed(OglobaFailureMapper.MalformedResponse());
        }
        catch (JsonException)
        {
            return PortResult<Unit>.Failed(OglobaFailureMapper.MalformedResponse());
        }
    }

    public async Task<PortResult<BusinessUnitInfo>> GetBusinessUnitInfoAsync(
        StoreId storeId,
        CancellationToken cancellationToken)
    {
        var responseResult = await GetAsync(BusinessUnitInfoPath, storeId, cancellationToken);

        if (responseResult.IsFailure)
        {
            return PortResult<BusinessUnitInfo>.Failed(responseResult.Failure);
        }

        try
        {
            using var document = JsonDocument.Parse(responseResult.Value);
            var root = document.RootElement;

            var name = ReadFirstString(root, "name", "businessName", "buName", "merchantName", "description")
                ?? string.Empty;
            long? balance = ReadFirstLong(root, "balance", "availableBalance", "net");

            return PortResult<BusinessUnitInfo>.Success(new BusinessUnitInfo(name, balance));
        }
        catch (JsonException)
        {
            return PortResult<BusinessUnitInfo>.Failed(OglobaFailureMapper.MalformedResponse());
        }
    }

    private static string? ReadFirstString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static long? ReadFirstLong(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt64(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private async Task<PortResult<string>> GetAsync(
        string relativePath,
        StoreId storeId,
        CancellationToken cancellationToken)
    {
        var credentialResult = await _credentials.GetAsync(storeId, cancellationToken);

        if (credentialResult.IsFailure)
        {
            return PortResult<string>.Failed(credentialResult.Failure);
        }

        if (!_options.TryResolvePath(relativePath, out var resolvedPath))
        {
            return PortResult<string>.Failed(
                OglobaFailureMapper.OperationNotPublished(relativePath));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(_options.BaseAddress, resolvedPath!));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Todos los GET del proveedor son de solo lectura (conectividad, productos, saldo del
        // comercio): ninguno mueve plata, así que van con el tope corto.
        timeout.CancelAfter(_options.QueryTimeout);
        ApplyAuthentication(request, storeId, credentialResult.Value);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation(ApiVersionHeader, _options.ApiVersion);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(AcceptLanguageHeader));

        string responseBody = string.Empty;
        string? errorCode = null;

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                await RecordTrafficAsync(relativePath, storeId, string.Empty, string.Empty,
                    false, $"http_{((int)response.StatusCode)}", cancellationToken);
                return PortResult<string>.Failed(
                    OglobaFailureMapper.FromHttpStatus(response.StatusCode));
            }

            responseBody = await response.Content.ReadAsStringAsync(timeout.Token);

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                await RecordTrafficAsync(relativePath, storeId, string.Empty, string.Empty,
                    false, "ogloba.malformed_response", cancellationToken);
                return PortResult<string>.Failed(OglobaFailureMapper.MalformedResponse());
            }

            try
            {
                using var probe = JsonDocument.Parse(responseBody);
                if (probe.RootElement.TryGetProperty("errorCode", out var ec) &&
                    ec.ValueKind != JsonValueKind.Null)
                {
                    errorCode = ec.ValueKind == JsonValueKind.String ? ec.GetString() : ec.GetRawText();
                }
            }
            catch
            {
                // ignore
            }

            await RecordTrafficAsync(relativePath, storeId, string.Empty, responseBody,
                true, errorCode, cancellationToken);

            return PortResult<string>.Success(responseBody);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await RecordTrafficAsync(relativePath, storeId, string.Empty, string.Empty,
                false, "ogloba.timeout", cancellationToken);
            return PortResult<string>.Failed(OglobaFailureMapper.Timeout());
        }
        catch (HttpRequestException)
        {
            await RecordTrafficAsync(relativePath, storeId, string.Empty, string.Empty,
                false, "ogloba.network_error", cancellationToken);
            return PortResult<string>.Failed(OglobaFailureMapper.Network());
        }
    }

    private Task<PortResult<OglobaResponse>> PostAsync<TRequest>(
        string relativePath,
        TRequest payload,
        StoreId storeId,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null) =>
        PostAsync<TRequest, OglobaResponse>(
            relativePath,
            payload,
            storeId,
            static response => response.IsSuccessful,
            static response => OglobaFailureMapper.FromRejectedResponse(response),
            cancellationToken,
            timeout);

    private Task RecordTrafficAsync(
        string path,
        StoreId storeId,
        string requestBody,
        string responseBody,
        bool isSuccessful,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        if (_trafficLog is null)
        {
            return Task.CompletedTask;
        }

        // transactionNumber y referenceNumber se pueden extraer del body — pero solo si
        // el caller los pasa explícitamente. Sin ellos, la correlación se hace por
        // timestamp + path (suficiente para el XLSX UAT interno, que se pega por
        // request/response y se agrupa por escenario).
        return _trafficLog.RecordAsync(
            new OglobaTrafficEntry(
                DateTimeOffset.UtcNow,
                TransactionNumber: string.Empty,
                ReferenceNumber: null,
                Path: path,
                StoreId: storeId.Value,
                RequestBody: requestBody,
                ResponseBody: responseBody,
                IsSuccessful: isSuccessful,
                ErrorCode: errorCode),
            cancellationToken);
    }

    // Genérico sobre TResponse porque /orderCreation y /orderConfirm devuelven formas
    // distintas a OglobaResponse — isSuccessful/mapRejection quedan a cargo de cada llamador.
    /// <param name="timeout">
    /// Tope de esta llamada. Por defecto el de las operaciones que mueven plata (90 s). Las
    /// consultas de solo lectura pasan <c>_options.QueryTimeout</c>: no dejan nada a medias si se
    /// caen, así que no tiene sentido hacer esperar al cajero el margen completo.
    /// </param>
    private async Task<PortResult<TResponse>> PostAsync<TRequest, TResponse>(
        string relativePath,
        TRequest payload,
        StoreId storeId,
        Func<TResponse, bool> isSuccessful,
        Func<TResponse, PortFailure> mapRejection,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var credentialResult = await _credentials.GetAsync(storeId, cancellationToken);

        if (credentialResult.IsFailure)
        {
            return PortResult<TResponse>.Failed(credentialResult.Failure);
        }

        // Se resuelve ANTES de armar nada: si el ambiente no publica esta operación, sale por
        // acá como fallo con motivo. Lanzar una excepción en el hilo de un cobro dejaría al
        // cajero con la operación tumbada y sin nada que decirle al cliente.
        if (!_options.TryResolvePath(relativePath, out var resolvedPath))
        {
            return PortResult<TResponse>.Failed(
                OglobaFailureMapper.OperationNotPublished(relativePath));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_options.BaseAddress, resolvedPath!));
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutSource.CancelAfter(timeout ?? _options.RequestTimeout);
        ApplyAuthentication(request, storeId, credentialResult.Value);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation(ApiVersionHeader, _options.ApiVersion);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(AcceptLanguageHeader));

        // Serializamos UNA sola vez — se usa tanto para el body del request como para
        // el log de tráfico UAT. Importante: NO contiene password de Basic Auth (solo
        // viaja en el header).
        var requestBodyJson = JsonSerializer.Serialize(payload, SerializerOptions);

        try
        {
            request.Content = new StringContent(
                requestBodyJson,
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                await RecordTrafficAsync(relativePath, storeId, requestBodyJson, string.Empty,
                    false, $"http_{((int)response.StatusCode)}", cancellationToken);
                return PortResult<TResponse>.Failed(
                    OglobaFailureMapper.FromHttpStatus(response.StatusCode));
            }

            var content = await response.Content.ReadAsStringAsync(timeoutSource.Token);

            if (string.IsNullOrWhiteSpace(content))
            {
                await RecordTrafficAsync(relativePath, storeId, requestBodyJson, string.Empty,
                    false, "ogloba.malformed_response", cancellationToken);
                return PortResult<TResponse>.Failed(
                    OglobaFailureMapper.MalformedResponse());
            }

            var body = JsonSerializer.Deserialize<TResponse>(content, SerializerOptions);

            if (body is null)
            {
                await RecordTrafficAsync(relativePath, storeId, requestBodyJson, content,
                    false, "ogloba.malformed_response", cancellationToken);
                return PortResult<TResponse>.Failed(
                    OglobaFailureMapper.MalformedResponse());
            }

            var success = isSuccessful(body);
            var rejectionCode = success ? null : ExtractErrorCode(body);

            await RecordTrafficAsync(relativePath, storeId, requestBodyJson, content,
                success, rejectionCode, cancellationToken);

            return success
                ? PortResult<TResponse>.Success(body)
                : PortResult<TResponse>.Failed(mapRejection(body));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await RecordTrafficAsync(relativePath, storeId, requestBodyJson, string.Empty,
                false, "ogloba.timeout", cancellationToken);
            return PortResult<TResponse>.Failed(OglobaFailureMapper.Timeout());
        }
        catch (HttpRequestException)
        {
            await RecordTrafficAsync(relativePath, storeId, requestBodyJson, string.Empty,
                false, "ogloba.network_error", cancellationToken);
            return PortResult<TResponse>.Failed(OglobaFailureMapper.Network());
        }
        catch (JsonException)
        {
            await RecordTrafficAsync(relativePath, storeId, requestBodyJson, string.Empty,
                false, "ogloba.malformed_response", cancellationToken);
            return PortResult<TResponse>.Failed(
                OglobaFailureMapper.MalformedResponse());
        }
    }

    // Extrae el errorCode del body si la respuesta es una de las DTO conocidas con
    // `GetErrorCode()`. Devuelve null si no aplica o si el body no tiene ese accessor.
    private static string? ExtractErrorCode<TResponse>(TResponse body) =>
        body switch
        {
            OglobaResponse r => r.GetErrorCode(),
            OglobaOrderCreationResponse r => r.GetErrorCode(),
            OglobaOrderConfirmResponse r => r.GetErrorCode(),
            _ => null
        };

    // Postman oficial ("Ogloba - GiftCard integration", Step 2): confirmTransaction solo
    // lleva merchantId/terminalId/cashierId/referenceNumber.
    private static OglobaConfirmCancelRequest CreateConfirmCancelPayload(
        TransactionActionRequest request) =>
        new(
            request.StoreId.Value,
            request.TerminalId.Value,
            request.CashierId.Value,
            request.ReferenceNumber.Value);

    /// <summary>Header con el que el API Management de Permoda autentica cada llamada.</summary>
    private const string SubscriptionKeyHeader = "Ocp-Apim-Subscription-Key";

    /// <summary>
    /// Serial imposible con el que se sondea la conexión: dieciséis ceros. No corresponde a
    /// ninguna tarjeta emitida, así que la respuesta siempre es "no existe" y nunca se toca el
    /// saldo de nadie.
    /// </summary>
    private const string ConnectivityProbeCard = "0000000000000000";

    /// <summary>
    /// Sondeo de conexión por <c>/balance</c>, para el APIM (ver <see cref="CheckConnectivityAsync"/>).
    /// <para>
    /// Lo que se comprueba es que Ogloba <b>conteste</b>, no que la tarjeta exista. Un rechazo de
    /// negocio —"la tarjeta no existe"— prueba la cadena completa igual que un éxito; lo único
    /// que significa "no hay conexión" es un fallo de red o de autenticación.
    /// </para>
    /// </summary>
    private async Task<PortResult<Unit>> CheckConnectivityThroughBalanceAsync(
        StoreId storeId,
        CancellationToken cancellationToken)
    {
        var probe = new BalanceQuery(
            storeId,
            TerminalId.Create("CONEXION").Value,
            CashierId.Create("CONEXION").Value,
            TransactionNumber.Create(
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    .ToString(System.Globalization.CultureInfo.InvariantCulture)).Value,
            CardIdentifier.CreatePhysicalCard(ConnectivityProbeCard).Value);

        var result = await GetBalanceAsync(probe, cancellationToken);

        if (result.IsSuccess)
        {
            return PortResult<Unit>.Success(Unit.Value);
        }

        // Que Ogloba RECHACE la tarjeta es exactamente lo que se esperaba: contestó. Lo que no
        // prueba conexión es que falle la red, la autenticación de la pasarela o el formato.
        return result.Failure.Type == PortFailureType.Rejected
            ? PortResult<Unit>.Success(Unit.Value)
            : PortResult<Unit>.Failed(result.Failure);
    }

    /// <summary>
    /// Autentica la petición según el ambiente.
    /// <para>
    /// Directo contra Ogloba va Basic Auth con <c>StoreId:passphrase</c>. Por el API Management de
    /// Permoda va la <c>Ocp-Apim-Subscription-Key</c> de la tienda, y NO va Basic: el APIM es
    /// quien guarda la credencial real de Ogloba, y esa es justamente la ganancia — la terminal
    /// deja de custodiar una credencial de Ogloba y pasa a tener una llave que Permoda revoca
    /// desde Azure sin tocar las otras tiendas.
    /// </para>
    /// <para>
    /// El secreto se guarda en el mismo sitio en los dos casos —el llavero cifrado, por tienda—;
    /// lo único que cambia es el header por el que viaja.
    /// </para>
    /// </summary>
    private void ApplyAuthentication(
        HttpRequestMessage request,
        StoreId storeId,
        OglobaStoreCredential credential)
    {
        if (_options.UsesApiManagement)
        {
            request.Headers.TryAddWithoutValidation(SubscriptionKeyHeader, credential.Password);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes($"{storeId.Value}:{credential.Password}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(bytes));
    }

    private static PortResult<Unit> ToUnitResult(PortResult<OglobaResponse> response) =>
        response.IsSuccess
            ? PortResult<Unit>.Success(Unit.Value)
            : PortResult<Unit>.Failed(response.Failure);

    private static string ToTransactionType(GiftCardOperation operation) => operation switch
    {
        GiftCardOperation.Activation => "A",
        GiftCardOperation.Redemption => "P",
        GiftCardOperation.Reload => "L",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
    };

    private static string ToFinalStatus(ReconciliationFinalStatus finalStatus) => finalStatus switch
    {
        ReconciliationFinalStatus.Successful => "Y",
        ReconciliationFinalStatus.Failed => "N",
        _ => throw new ArgumentOutOfRangeException(nameof(finalStatus), finalStatus, null)
    };

    /// <summary>
    /// Combina el mensaje libre de la activación con la cédula y el nombre del cliente
    /// concatenados (<c>CC-NombreApellido</c>). Si solo uno trae contenido, devuelve ese.
    /// Si ninguno trae contenido, devuelve <c>null</c>.
    /// </summary>
    /// <summary>Tope de <c>receiverMobileNo</c> en /orderCreation (docs §3.9).</summary>
    private const int OrderReceiverMobileNoMaxLength = 60;

    /// <summary>
    /// Recorta al tope del contrato. Ogloba rechaza el pedido entero si un campo se pasa de
    /// largo, y perder los últimos caracteres de la identificación del cliente es mucho menos
    /// grave que no poder emitir el bono.
    /// </summary>
    private static string? Truncate(string? value, int maxLength)
    {
        var text = NullIfBlank(value);

        return text is null || text.Length <= maxLength
            ? text
            : text[..maxLength];
    }
}

