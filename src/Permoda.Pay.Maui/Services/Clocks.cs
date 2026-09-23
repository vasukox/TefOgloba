using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Application.Abstractions.Time;

namespace Permoda.Pay.Maui.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class SteppingClock : IClock
{
    private DateTimeOffset _now;

    public SteppingClock(DateTimeOffset initial) =>
        _now = initial;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);

    public DateTimeOffset UtcNow => _now;
}
