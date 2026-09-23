namespace Permoda.Pay.Maui.Common;

/// <summary>
/// Set de iconos line-style (estilo React/Lucide). Los SVGs viven en
/// <c>Resources/Images/ic_*.svg</c> con el color de stroke hardcodeado al token
/// correspondiente del design system (Primary / TextSecondary / Error / etc.) — no se
/// pueden tintar en runtime porque <c>Image.Color</c> en MAUI no es BindableProperty.
/// Si se necesita el mismo icono en otro color, duplicar el archivo (p. ej.
/// <c>ic_x_danger.svg</c>) y registrarlo en el .csproj.
/// </summary>
public static class Icons
{
    // Nombres de los iconos disponibles (deben coincidir con el sufijo del archivo SVG).
    public const string Home = "home";
    public const string Sparkles = "sparkles";
    public const string Card = "card";
    public const string Search = "search";
    public const string List = "list";
    public const string Cog = "cog";
    public const string Menu = "menu";
    public const string Plus = "plus";
    public const string X = "x";
    public const string Check = "check";
    public const string ArrowLeft = "arrow_left";
    public const string Eye = "eye";
    public const string Trash = "trash";

    /// <summary>
    /// Nombre del recurso para usar en <c>Image Source</c>. Va con extensión <c>.png</c> aunque
    /// el archivo fuente sea SVG: MAUI los convierte a PNG al compilar y los publica con esa
    /// extensión. Referenciarlos como <c>.svg</c> compila igual pero falla en runtime con
    /// FileNotFoundException.
    /// </summary>
    public static string File(string name) => $"ic_{name}.png";
}