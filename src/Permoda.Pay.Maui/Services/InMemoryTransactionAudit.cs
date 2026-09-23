using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Logging;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Maui.Services;

public sealed class InMemoryTransactionAudit : ITransactionAudit
{
    // Cap a N entradas con eviction FIFO. En cajas con
    // ~1.000 ventas/día, 10.000 cubre ~10 días de historial en RAM (~1-2 MB) sin
    // crecer sin límite. Lo que se sale del cap se pierde — el log completo está
    // exportable vía AdminLogExportPage (UAT XLSX) que es persistente en disco.
    public const int MaxRetainedEntries = 10_000;

    private readonly LinkedList<TransactionAuditEntry> _entries = new();
    private readonly object _lock = new();

    public IReadOnlyCollection<TransactionAuditEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList();
            }
        }
    }

    public Task<PortResult<Unit>> RecordAsync(
        TransactionAuditEntry entry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _entries.AddLast(entry);
            // Eviction FIFO: las más viejas salen primero.
            while (_entries.Count > MaxRetainedEntries)
            {
                _entries.RemoveFirst();
            }
        }

        return Task.FromResult(PortResult<Unit>.Success(Unit.Value));
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}
