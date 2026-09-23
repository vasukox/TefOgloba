using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Infrastructure.Security;

namespace Permoda.Pay.Infrastructure.Tests.TestDoubles;

internal sealed class StubDatabaseEncryptionKeyProvider : IDatabaseEncryptionKeyProvider
{
    private readonly byte[] _key;

    public StubDatabaseEncryptionKeyProvider(byte[] key)
    {
        _key = key.ToArray();
    }

    public int CallCount { get; private set; }

    public ValueTask<PortResult<DatabaseEncryptionKey>> GetOrCreateAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        return ValueTask.FromResult(
            PortResult<DatabaseEncryptionKey>.Success(new DatabaseEncryptionKey(_key)));
    }
}
