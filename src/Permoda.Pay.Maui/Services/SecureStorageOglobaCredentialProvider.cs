using System.Security.Cryptography;
using System.Text;
using Permoda.Pay.Application.Abstractions;
using Permoda.Pay.Domain.Payments;
using Permoda.Pay.Infrastructure.Ogloba.Authentication;

namespace Permoda.Pay.Maui.Services;

public sealed class SecureStorageOglobaCredentialProvider : IOglobaCredentialProvider
{
    private const string StoragePrefix = "tefogloba.ogloba-password.";

    public async ValueTask<PortResult<OglobaStoreCredential>> GetAsync(
        StoreId storeId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageKey = StoragePrefix + storeId.Value;
        var storedValue = await SecureStorage.Default.GetAsync(storageKey);

        if (!string.IsNullOrWhiteSpace(storedValue))
        {
            try
            {
                return PortResult<OglobaStoreCredential>.Success(
                    new OglobaStoreCredential(Decode(storedValue)));
            }
            catch (Exception exception) when (exception is FormatException or CryptographicException)
            {
                // Valor corrupto: se descarta para que la app se auto-sane en el próximo intento.
                SecureStorage.Default.Remove(storageKey);
            }
        }

        var environmentPassword = Environment.GetEnvironmentVariable("OGLOBA_PASSWORD");

        if (!string.IsNullOrWhiteSpace(environmentPassword))
        {
            await SecureStorage.Default.SetAsync(storageKey, Encode(environmentPassword));
            cancellationToken.ThrowIfCancellationRequested();

            return PortResult<OglobaStoreCredential>.Success(
                new OglobaStoreCredential(environmentPassword));
        }

        return PortResult<OglobaStoreCredential>.Failed(new PortFailure(
            "ogloba.credentials_missing",
            "No Ogloba credentials are available for the requested store.",
            PortFailureType.Authentication));
    }

    public async ValueTask<PortResult<OglobaStoreCredential>> PersistAsync(
        StoreId storeId,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var credential = new OglobaStoreCredential(password);
        var storageKey = StoragePrefix + storeId.Value;
        await SecureStorage.Default.SetAsync(storageKey, Encode(password));
        cancellationToken.ThrowIfCancellationRequested();

        return PortResult<OglobaStoreCredential>.Success(credential);
    }

    private static string Encode(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        return Convert.ToBase64String(bytes);
    }

    private static string Decode(string stored)
    {
        var bytes = Convert.FromBase64String(stored);
        return Encoding.UTF8.GetString(bytes);
    }
}
