using System.Xml;
using System.Xml.Linq;

namespace Permoda.Pay.Maui.Configuration;

/// <summary>
/// Configuración de la terminal (nivel administrador): identifica la tienda, la caja y el
/// endpoint Ogloba. El cajero NO vive aquí: es la sesión activa (login por turno). El nombre
/// de la tienda se trae de Ogloba (GET /getBuInfo) y se cachea aquí. La contraseña Ogloba se
/// guarda cifrada aparte (por tienda) vía el proveedor de credenciales.
/// </summary>
public sealed record StoreConfiguration(
    string StoreId,
    string TerminalId,
    string BaseUrl,
    string ApiVersion,
    string StoreName)
{
    public const string StoreIdField = "StoreId";
    public const string TerminalIdField = "TerminalId";
    public const string BaseUrlField = "BaseUrl";
    public const string ApiVersionField = "ApiVersion";
    public const string StoreNameField = "StoreName";

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(StoreId) &&
        !string.IsNullOrWhiteSpace(TerminalId) &&
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(ApiVersion);
}

public sealed class SecureStorageStoreConfigurationProvider
{
    // v3: se quitó CashierId (ahora es sesión) y se agregó StoreName.
    private const string StorageKey = "tefogloba.store-configuration.v3";

    /// <summary>Configuración persistida, o <c>null</c> si la terminal aún no se configuró.</summary>
    public async ValueTask<StoreConfiguration?> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storedValue = await SecureStorage.Default.GetAsync(StorageKey);

        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return null;
        }

        try
        {
            return Decode(storedValue);
        }
        catch (Exception exception) when (exception is FormatException or XmlException or InvalidDataException)
        {
            SecureStorage.Default.Remove(StorageKey);
            return null;
        }
    }

    public async ValueTask PersistAsync(
        StoreConfiguration configuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SecureStorage.Default.SetAsync(StorageKey, Encode(configuration));
    }

    private static string Encode(StoreConfiguration configuration)
    {
        var document = new XDocument(
            new XElement("Configuration",
                new XElement(StoreConfiguration.StoreIdField, configuration.StoreId),
                new XElement(StoreConfiguration.TerminalIdField, configuration.TerminalId),
                new XElement(StoreConfiguration.BaseUrlField, configuration.BaseUrl),
                new XElement(StoreConfiguration.ApiVersionField, configuration.ApiVersion),
                new XElement(StoreConfiguration.StoreNameField, configuration.StoreName)));

        var bytes = System.Text.Encoding.UTF8.GetBytes(document.ToString());
        return Convert.ToBase64String(bytes);
    }

    private static StoreConfiguration Decode(string stored)
    {
        var bytes = Convert.FromBase64String(stored);
        var xml = System.Text.Encoding.UTF8.GetString(bytes);
        var document = XDocument.Parse(xml);
        var root = document.Root
            ?? throw new InvalidDataException("Empty configuration root.");

        string Read(string field, bool required)
        {
            var value = root.Elements(field).FirstOrDefault()?.Value;
            if (string.IsNullOrEmpty(value) && required)
            {
                throw new InvalidDataException($"The configuration field '{field}' is missing.");
            }

            return value ?? string.Empty;
        }

        return new StoreConfiguration(
            Read(StoreConfiguration.StoreIdField, required: true),
            Read(StoreConfiguration.TerminalIdField, required: true),
            Read(StoreConfiguration.BaseUrlField, required: true),
            Read(StoreConfiguration.ApiVersionField, required: true),
            Read(StoreConfiguration.StoreNameField, required: false));
    }
}
