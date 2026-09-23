namespace Permoda.Pay.Infrastructure.Ogloba.Authentication;

public sealed class OglobaStoreCredential
{
    public OglobaStoreCredential(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("The Ogloba store password is required.", nameof(password));
        }

        Password = password;
    }

    public string Password { get; }

    public override string ToString() => "[REDACTED]";
}
