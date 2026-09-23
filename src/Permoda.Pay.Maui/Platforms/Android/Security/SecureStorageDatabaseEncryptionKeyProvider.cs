using System.Security.Cryptography;
using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Infrastructure.Security;

namespace Permoda.Pay.Maui.Platforms.Android.Security;

public sealed class SecureStorageDatabaseEncryptionKeyProvider : IDatabaseEncryptionKeyProvider
{
    private const string StorageKey = "tefogloba.database-key.v1";

    public async ValueTask<PortResult<DatabaseEncryptionKey>> GetOrCreateAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var storedValue = await SecureStorage.Default.GetAsync(StorageKey);
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(storedValue))
            {
                var storedBytes = Convert.FromBase64String(storedValue);

                try
                {
                    return PortResult<DatabaseEncryptionKey>.Success(
                        new DatabaseEncryptionKey(storedBytes));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(storedBytes);
                }
            }

            var generatedBytes = RandomNumberGenerator.GetBytes(
                DatabaseEncryptionKey.RequiredLength);

            try
            {
                await SecureStorage.Default.SetAsync(
                    StorageKey,
                    Convert.ToBase64String(generatedBytes));
                cancellationToken.ThrowIfCancellationRequested();

                return PortResult<DatabaseEncryptionKey>.Success(
                    new DatabaseEncryptionKey(generatedBytes));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(generatedBytes);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return PortResult<DatabaseEncryptionKey>.Failed(new PortFailure(
                "database_key.unavailable",
                "The encrypted database key is unavailable.",
                PortFailureType.Technical));
        }
    }
}
