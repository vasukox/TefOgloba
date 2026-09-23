using Permoda.Pay.Maui.ViewModels.Pos;

namespace Permoda.Pay.Maui.Views.Pos;

/// <summary>
/// Ingreso del cajero: usuario y contraseña.
/// <para>
/// Reemplaza al selector de dos pasos (elegir el nombre de una grilla y luego la contraseña). La
/// grilla publicaba quién trabaja en esa caja y obligaba a buscarse antes de poder operar.
/// </para>
/// <para>
/// Se navega como RUTA GLOBAL (<c>Routing.RegisterRoute</c>) y se resuelve como transient, no
/// como <c>FlyoutItem</c> con <c>DataTemplate</c> y singleton en DI. Ese era el origen de la
/// "pestaña que se antepone": una sola instancia de página terminaba colgada de dos padres del
/// Shell, y se veía la copia vacía encima de la buena.
/// </para>
/// </summary>
public partial class CashierLoginPage : ContentPage
{
    private readonly CashierLoginViewModel _viewModel;

    public CashierLoginPage(CashierLoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>
    /// El try/catch existe porque este método es <c>async void</c>: sin él, cualquier excepción
    /// de la carga se pierde y la página queda dibujada a medias, con el encabezado puesto y el
    /// cuerpo vacío.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await _viewModel.LoadAsync();

            // El cursor arranca en Usuario para que el cajero pueda escribir de una. Sin esto hay
            // que tocar el campo antes de teclear, en cada ingreso de cada turno.
            Dispatcher.Dispatch(() => UserEntry.Focus());
        }
        catch (Exception exception)
        {
            Android.Util.Log.Error("TefOgloba", $"[CashierLoginPage] Carga falló: {exception}");
            _viewModel.Message = $"No se pudo cargar la pantalla. {exception.Message}";
        }
    }

    /// <summary>
    /// Enter en Usuario baja a Contraseña en vez de intentar entrar con el campo vacío: es lo que
    /// hace cualquier formulario de ingreso y lo que el cajero espera del teclado.
    /// </summary>
    private void OnUserCompleted(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(() => PasswordEntry.Focus());

    /// <summary>
    /// Salir con el botón físico durante un cobro dejaría a HiPOS esperando una respuesta que
    /// no llega, y la venta colgada. Se trata como cancelación explícita.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        if (!Services.HiPosFlow.IsActive)
        {
            return base.OnBackButtonPressed();
        }

        Services.HiPosFlow.Complete(null);
        Services.HiPosFlow.CloseModuleScreen();
        return true;
    }
}
