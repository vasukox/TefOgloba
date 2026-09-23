using Permoda.Pay.Application.Abstractions;

namespace Permoda.Pay.Application.Abstractions.Logging;

public interface ITransactionAudit
{
    Task<PortResult<Unit>> RecordAsync(
        TransactionAuditEntry entry,
        CancellationToken cancellationToken);
}
