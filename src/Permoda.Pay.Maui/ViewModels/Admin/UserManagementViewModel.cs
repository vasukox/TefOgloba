using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Permoda.Pay.Maui.Services;

namespace Permoda.Pay.Maui.ViewModels.Admin;

/// <summary>
/// Gestión de usuarios de la caja: alta, cambio de contraseña y baja de cajeros.
/// <para>
/// Está detrás del PIN de administrador porque quien entra acá puede darse de alta a sí mismo, o
/// cambiarle la contraseña a otro y operar firmando con su nombre. Sin esa puerta, el login de
/// cajero no serviría de nada: cualquiera se crearía un usuario y entraría.
/// </para>
/// <para>
/// El desbloqueo dura lo que dura la pantalla. Al salir se vuelve a bloquear
/// (<see cref="Lock"/>): dejar la sesión de administrador abierta en una caja del mostrador es
/// dejar la puerta abierta.
/// </para>
/// </summary>
public sealed partial class UserManagementViewModel : ObservableObject
{
    private readonly PosSession _session;

    public UserManagementViewModel(PosSession session) => _session = session;

    // ===== Puerta: PIN de administrador =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked))]
    private bool _isUnlocked;

    public bool IsLocked => !IsUnlocked;

    [ObservableProperty]
    private string _adminPin = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _notice = string.Empty;

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

    [RelayCommand]
    private async Task UnlockAsync()
    {
        var pin = AdminPin.Trim();

        if (pin.Length == 0)
        {
            Message = "Escribe el PIN de administrador.";
            return;
        }

        if (!await _session.VerifyAdminPinAsync(pin))
        {
            Message = "PIN incorrecto.";
            AdminPin = string.Empty;
            return;
        }

        AdminPin = string.Empty;
        Message = string.Empty;
        IsUnlocked = true;

        await LoadAsync();
    }

    /// <summary>Vuelve a cerrar la puerta. Se llama al salir de la pantalla.</summary>
    public void Lock()
    {
        IsUnlocked = false;
        AdminPin = string.Empty;
        NewUserName = string.Empty;
        NewPassword = string.Empty;
        Message = string.Empty;
        Notice = string.Empty;
    }

    // ===== CRUD =====

    public ObservableCollection<ManagedUser> Users { get; } = new();

    public bool HasUsers => Users.Count > 0;

    /// <summary>
    /// Cuántos pueden entrar hoy. Se usa para no dejar la caja sin nadie: desactivar al último
    /// activo la deja tan bloqueada como borrarlo, solo que de forma menos evidente.
    /// </summary>
    private int EnabledCount => Users.Count(u => u.IsEnabled);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    private string _newUserName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    private bool _isBusy;

    public bool CanAdd =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(NewUserName)
        && !string.IsNullOrWhiteSpace(NewPassword);

    public async Task LoadAsync()
    {
        if (!IsUnlocked)
        {
            return;
        }

        try
        {
            await _session.EnsureLoadedAsync(CancellationToken.None);

            var all = await _session.GetCashiersAsync(CancellationToken.None);
            var disabled = await _session.GetDisabledCashiersAsync(CancellationToken.None);

            Users.Clear();

            // Ordenados: primero los activos, y alfabético dentro de cada grupo. Los desactivados
            // al final porque son la excepción y no deben estorbar la lectura de la lista real.
            foreach (var user in all
                .Select(name => new ManagedUser(
                    name,
                    !disabled.Any(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(u => u.IsEnabled)
                .ThenBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Users.Add(user);
            }
        }
        catch (Exception exception)
        {
            Message = $"No se pudo leer la lista de usuarios. {exception.Message}";
        }

        OnPropertyChanged(nameof(HasUsers));
    }

    /// <summary>
    /// Alta o cambio de contraseña: si el usuario ya existe, se le reemplaza la contraseña. Es el
    /// mismo gesto desde el punto de vista del administrador ("dejar a fulano con esta clave") y
    /// evita una pantalla aparte para recuperar un acceso perdido, que es el caso más frecuente.
    /// </summary>
    [RelayCommand]
    private async Task AddOrUpdateAsync()
    {
        var user = NewUserName.Trim();
        var password = NewPassword.Trim();

        if (user.Length == 0 || password.Length == 0)
        {
            Message = "Escribe el usuario y su contraseña.";
            return;
        }

        if (password.Length < 4)
        {
            Message = "La contraseña debe tener al menos 4 caracteres.";
            return;
        }

        if (!_session.IsConfigured)
        {
            Message = "Configura primero la tienda antes de registrar usuarios.";
            return;
        }

        IsBusy = true;
        Message = string.Empty;

        try
        {
            var existed = Users.Any(u => string.Equals(u.Name, user, StringComparison.OrdinalIgnoreCase));

            await _session.RegisterCashierAsync(user, CancellationToken.None);
            await _session.SetCashierPasswordAsync(user, password);

            NewUserName = string.Empty;
            NewPassword = string.Empty;
            Notice = existed
                ? $"Se actualizó la contraseña de {user}."
                : $"{user} quedó registrado y ya puede entrar.";

            await LoadAsync();
        }
        catch (Exception exception)
        {
            Message = $"No se pudo guardar. {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Prepara el cambio de contraseña: pone el usuario en el formulario de arriba y deja el foco
    /// en la contraseña.
    /// <para>
    /// No abre un diálogo aparte a propósito. El formulario ya valida el mínimo de caracteres y
    /// tiene el campo enmascarado; un <c>DisplayPrompt</c> mostraría la clave en claro sobre el
    /// mostrador y duplicaría la validación en dos sitios.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void ChangePassword(ManagedUser? user)
    {
        if (user is null)
        {
            return;
        }

        NewUserName = user.Name;
        NewPassword = string.Empty;
        Message = string.Empty;
        Notice = $"Escribe la nueva contraseña de {user.Name} y toca Guardar usuario.";

        PasswordChangeRequested?.Invoke();
    }

    /// <summary>La vista lo usa para llevar el foco al campo de contraseña.</summary>
    public event Action? PasswordChangeRequested;

    /// <summary>
    /// Activa o desactiva. Un usuario desactivado conserva su contraseña y su historial pero no
    /// puede entrar — es lo que se usa en vacaciones o traslados, donde borrarlo sería perder el
    /// rastro de lo que hizo.
    /// </summary>
    [RelayCommand]
    private async Task ToggleEnabledAsync(ManagedUser? user)
    {
        if (user is null)
        {
            return;
        }

        var enabling = !user.IsEnabled;

        // Desactivar al último activo deja la caja igual de bloqueada que borrarlo, solo que de
        // una forma menos evidente para quien lo hace.
        if (!enabling && EnabledCount <= 1)
        {
            Message = "No puedes desactivar al último usuario activo: nadie podría entrar a la caja.";
            return;
        }

        IsBusy = true;
        Message = string.Empty;

        try
        {
            await _session.SetCashierEnabledAsync(user.Name, enabling, CancellationToken.None);

            Notice = enabling
                ? $"{user.Name} vuelve a poder entrar."
                : $"{user.Name} quedó desactivado y ya no puede entrar.";

            await LoadAsync();
        }
        catch (Exception exception)
        {
            Message = $"No se pudo cambiar el estado. {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Baja de un usuario. La confirmación la pide la pantalla ANTES de llamar acá: borrar un
    /// cajero de un toque, en una tablet de mostrador, se hace solo.
    /// </summary>
    [RelayCommand]
    private async Task RemoveAsync(string? user)
    {
        var id = (user ?? string.Empty).Trim();

        if (id.Length == 0)
        {
            return;
        }

        // No se permite quedarse sin ninguno: sin usuarios, nadie puede entrar a la caja y
        // recuperarla exige volver a pasar por Configuración.
        if (Users.Count <= 1)
        {
            Message = "No puedes eliminar al último usuario: la caja quedaría sin nadie que pueda entrar.";
            return;
        }

        IsBusy = true;
        Message = string.Empty;

        try
        {
            await _session.RemoveCashierAsync(id, CancellationToken.None);
            Notice = $"Se eliminó a {id}.";
            await LoadAsync();
        }
        catch (Exception exception)
        {
            Message = $"No se pudo eliminar. {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>
/// Un usuario de la caja tal como lo ve el administrador: su nombre y si puede entrar hoy.
/// <para>
/// Es inmutable: la lista se reconstruye entera después de cada cambio en vez de mutar filas.
/// Con listas de este tamaño —cinco o seis cajeros por caja— recargar es instantáneo, y evita el
/// error clásico de que la pantalla muestre un estado que el almacenamiento no llegó a guardar.
/// </para>
/// </summary>
/// <param name="Name">Usuario con el que inicia sesión.</param>
/// <param name="IsEnabled">Puede entrar. Un desactivado conserva contraseña e historial.</param>
public sealed record ManagedUser(string Name, bool IsEnabled)
{
    public string StatusLabel => IsEnabled ? "ACTIVO" : "INACTIVO";

    /// <summary>Texto del botón: dice qué VA A PASAR, no en qué estado está.</summary>
    public string ToggleLabel => IsEnabled ? "Desactivar" : "Activar";

    /// <summary>Iniciales para el avatar, con el mismo criterio del chip de la barra superior.</summary>
    public string Initials
    {
        get
        {
            var name = Name.Trim();

            if (name.Length == 0)
            {
                return "··";
            }

            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return words.Length >= 2
                ? $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}"
                : name[..Math.Min(2, name.Length)].ToUpperInvariant();
        }
    }
}
