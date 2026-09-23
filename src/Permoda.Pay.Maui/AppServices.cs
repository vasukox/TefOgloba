using Microsoft.Extensions.DependencyInjection;

namespace Permoda.Pay.Maui;

public static class AppServices
{
    public static IServiceProvider? Current { get; private set; }

    public static void Capture(IServiceProvider services) =>
        Current = services ?? throw new ArgumentNullException(nameof(services));
}
