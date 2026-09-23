using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Abstractions.Persistence;

public interface IPendingPaymentRepository
{
    Task<PortResult<Unit>> SaveAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken);

    Task<PortResult<Unit>> DeleteAsync(
        TransactionNumber transactionNumber,
        CancellationToken cancellationToken);

    Task<PortResult<IReadOnlyCollection<PaymentTransaction>>> LoadPendingAsync(
        CancellationToken cancellationToken);
}
