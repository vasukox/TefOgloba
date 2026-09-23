namespace Permoda.Pay.Application.Abstractions.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
