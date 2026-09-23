using Permoda.Pay.Application.Abstractions.Time;

namespace Permoda.Pay.Application.Tests.TestDoubles;

internal sealed class StubClock : IClock
{
    public DateTimeOffset UtcNow { get; } = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
}
