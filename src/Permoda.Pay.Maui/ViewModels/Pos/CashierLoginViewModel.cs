using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Permoda.Pay.Maui.Common;
using Permoda.Pay.Maui.Services;

namespace Permoda.Pay.Maui.ViewModels.Pos;

/// <summary>
/// Ingreso del cajero: usuario y contraseña, como cualquier login.
/// <para>
/// Reemplaza al selector de dos pasos (elegir el nombre de una lista y después escribir la
/// contraseña). La lista tenía dos problemas: mostraba a todo el mundo quién trabaja en esa caja,
/// y con muchos cajeros obligaba a buscarse en una grilla antes de poder operar. Un usuario
/// escrito es más rápido y no publica la nómina.
/// </para>
/// <para>
/// Acá NO se crean contraseñas. Los cajeros se dan de alta con su contraseña en Configuración
/// (paso 3), así que todo cajero registrado ya tiene una. Esta pantalla solo verifica.
/// </para>
/// </summary>
public sealed partial class CashierLoginViewModel : ObservableObject
{
    private readonly PosSession _session;

    public CashierLoginViewModel(PosSession session) => _session = session;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn))]
    private string _userName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn))]
    private bool _isBusy;

    public bool CanSignIn =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(UserName)
        && !string.IsNullOrWhiteSpace(Password);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    /// <summary>Contexto de la operación: por cuánto es el cobro, si vino de HiPOS.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>
    /// La caja no tiene cajeros dados de alta. Entonces no se muestra el formulario —no habría
    /// contra qué validar— sino el camino para registrarlos.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowForm))]
    private bool _hasNoCashiers;

    public bool ShowForm => !HasNoCashiers;

    public async Task LoadAsync()
    {
        UserName = string.Empty;
        Password = string.Empty;
        Message = string.Empty;

        try
        {
            await _session.EnsureLoadedAsync(CancellationToken.None);

            var cashiers = _session.IsConfigured
                ? await _session.GetCashiersAsync(CancellationToken.None)
                : [];

            HasNoCashiers = cashiers.Count == 0;
        }
        catch (Exception exception)
        {
            // Leer del almacenamiento seguro puede fallar. Sin este catch la pantalla se dibuja a
            // medias y el cajero se queda mirando un vacío sin saber qué pasó.
            HasNoCashiers = false;
            Message = $"No se pudo leer la configuración de la caja. {exception.Message}";
        }

        Summary = HiPosFlow.Current is { } flow
            ? flow.Intent == HiPos.HiPosSaleIntent.Activation
                ? $"Entrada de caja · {FormatPesos(flow.AmountPesos)}"
                : $"Cobro · {FormatPesos(flow.AmountPesos)}"
            : "Activar bonos y consultar saldos";
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var user = UserName.Trim();
        var password = Password;

        if (user.Length == 0 || password.Length == 0)
        {
            Message = "Escribe tu usuario y tu contraseña.";
            return;
        }

        IsBusy = true;
        Message = string.Empty;

        try
        {
            var registered = await _session.IsCashierRegisteredAsync(user, CancellationToken.None);
            var passwordOk = registered && await _session.VerifyCashierPasswordAsync(user, password);

            // Se comprueba DESPUÉS de la contraseña y con su propio mensaje. Decir "está
            // desactivado" antes de validar la clave le confirmaría a un desconocido que ese
            // usuario existe; después de acertarla, ya no revela nada que no supiera, y en cambio
            // le evita al cajero pensar que se equivocó de contraseña cuando el problema es otro.
            if (passwordOk && !await _session.IsCashierEnabledAsync(user, CancellationToken.None))
            {
                Message = "Tu usuario está desactivado. Pídele al administrador que lo reactive.";
                Password = string.Empty;
                return;
            }

            if (!passwordOk)
            {
                // Un solo mensaje para "no existe" y para "contraseña incorrecta", a propósito:
                // decir cuál de las dos falló le confirma a un desconocido qué usuarios existen en
                // esta caja. Se limpia la contraseña, no el usuario: quien se equivocó al escribir
                // la clave no tiene por qué volver a teclear su nombre.
                Message = "Usuario o contraseña incorrectos.";
                Password = string.Empty;
                return;
            }

            await _session.LoginCashierAsync(user, CancellationToken.None);

            UserName = string.Empty;
            Password = string.Empty;
            Message = string.Empty;

            // Si vino de HiPOS, el destino lo decidió el POS. Si no, al menú.
            var route = HiPosFlow.Current is { } flow
                ? flow.Intent == HiPos.HiPosSaleIntent.Activation
                    ? AppRoutes.Activate
                    : AppRoutes.Redeem
                : AppRoutes.Home;

            await Shell.Current.GoToAsync(route);
        }
        catch (Exception exception)
        {
            Message = $"No se pudo validar el ingreso. {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task GoToSetupAsync() =>
        await Shell.Current.GoToAsync(
            _session.IsConfigured ? AppRoutes.CashierSetup : AppRoutes.Setup);

    /// <summary>
    /// Entrada a la zona de administración desde el ingreso de cajero.
    /// <para>
    /// Existe porque esta pantalla apaga el menú lateral (<c>Shell.FlyoutBehavior="Disabled"</c>)
    /// y por lo tanto era un callejón sin salida: un administrador que llegara acá no tenía cómo
    /// alcanzar Usuarios ni Configuración. El único escape era el botón "Registrar cajeros", que
    /// solo se muestra cuando la caja NO tiene ningún cajero — en una caja ya montada desaparece,
    /// y entonces para entrar a configurar hacía falta un usuario de cajero, que es justo lo que
    /// el administrador puede no tener.
    /// </para>
    /// <para>
    /// Poner esta puerta en una pantalla SIN autenticar no expone nada: lleva al PIN, no a la
    /// configuración. <c>SetupViewModel</c> no muestra un solo dato hasta que
    /// <c>VerifyAdminPinAsync</c> acierta, y lo mismo hace Usuarios por su cuenta.
    /// </para>
    /// <para>
    /// Va SIEMPRE a Configuración, sin la bifurcación de <c>GoToSetupCommand</c>: en una caja
    /// recién instalada esa pantalla es el primer paso del alta (crear el PIN) y se abre sin
    /// pedirlo, porque todavía no existe. El PIN protege una caja ya montada, no una que aún no
    /// puede operar.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task GoToAdminAsync() =>
        await Shell.Current.GoToAsync(AppRoutes.Setup);

    private static string FormatPesos(long pesos) =>
        $"${pesos.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-CO"))}";
}
