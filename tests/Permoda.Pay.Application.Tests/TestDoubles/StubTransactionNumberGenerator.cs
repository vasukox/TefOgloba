using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Application.Tests.TestDoubles;

internal sealed class StubTransactionNumberGenerator : ITransactionNumberGenerator
{
    private readonly Queue<TransactionNumber> _numbers;

    public StubTransactionNumberGenerator(params string[] numbers)
    {
        _numbers = new Queue<TransactionNumber>(
            numbers.Select(number => TransactionNumber.Create(number).Value));
    }

    public TransactionNumber Generate()
    {
        if (_numbers.Count == 0)
        {
            throw new InvalidOperationException("No transaction number was configured.");
        }

        return _numbers.Dequeue();
    }
}
