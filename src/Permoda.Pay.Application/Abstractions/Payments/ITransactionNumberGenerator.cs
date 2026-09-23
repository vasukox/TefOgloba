using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Abstractions.Payments;

public interface ITransactionNumberGenerator
{
    TransactionNumber Generate();
}
