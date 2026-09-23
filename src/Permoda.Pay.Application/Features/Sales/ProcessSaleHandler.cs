using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Logging;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Application.Abstractions.Persistence;
using Permoda.Pay.Application.Abstractions.Time;
using Permoda.Pay.Application.Errors;
using Permoda.Pay.Application.Features;
using Permoda.Pay.Domain.Common;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Features.Sales;

public sealed class ProcessSaleHandler
{
    private const int MaximumRedemptionAttempts = 2;

    private readonly IGiftCardProvider _provider;
    private readonly IPendingPaymentRepository _pendingPayments;
    private readonly ITransactionNumberGenerator _transactionNumbers;
    private readonly ITransactionAudit _audit;
    private readonly IClock _clock;

    public ProcessSaleHandler(
        IGiftCardProvider provider,
        IPendingPaymentRepository pendingPayments,
        ITransactionNumberGenerator transactionNumbers,
        ITransactionAudit audit,
        IClock clock)
    {
        _provider = provider;
        _pendingPayments = pendingPayments;
        _transactionNumbers = transactionNumbers;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ProcessSaleResult> HandleAsync(
        ProcessSaleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var inputResult = CreateInput(command);

        if (inputResult.IsFailure)
        {
            return ProcessSaleResult.Declined(
                null,
                ApplicationError.Validation(
                    inputResult.Error.Code,
                    inputResult.Error.Description));
        }

        for (var attempt = 1; attempt <= MaximumRedemptionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var transactionResult = PaymentTransaction.Create(
                _transactionNumbers.Generate(),
                inputResult.Value.StoreId,
                inputResult.Value.TerminalId,
                inputResult.Value.CashierId,
                inputResult.Value.CardIdentifier,
                inputResult.Value.Amount,
                command.Operation);

            if (transactionResult.IsFailure)
            {
                return ProcessSaleResult.Declined(
                    null,
                    ApplicationError.Validation(
                        transactionResult.Error.Code,
                        transactionResult.Error.Description));
            }

            var transaction = transactionResult.Value;
            await AuditAsync(transaction, TransactionAuditEvent.Created, null, cancellationToken);

            // Pre-flight /balance (solo en redención — activación y recarga crean o incrementan
            // saldo, no lo consumen). Confirmado en vivo (2026-07-28): /balance responde en
            // PESOS tal cual, igual que /activation, /redemption y /orderCreation — comparar
            // sin ×100. Es best-effort: si Ogloba devuelve saldo insuficiente o tarjeta
            // inactiva, paramos ANTES del Step 1 (ahorramos una pre-autorización bloqueada).
            // Si /balance falla por timeout/red, dejamos pasar — /redemption rechazará
            // después con el mismo motivo si aplica.
            if (transaction.Operation == GiftCardOperation.Redemption)
            {
                var preCheckResult = await PreCheckBalanceAsync(transaction, cancellationToken);

                if (preCheckResult is not null)
                {
                    return preCheckResult;
                }
            }

            // La activación usa el endpoint /activation; la redención /redemption.
            // Se concatena la cédula y el nombre del cliente al campo `note` del request en
            // activación: el endpoint /activation no tiene campo dedicado para datos del cliente,
            // así que viajamos en el texto libre que Ogloba persiste en el servidor.
            //
            // El correo SÍ tiene campo propio, y es lo que hace que Ogloba envíe el bono digital
            // al beneficiario cuando se confirma la activación. Solo se manda al ACTIVAR: en una
            // redención no hay nada que enviar.
            var step1Request = GiftCardRequestFactory.CreateRedemption(
                transaction,
                BuildCustomerNote(command),
                email: transaction.Operation == GiftCardOperation.Activation
                    ? command.CustomerEmail
                    : null);
            var redemptionResult = transaction.Operation == GiftCardOperation.Activation
                ? await _provider.ActivateAsync(step1Request, cancellationToken)
                : await _provider.RedeemAsync(step1Request, cancellationToken);

            if (redemptionResult.IsSuccess)
            {
                return await CompleteAuthorizedSaleAsync(
                    transaction,
                    redemptionResult.Value,
                    cancellationToken);
            }

            if (!redemptionResult.Failure.IsUncertain)
            {
                transaction.RegisterRequestRejection();
                await AuditAsync(
                    transaction,
                    TransactionAuditEvent.RequestRejected,
                    redemptionResult.Failure.Code,
                    cancellationToken);

                return ProcessSaleResult.Declined(
                    transaction.TransactionNumber.Value,
                    MapDeclinedError(redemptionResult.Failure));
            }

            var reversalResult = await ReverseUncertainRequestAsync(
                transaction,
                redemptionResult.Failure,
                cancellationToken);

            if (!reversalResult.IsSuccess)
            {
                return ProcessSaleResult.Unknown(
                    transaction.TransactionNumber.Value,
                    transaction.ReferenceNumber?.Value,
                    ApplicationError.Unknown(
                        reversalResult.Failure.Code,
                        reversalResult.Failure.Description));
            }

            if (attempt == MaximumRedemptionAttempts)
            {
                return ProcessSaleResult.Declined(
                    transaction.TransactionNumber.Value,
                    ApplicationError.Technical(
                        "sale.timeout_reversed",
                        "The redemption timed out and was safely reversed."));
            }
        }

        throw new InvalidOperationException("The configured redemption attempts were exhausted unexpectedly.");
    }

    private async Task<ProcessSaleResult> CompleteAuthorizedSaleAsync(
        PaymentTransaction transaction,
        RedemptionAuthorization authorization,
        CancellationToken cancellationToken)
    {
        var requestResult = transaction.RegisterRequest(authorization.ReferenceNumber);

        if (requestResult.IsFailure)
        {
            return ProcessSaleResult.Unknown(
                transaction.TransactionNumber.Value,
                authorization.ReferenceNumber.Value,
                ApplicationError.Unknown(
                    requestResult.Error.Code,
                    requestResult.Error.Description));
        }

        await AuditAsync(transaction, TransactionAuditEvent.RequestAccepted, null, cancellationToken);

        var persistenceResult = await _pendingPayments.SaveAsync(transaction, cancellationToken);

        if (persistenceResult.IsFailure)
        {
            // La persistencia local falló después de que Ogloba autorizó Step 1: la
            // pre-autorización quedó en `Requested`. Llamamos /reversal (no /cancelTransaction
            // — KOAJ no maneja cancelaciones de bonos). Si el reversal también falla, dejamos
            // la transacción en BD como ReversalPending para que RecoveryStartupRunner la
            // reintente en el próximo arranque.
            return await ReverseAfterPersistenceFailureAsync(
                transaction,
                persistenceResult.Failure,
                cancellationToken);
        }

        var prepareConfirmationResult = transaction.PrepareConfirmation();

        if (prepareConfirmationResult.IsFailure)
        {
            return ProcessSaleResult.Unknown(
                transaction.TransactionNumber.Value,
                transaction.ReferenceNumber?.Value,
                ApplicationError.Unknown(
                    prepareConfirmationResult.Error.Code,
                    prepareConfirmationResult.Error.Description));
        }

        await AuditAsync(transaction, TransactionAuditEvent.ConfirmationPending, null, cancellationToken);

        var confirmationResult = await _provider.ConfirmAsync(
            GiftCardRequestFactory.CreateTransactionAction(transaction),
            cancellationToken);

        if (confirmationResult.IsFailure)
        {
            return confirmationResult.Failure.Type == PortFailureType.Rejected
                ? await HandleDefinitiveStep2FailureAsync(
                    transaction,
                    confirmationResult.Failure,
                    cancellationToken)
                : await KeepConfirmationPendingAsync(
                    transaction,
                    confirmationResult.Failure,
                    cancellationToken);
        }

        transaction.RegisterConfirmation();
        await AuditAsync(transaction, TransactionAuditEvent.Confirmed, null, cancellationToken);
        await _pendingPayments.SaveAsync(transaction, cancellationToken);

        var reconciliationCompleted = await ReconcileAsync(transaction, cancellationToken);

        return ProcessSaleResult.Approved(
            transaction.TransactionNumber.Value,
            authorization.ReferenceNumber.Value,
            transaction.CardIdentifier.MaskedValue,
            authorization.RemainingBalance.MinorUnits,
            authorization.RemainingBalance.Currency,
            !reconciliationCompleted,
            transaction.StoreId.Value,
            transaction.TerminalId.Value,
            transaction.Amount.MinorUnits,
            authorization.CardNumber,
            authorization.EGiftCardUrl);
    }

    private async Task<PortResult<Unit>> ReverseUncertainRequestAsync(
        PaymentTransaction transaction,
        PortFailure originalFailure,
        CancellationToken cancellationToken)
    {
        var preparationResult = transaction.PrepareReversal();

        if (preparationResult.IsFailure)
        {
            return PortResult<Unit>.Failed(new PortFailure(
                preparationResult.Error.Code,
                preparationResult.Error.Description,
                PortFailureType.Indeterminate));
        }

        await _pendingPayments.SaveAsync(transaction, cancellationToken);
        await AuditAsync(
            transaction,
            TransactionAuditEvent.ReversalPending,
            originalFailure.Code,
            cancellationToken);

        var reversalResult = await _provider.ReverseAsync(
            GiftCardRequestFactory.CreateReversal(transaction),
            cancellationToken);

        if (reversalResult.IsFailure)
        {
            await _pendingPayments.SaveAsync(transaction, cancellationToken);
            await AuditAsync(
                transaction,
                TransactionAuditEvent.RecoveryRequired,
                reversalResult.Failure.Code,
                cancellationToken);

            return reversalResult;
        }

        transaction.RegisterReversal();
        await AuditAsync(transaction, TransactionAuditEvent.Reversed, null, cancellationToken);
        await _pendingPayments.SaveAsync(transaction, cancellationToken);
        await _pendingPayments.DeleteAsync(transaction.TransactionNumber, cancellationToken);

        return PortResult<Unit>.Success(Unit.Value);
    }

    private async Task<ProcessSaleResult> ReverseAfterPersistenceFailureAsync(
        PaymentTransaction transaction,
        PortFailure persistenceFailure,
        CancellationToken cancellationToken)
    {
        var preparationResult = transaction.PrepareReversal();

        if (preparationResult.IsFailure)
        {
            return ProcessSaleResult.Unknown(
                transaction.TransactionNumber.Value,
                transaction.ReferenceNumber?.Value,
                ApplicationError.Unknown(
                    preparationResult.Error.Code,
                    preparationResult.Error.Description));
        }

        await _pendingPayments.SaveAsync(transaction, cancellationToken);
        await AuditAsync(
            transaction,
            TransactionAuditEvent.ReversalPending,
            persistenceFailure.Code,
            cancellationToken);

        var reversalResult = await _provider.ReverseAsync(
            GiftCardRequestFactory.CreateReversal(transaction),
            cancellationToken);

        if (reversalResult.IsFailure)
        {
            await _pendingPayments.SaveAsync(transaction, cancellationToken);
            await AuditAsync(
                transaction,
                TransactionAuditEvent.RecoveryRequired,
                reversalResult.Failure.Code,
                cancellationToken);

            return ProcessSaleResult.Unknown(
                transaction.TransactionNumber.Value,
                transaction.ReferenceNumber?.Value,
                ApplicationError.Unknown(
                    reversalResult.Failure.Code,
                    reversalResult.Failure.Description));
        }

        transaction.RegisterReversal();
        await AuditAsync(transaction, TransactionAuditEvent.Reversed, null, cancellationToken);
        await _pendingPayments.SaveAsync(transaction, cancellationToken);
        await _pendingPayments.DeleteAsync(transaction.TransactionNumber, cancellationToken);

        return ProcessSaleResult.Unknown(
            transaction.TransactionNumber.Value,
            transaction.ReferenceNumber?.Value,
            ApplicationError.Technical(
                persistenceFailure.Code,
                persistenceFailure.Description));
    }

    private async Task<ProcessSaleResult> HandleDefinitiveStep2FailureAsync(
        PaymentTransaction transaction,
        PortFailure failure,
        CancellationToken cancellationToken)
    {
        transaction.RegisterStep2Failure();
        await _pendingPayments.SaveAsync(transaction, cancellationToken);
        await AuditAsync(
            transaction,
            TransactionAuditEvent.RecoveryRequired,
            failure.Code,
            cancellationToken);

        var reconciliationCompleted = await ReconcileAsync(transaction, cancellationToken);

        return ProcessSaleResult.Declined(
            transaction.TransactionNumber.Value,
            MapDeclinedError(failure),
            !reconciliationCompleted);
    }

    private async Task<ProcessSaleResult> KeepConfirmationPendingAsync(
        PaymentTransaction transaction,
        PortFailure failure,
        CancellationToken cancellationToken)
    {
        await _pendingPayments.SaveAsync(transaction, cancellationToken);
        await AuditAsync(
            transaction,
            TransactionAuditEvent.RecoveryRequired,
            failure.Code,
            cancellationToken);

        return ProcessSaleResult.Unknown(
            transaction.TransactionNumber.Value,
            transaction.ReferenceNumber?.Value,
            ApplicationError.Unknown(failure.Code, failure.Description));
    }

    private async Task<bool> ReconcileAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken)
    {
        var finalStatusResult = transaction.GetPendingReconciliationFinalStatus();

        if (finalStatusResult.IsFailure || transaction.ReferenceNumber is null)
        {
            await AuditAsync(
                transaction,
                TransactionAuditEvent.RecoveryRequired,
                finalStatusResult.IsFailure ? finalStatusResult.Error.Code : "reference_number.required",
                cancellationToken);
            return false;
        }

        await AuditAsync(
            transaction,
            TransactionAuditEvent.ReconciliationPending,
            null,
            cancellationToken);

        var reconciliationResult = await _provider.ReconcileAsync(
            GiftCardRequestFactory.CreateReconciliation(
                transaction,
                finalStatusResult.Value),
            cancellationToken);

        if (reconciliationResult.IsFailure)
        {
            await _pendingPayments.SaveAsync(transaction, cancellationToken);
            await AuditAsync(
                transaction,
                TransactionAuditEvent.RecoveryRequired,
                reconciliationResult.Failure.Code,
                cancellationToken);
            return false;
        }

        transaction.RegisterReconciliation();
        await AuditAsync(transaction, TransactionAuditEvent.Reconciled, null, cancellationToken);
        await _pendingPayments.SaveAsync(transaction, cancellationToken);
        await _pendingPayments.DeleteAsync(transaction.TransactionNumber, cancellationToken);

        return true;
    }

    private async Task AuditAsync(
        PaymentTransaction transaction,
        TransactionAuditEvent auditEvent,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        await _audit.RecordAsync(
            new TransactionAuditEntry(
                _clock.UtcNow,
                transaction.TransactionNumber.Value,
                transaction.ReferenceNumber?.Value,
                transaction.Status,
                auditEvent,
                errorCode),
            cancellationToken);
    }

    /// <summary>
    /// Pre-check de saldo antes de /redemption. Devuelve un <see cref="ProcessSaleResult"/>
    /// cuando hay que abortar (saldo insuficiente / tarjeta inactiva), o <c>null</c> cuando
    /// se puede proceder. Un fallo técnico de /balance NO aborta — dejamos que /redemption
    /// haga su propio chequeo atómico.
    /// </summary>
    private async Task<ProcessSaleResult?> PreCheckBalanceAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken)
    {
        var balanceQuery = new BalanceQuery(
            transaction.StoreId,
            transaction.TerminalId,
            transaction.CashierId,
            transaction.TransactionNumber,
            transaction.CardIdentifier);

        var balanceResult = await _provider.GetBalanceAsync(balanceQuery, cancellationToken);

        if (balanceResult.IsFailure)
        {
            return null;
        }

        var balance = balanceResult.Value;

        if (!balance.IsActive)
        {
            return ProcessSaleResult.Declined(
                transaction.TransactionNumber.Value,
                ApplicationError.Declined(
                    "ogloba.card_inactive",
                    $"El bono no se puede usar en este momento (estado: {balance.Status})."));
        }

        if (transaction.Amount.MinorUnits > balance.BalanceMinorUnits)
        {
            return ProcessSaleResult.Declined(
                transaction.TransactionNumber.Value,
                ApplicationError.Declined(
                    "53",
                    $"El bono no tiene saldo suficiente para esta compra (saldo: {balance.BalanceMinorUnits:N0} COP)."));
        }

        return null;
    }

    private static Result<SaleInput> CreateInput(ProcessSaleCommand command)
    {
        var storeIdResult = StoreId.Create(command.StoreId);

        if (storeIdResult.IsFailure)
        {
            return Result<SaleInput>.Failure(storeIdResult.Error);
        }

        var terminalIdResult = TerminalId.Create(command.TerminalId);

        if (terminalIdResult.IsFailure)
        {
            return Result<SaleInput>.Failure(terminalIdResult.Error);
        }

        var cashierIdResult = CashierId.Create(command.CashierId);

        if (cashierIdResult.IsFailure)
        {
            return Result<SaleInput>.Failure(cashierIdResult.Error);
        }

        // Física → PAN en cardNumber; Virtual/digital → gencode (código de producto).
        var cardIdentifierResult = command.CardKind == CardIdentifierKind.DigitalGencode
            ? CardIdentifier.CreateDigitalGencode(command.CardNumber)
            : CardIdentifier.CreatePhysicalCard(command.CardNumber);

        if (cardIdentifierResult.IsFailure)
        {
            return Result<SaleInput>.Failure(cardIdentifierResult.Error);
        }

        var amountResult = Money.Create(command.AmountUnits, command.Currency);

        if (amountResult.IsFailure)
        {
            return Result<SaleInput>.Failure(amountResult.Error);
        }

        if (amountResult.Value.MinorUnits == 0)
        {
            return Result<SaleInput>.Failure(PaymentTransactionErrors.InvalidAmount);
        }

        return Result<SaleInput>.Success(new SaleInput(
            storeIdResult.Value,
            terminalIdResult.Value,
            cashierIdResult.Value,
            cardIdentifierResult.Value,
            amountResult.Value));
    }

    private static ApplicationError MapDeclinedError(PortFailure failure) =>
        failure.Type == PortFailureType.Rejected
            ? ApplicationError.Declined(failure.Code, failure.Description)
            : ApplicationError.Technical(failure.Code, failure.Description);

    /// <summary>
    /// Construye el string concatenado que se manda a Ogloba en el campo <c>note</c> del
    /// request de /activation. Vacío si el cajero no capturó datos del cliente.
    /// </summary>
    private static string? BuildCustomerNote(ProcessSaleCommand command) =>
        command.Operation == GiftCardOperation.Activation
            ? CustomerMobileNoBuilder.Build(command.CustomerDocumentNumber, command.CustomerName)
            : null;

    private sealed record SaleInput(
        StoreId StoreId,
        TerminalId TerminalId,
        CashierId CashierId,
        CardIdentifier CardIdentifier,
        Money Amount);
}
