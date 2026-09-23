using System.Security.Cryptography;

namespace Permoda.Pay.Infrastructure.Security;

public sealed class DatabaseEncryptionKey : IDisposable
{
    public const int RequiredLength = 32;

    private byte[]? _bytes;

    public DatabaseEncryptionKey(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != RequiredLength)
        {
            throw new ArgumentException(
                $"The database encryption key must contain {RequiredLength} bytes.",
                nameof(bytes));
        }

        _bytes = bytes.ToArray();
    }

    public ReadOnlyMemory<byte> Bytes => _bytes is not null
        ? _bytes
        : throw new ObjectDisposedException(nameof(DatabaseEncryptionKey));

    public void Dispose()
    {
        if (_bytes is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_bytes);
        _bytes = null;
    }

    public override string ToString() => "[REDACTED]";
}
