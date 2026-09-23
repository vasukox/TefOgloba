using Permoda.Pay.Application.Abstractions.Logging;
using Permoda.Pay.Application.Abstractions.Time;
using Permoda.Pay.Application.Errors;
using Permoda.Pay.Domain.Payments;
using Permoda.Pay.Application.Features.Recovery;

namespace Permoda.Pay.Application.Features.Lifecycle;

public sealed class RecoveryStartupRunner
{
    private readonly RecoverPendingPaymentsHandler _handler;
    private readonly ITransactionAudit _audit;
    private readonly IClock _clock;
    private readonly object _gate = new();
    private bool _hasCompleted;
    private bool _isRunning;

    public RecoveryStartupRunner(
        RecoverPendingPaymentsHandler handler,
        ITransactionAudit audit,
        IClock clock)
    {
        _handler = handler;
        _audit = audit;
        _clock = clock;
    }

    public async Task<RecoverPendingPaymentsResult> RunOnceAsync(
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            // Sólo se bloquea el reintento tras una ejecución exitosa.
            // Antes se marcaba _hasRun antes del await, de modo que una excepción o un
            // fallo de carga dejaba la recuperación inhabilitada para todo el proceso.
            if (_hasCompleted || _isRunning)
            {
                return RecoverPendingPaymentsResult.LoadFailed(
                    ApplicationError.Technical(
                        "recovery.already_executed",
                        "The recovery process has already been executed for this process."));
            }

            _isRunning = true;
        }

        try
        {
            var result = await _handler.HandleAsync(cancellationToken);

            await _audit.RecordAsync(
                new TransactionAuditEntry(
                    TimestampUtc: _clock.UtcNow,
                    TransactionNumber: "lifecycle.startup",
                    ReferenceNumber: null,
                    Status: PaymentStatus.Created,
                    Event: TransactionAuditEvent.RecoveryRequired,
                    ErrorCode: $"recovered={result.Recovered};remaining={result.Remaining}"),
                cancellationToken);

            lock (_gate)
            {
                // Sólo se considera "ejecutado" si la carga funcionó; un LoadFailed
                // (base de datos no disponible) debe poder reintentarse más tarde.
                if (result.IsSuccess)
                {
                    _hasCompleted = true;
                }
            }

            return result;
        }
        finally
        {
            lock (_gate)
            {
                _isRunning = false;
            }
        }
    }
}
