using Permoda.Pay.Maui.ViewModels.Admin;

namespace Permoda.Pay.Maui.Views.Admin;

/// <summary>
/// Gestión de usuarios de la caja, detrás del PIN de administrador.
/// Ver <see cref="UserManagementViewModel"/> para por qué la puerta existe.
/// </summary>
public partial class UserManagementPage : ContentPage
{
    private readonly UserManagementViewModel _viewModel;

    public UserManagementPage(UserManagementViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        // "Cambiar contraseña" en una fila llena el formulario de arriba; el cursor va al campo
        // de la clave para que el administrador escriba de una, sin tener que buscarlo.
        _viewModel.PasswordChangeRequested += OnPasswordChangeRequested;
    }

    private void OnPasswordChangeRequested() =>
        Dispatcher.Dispatch(() => PasswordEntry.Focus());

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await _viewModel.LoadAsync();
        }
        catch (Exception exception)
        {
            Android.Util.Log.Error("TefOgloba", $"[UserManagementPage] Carga falló: {exception}");
            _viewModel.Message = $"No se pudo cargar la pantalla. {exception.Message}";
        }
    }

    /// <summary>
    /// Al salir se vuelve a pedir el PIN. Dejar la sesión de administrador abierta en una tablet
    /// de mostrador equivale a dejar la puerta abierta: el siguiente que abra el menú entra sin
    /// nada.
    /// </summary>
    protected override void OnDisappearing()
    {
        _viewModel.Lock();
        base.OnDisappearing();
    }

    /// <summary>
    /// Confirmación antes de eliminar. Va en la vista y no en el ViewModel porque es un diálogo
    /// de plataforma; el ViewModel se queda con la decisión ya tomada.
    /// </summary>
    private async void OnRemoveClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string user } || string.IsNullOrWhiteSpace(user))
        {
            return;
        }

        var confirmed = await DisplayAlert(
            "Eliminar usuario",
            $"¿Eliminar a {user}? No podrá volver a entrar hasta que lo registres de nuevo.",
            "Eliminar",
            "Cancelar");

        if (!confirmed)
        {
            return;
        }

        if (_viewModel.RemoveCommand.CanExecute(user))
        {
            await _viewModel.RemoveCommand.ExecuteAsync(user);
        }
    }
}
