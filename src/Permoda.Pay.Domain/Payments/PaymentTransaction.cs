using Permoda.Pay.Domain.Common;
using Permoda.Pay.Domain.GiftCards;

namespace Permoda.Pay.Domain.Payments;

public sealed class PaymentTransaction
{
    private PaymentTransaction(
        TransactionNumber transactionNumber,
        StoreId storeId,
        TerminalId terminalId,
        CashierId cashierId,
        CardIdentifier cardIdentifier,
        Money amount,
        GiftCardOperation operation)
    {
        TransactionNumber = transactionNumber;
        StoreId = storeId;
        TerminalId = terminalId;
        CashierId = cashierId;
        CardIdentifier = cardIdentifier;
        Amount = amount;
        Operation = operation;
        Status = PaymentStatus.Created;
        ReconciliationStatus = ReconciliationStatus.NotRequired;
    }

    public TransactionNumber TransactionNumber { get; }

    public StoreId StoreId { get; }

    public TerminalId TerminalId { get; }

    public CashierId CashierId { get; }

    public CardIdentifier CardIdentifier { get; }

    public Money Amount { get; }

    public GiftCardOperation Operation { get; }

    public ReferenceNumber? ReferenceNumber { get; private set; }

    public PaymentStatus Status { get; private set; }

    public ReconciliationStatus ReconciliationStatus { get; private set; }

    public static Result<PaymentTransaction> Create(
        TransactionNumber? transactionNumber,
        StoreId? storeId,
        TerminalId? terminalId,
        CashierId? cashierId,
        CardIdentifier? cardIdentifier,
        Money? amount,
        GiftCardOperation operation)
    {
        if (transactionNumber is null)
        {
            return Result<PaymentTransaction>.Failure(PaymentTransactionErrors.Required("transaction_number"));
        }

        if (storeId is null)
        {
            return Result<PaymentTransaction>.Failure(PaymentTransactionErrors.Required("store_id"));
        }

        if (terminalId is null)
        {
            return Result<PaymentTransaction>.Failure(PaymentTransactionErrors.Required("terminal_id"));
        }

        if (cashierId is null)
        {
            return Result<PaymentTransaction>.Failure(PaymentTransactionErrors.Required("cashier_id"));
        }

        if (cardIdentifier is null)
        {
            return Result<PaymentTransaction>.Failure(PaymentTransactionErrors.Required("card_identifier"));
        }

        if (amount is null)
        {
            return Result<PaymentTransaction>.Failure(PaymentTransactionErrors.Required("amount"));
        }

        if (!Enum.IsDefined(operation))
        {
            return Result<PaymentTransaction>.Failure(PaymentTransactionErrors.InvalidOperation);
        }

        if (amount.MinorUnits == 0 ||
            (operation is (GiftCardOperation.Activation or GiftCardOperation.Reload) && amount.MinorUnits < 0))
        {
            return Result<PaymentTransaction>.Failure(PaymentTransactionErrors.InvalidAmount);
        }

        return Result<PaymentTransaction>.Success(new PaymentTransaction(
            transactionNumber,
            storeId,
            terminalId,
            cashierId,
            cardIdentifier,
            amount,
            operation));
    }

    public static Result<PaymentTransaction> Restore(PaymentTransactionState? state)
    {
        if (state is null)
        {
            return Result<PaymentTransaction>.Failure(PaymentTransactionErrors.Required("state"));
        }

        var transactionResult = Create(
            state.TransactionNumber,
            state.StoreId,
            state.TerminalId,
            state.CashierId,
            state.CardIdentifier,
            state.Amount,
            state.Operation);

        if (transactionResult.IsFailure)
        {
            return transactionResult;
        }

        if (!Enum.IsDefined(state.Status) ||
            !Enum.IsDefined(state.ReconciliationStatus) ||
            !HasValidReference(state) ||
            !HasValidReconciliationState(state))
        {
            return Result<PaymentTransaction>.Failure(
                PaymentTransactionErrors.InvalidPersistedState);
        }

        var transaction = transactionResult.Value;
        transaction.ReferenceNumber = state.ReferenceNumber;
        transaction.Status = state.Status;
        transaction.ReconciliationStatus = state.ReconciliationStatus;

        return Result<PaymentTransaction>.Success(transaction);
    }

    public PaymentTransactionState ToState() =>
        new(
            TransactionNumber,
            StoreId,
            TerminalId,
            CashierId,
            CardIdentifier,
            Amount,
            Operation,
            ReferenceNumber,
            Status,
            ReconciliationStatus);

    public Result RegisterRequest(ReferenceNumber? referenceNumber)
    {
        if (referenceNumber is null)
        {
            return Result.Failure(PaymentTransactionErrors.Required("reference_number"));
        }

        if (Status == PaymentStatus.Requested)
        {
            return ReferenceNumber == referenceNumber
                ? Result.Success()
                : Result.Failure(PaymentTransactionErrors.ReferenceConflict);
        }

        if (Status != PaymentStatus.Created)
        {
            return InvalidTransition("register the Step 1 response");
        }

        ReferenceNumber = referenceNumber;
        Status = PaymentStatus.Requested;

        return Result.Success();
    }

    public Result RegisterRequestRejection()
    {
        if (Status == PaymentStatus.RequestRejected)
        {
            return Result.Success();
        }

        if (Status != PaymentStatus.Created)
        {
            return InvalidTransition("reject the Step 1 request");
        }

        Status = PaymentStatus.RequestRejected;

        return Result.Success();
    }

    public Result PrepareConfirmation()
    {
        if (Status == PaymentStatus.ConfirmationPending)
        {
            return Result.Success();
        }

        if (Status != PaymentStatus.Requested)
        {
            return InvalidTransition("prepare confirmation");
        }

        Status = PaymentStatus.ConfirmationPending;

        return Result.Success();
    }

    public Result RegisterConfirmation()
    {
        if (Status == PaymentStatus.Confirmed)
        {
            return Result.Success();
        }

        if (Status != PaymentStatus.ConfirmationPending)
        {
            return InvalidTransition("confirm");
        }

        Status = PaymentStatus.Confirmed;
        ReconciliationStatus = ReconciliationStatus.Pending;

        return Result.Success();
    }

    public Result RegisterStep2Failure()
    {
        if (Status == PaymentStatus.Step2Failed)
        {
            return Result.Success();
        }

        if (Status != PaymentStatus.ConfirmationPending)
        {
            return InvalidTransition("register a definitive Step 2 failure");
        }

        Status = PaymentStatus.Step2Failed;
        ReconciliationStatus = ReconciliationStatus.Pending;

        return Result.Success();
    }

    public Result PrepareReversal()
    {
        if (Status == PaymentStatus.ReversalPending)
        {
            return Result.Success();
        }

        // Created  = Step 1 sin respuesta todavía (timeout/error de red).
        // Requested = Step 1 respondió OK pero la persistencia local falló — la
        //              pre-autorización quedó en Ogloba y tenemos que deshacerla con /reversal.
        if (Status is not (PaymentStatus.Created or PaymentStatus.Requested))
        {
            return InvalidTransition("prepare reversal");
        }

        Status = PaymentStatus.ReversalPending;

        return Result.Success();
    }

    public Result RegisterReversal()
    {
        if (Status == PaymentStatus.Reversed)
        {
            return Result.Success();
        }

        if (Status != PaymentStatus.ReversalPending)
        {
            return InvalidTransition("reverse");
        }

        Status = PaymentStatus.Reversed;

        return Result.Success();
    }

    public Result<ReconciliationFinalStatus> GetPendingReconciliationFinalStatus()
    {
        if (ReconciliationStatus != ReconciliationStatus.Pending)
        {
            return Result<ReconciliationFinalStatus>.Failure(
                PaymentTransactionErrors.ReconciliationNotPending);
        }

        var finalStatus = Status switch
        {
            PaymentStatus.Confirmed => ReconciliationFinalStatus.Successful,
            PaymentStatus.Step2Failed => ReconciliationFinalStatus.Failed,
            _ => throw new InvalidOperationException(
                $"Status {Status} cannot have a pending reconciliation.")
        };

        return Result<ReconciliationFinalStatus>.Success(finalStatus);
    }

    public Result RegisterReconciliation()
    {
        if (ReconciliationStatus == ReconciliationStatus.Completed)
        {
            return Result.Success();
        }

        if (ReconciliationStatus != ReconciliationStatus.Pending)
        {
            return Result.Failure(PaymentTransactionErrors.ReconciliationNotPending);
        }

        ReconciliationStatus = ReconciliationStatus.Completed;

        return Result.Success();
    }

    private static bool HasValidReference(PaymentTransactionState state)
    {
        var requiresReference = state.Status is
            PaymentStatus.Requested or
            PaymentStatus.ConfirmationPending or
            PaymentStatus.Confirmed or
            PaymentStatus.Step2Failed;

        return requiresReference
            ? state.ReferenceNumber is not null
            : state.ReferenceNumber is null;
    }

    private static bool HasValidReconciliationState(PaymentTransactionState state) =>
        state.Status switch
        {
            PaymentStatus.Confirmed or
            PaymentStatus.Step2Failed =>
                state.ReconciliationStatus is ReconciliationStatus.Pending or ReconciliationStatus.Completed,
            _ => state.ReconciliationStatus == ReconciliationStatus.NotRequired
        };

    private Result InvalidTransition(string action) =>
        Result.Failure(PaymentTransactionErrors.InvalidTransition(Status, action));
}
