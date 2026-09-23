using Permoda.Pay.Application.Abstractions;

namespace Permoda.Pay.Infrastructure.Security;

public interface IDatabaseEncryptionKeyProvider
{
    ValueTask<PortResult<DatabaseEncryptionKey>> GetOrCreateAsync(
        CancellationToken cancellationToken);
}
