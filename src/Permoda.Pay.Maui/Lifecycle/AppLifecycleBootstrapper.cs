using Permoda.Pay.Application.Features.Lifecycle;
using Permoda.Pay.Application.Features.Recovery;
using Permoda.Pay.Application.Errors;
using Permoda.Pay.Maui.Services;

namespace Permoda.Pay.Maui.Lifecycle;

public interface IAppLifecycleBootstrapper
{
    Task RunAsync(CancellationToken cancellationToken = default);
}

public sealed class AppLifecycleBootstrapper : IAppLifecycleBootstrapper
{
    private readonly RecoveryStartupRunner _recoveryRunner;
    private readonly InMemoryTransactionAudit _audit;
    private readonly System.Func<DateTimeOffset> _timestampProvider;

    public AppLifecycleBootstrapper(
        RecoveryStartupRunner recoveryRunner,
        InMemoryTransactionAudit audit)
    {
        _recoveryRunner = recoveryRunner;
        _audit = audit;
        _timestampProvider = () => DateTimeOffset.UtcNow;
    }

    public Task RunAsync(CancellationToken cancellationToken = default) =>
        _recoveryRunner.RunOnceAsync(cancellationToken);
}
