using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Domain.Payments;

namespace Permoda.Pay.Maui.Services;

public sealed class MonotonicTransactionNumberGenerator : ITransactionNumberGenerator
{
    private long _counter;
    private readonly Func<DateTimeOffset> _timestampProvider;

    public MonotonicTransactionNumberGenerator(Func<DateTimeOffset>? timestampProvider = null)
    {
        _timestampProvider = timestampProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public TransactionNumber Generate()
    {
        var timestamp = _timestampProvider().ToUnixTimeMilliseconds();
        var sequence = Interlocked.Increment(ref _counter);
        var number = ((timestamp % 1_000_000_000_000L) * 1000) + (sequence % 1000);
        return TransactionNumber.Create(number.ToString()).Value;
    }
}
