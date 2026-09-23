using Permoda.Pay.Maui.ViewModels.Setup;

namespace Permoda.Pay.Maui.Views.Setup;

/// <summary>
/// La caja que reparte su configuración.
/// <para>
/// El servicio de red vive exactamente lo que dura esta pantalla: se enciende en
/// <c>OnAppearing</c> y se apaga en <c>OnDisappearing</c>. Atarlo al ciclo de vida de la página y
/// no a un servicio de la app es lo que garantiza que no quede una caja ofreciendo la llave de
/// producción de la tienda cuando nadie está mirando.
/// </para>
/// </summary>
public partial class ReplicationHostPage : ContentPage
{
    private readonly ReplicationHostViewModel _viewModel;

    public ReplicationHostPage(ReplicationHostViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await _viewModel.StartAsync();
        }
        catch (Exception exception)
        {
            // Abrir un socket puede fallar por cosas del dispositivo (puerto ocupado, la red
            // caída). El ViewModel ya lo cuenta en pantalla; acá solo se impide que una
            // excepción en OnAppearing —que es async void— tumbe la app.
            Android.Util.Log.Error("TefOgloba", $"[ReplicationHostPage] No se pudo abrir: {exception}");
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        try
        {
            await _viewModel.StopAsync();
        }
        catch (Exception exception)
        {
            Android.Util.Log.Error("TefOgloba", $"[ReplicationHostPage] No se pudo cerrar: {exception}");
        }
    }
}
