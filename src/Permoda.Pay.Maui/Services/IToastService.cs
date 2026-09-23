namespace Permoda.Pay.Maui.Services;

public enum ToastLevel
{
    Info,
    Success,
    Error
}

/// <summary>
/// Notificaciones tipo toast — reemplaza al StatusBannerView (que mostraba un cuadro
/// grande en la página). Implementación Android usa el <c>android.widget.Toast</c>
/// nativo: aparece abajo, no tapa contenido, se auto-dismiss.
/// </summary>
public interface IToastService
{
    void Show(string message, ToastLevel level = ToastLevel.Info);
    void ShowInfo(string title, string message);
    void ShowSuccess(string title, string message);
    void ShowError(string title, string message);
}