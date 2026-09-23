using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Logging;

namespace Permoda.Pay.Application.Tests.TestDoubles;

internal sealed class FakeTransactionAudit : ITransactionAudit
{
    public List<TransactionAuditEntry> Entries { get; } = [];

    public Task<PortResult<Unit>> RecordAsync(
        TransactionAuditEntry entry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Entries.Add(entry);
        return Task.FromResult(PortResult<Unit>.Success(Unit.Value));
    }
}
