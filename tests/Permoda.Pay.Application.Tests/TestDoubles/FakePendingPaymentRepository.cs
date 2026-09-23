using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Persistence;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Tests.TestDoubles;

internal sealed class FakePendingPaymentRepository : IPendingPaymentRepository
{
    private readonly IList<string> _calls;

    public FakePendingPaymentRepository(IList<string> calls)
    {
        _calls = calls;
    }

    public Queue<PortResult<Unit>> SaveResults { get; } = new();

    public Queue<PortResult<Unit>> DeleteResults { get; } = new();

    public List<PaymentStatus> SavedStatuses { get; } = [];

    public List<string> DeletedTransactionNumbers { get; } = [];

    public List<PaymentTransaction> PendingPayments { get; } = [];

    public PortFailure? LoadFailure { get; set; }

    public Task<PortResult<Unit>> SaveAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add($"repository.save.{transaction.Status}");
        SavedStatuses.Add(transaction.Status);
        return Task.FromResult(NextOrSuccess(SaveResults));
    }

    public Task<PortResult<Unit>> DeleteAsync(
        TransactionNumber transactionNumber,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("repository.delete");
        DeletedTransactionNumbers.Add(transactionNumber.Value);
        return Task.FromResult(NextOrSuccess(DeleteResults));
    }

    public Task<PortResult<IReadOnlyCollection<PaymentTransaction>>> LoadPendingAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _calls.Add("repository.load");

        var result = LoadFailure is null
            ? PortResult<IReadOnlyCollection<PaymentTransaction>>.Success(PendingPayments.ToArray())
            : PortResult<IReadOnlyCollection<PaymentTransaction>>.Failed(LoadFailure);

        return Task.FromResult(result);
    }

    private static PortResult<Unit> NextOrSuccess(Queue<PortResult<Unit>> results) =>
        results.Count == 0
            ? PortResult<Unit>.Success(Unit.Value)
            : results.Dequeue();
}
