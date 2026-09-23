namespace Permoda.Pay.Maui.ViewModels;

/// <summary>Resultado de una operación (activación/redención) para la tarjeta animada.</summary>
/// <param name="Url">Link opcional que Ogloba entrega al cliente (sólo bonos virtuales).</param>
public sealed record PosResult(
    bool Success,
    string Headline,
    string MaskedCard,
    string Reference,
    string BalanceText,
    bool HasResult,
    string Url = "")
{
    public string State => Success ? "ok" : "fail";

    public string Icon => Success ? "✓" : "✗";

    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);

    public static PosResult None { get; } = new(false, string.Empty, string.Empty, string.Empty, string.Empty, false);
}
