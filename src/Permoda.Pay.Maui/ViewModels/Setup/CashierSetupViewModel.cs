using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Permoda.Pay.Maui.Common;
using Permoda.Pay.Maui.Services;

namespace Permoda.Pay.Maui.ViewModels.Setup;

/// <summary>
/// Paso 3 del alta: el administrador registra los cajeros de la caja con su contraseña.
/// <para>
/// Es una pantalla aparte y no un bloque dentro de Configuración porque son dos tareas
/// distintas con dos momentos distintos: la identidad de la caja se define una vez, y los
/// cajeros entran y salen. Además los cajeros se guardan POR TIENDA, así que esto solo puede
/// existir DESPUÉS de guardar la tienda — mezclarlas en una sola pantalla permitía intentar
/// registrar cajeros contra una tienda que todavía no existía, y no se guardaba nada.
/// </para>
/// </summary>
public sealed partial class CashierSetupViewModel : ObservableObject
{
    private readonly PosSession _session;

    public CashierSetupViewModel(PosSession session) => _session = session;

    public ObservableCollection<string> Cashiers { get; } = new();

    public bool HasCashiers => Cashiers.Count > 0;

    public bool IsEmpty => !HasCashiers;

    /// <summary>Sin al menos un cajero la caja no puede operar, así que no se puede continuar.</summary>
    public bool CanContinue => HasCashiers && !NeedsStore;

    // Replicar se ofrece desde Configuración, no desde acá: esta pantalla da de alta PERSONAS y
    // lo que se replica es la identidad de la caja (tienda, llave, PIN).

    /// <summary>Falta el paso anterior: la tienda. Los cajeros se guardan por tienda.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRegister))]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    private bool _needsStore;

    public bool CanRegister => !NeedsStore;

    [RelayCommand]
    private async Task GoToStoreSetupAsync() => await Shell.Current.GoToAsync(AppRoutes.Setup);

    [ObservableProperty] private string _newCashierId = string.Empty;
    [ObservableProperty] private string _newCashierPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public async Task RefreshAsync()
    {
        await _session.EnsureLoadedAsync(CancellationToken.None);

        // Sin tienda no hay dónde guardar cajeros. NO se navega desde acá: esto corre en
        // OnAppearing, o sea en medio de una navegación, y pedirle a Shell que navegue ahí la
        // deja a medias — encabezado dibujado y cuerpo vacío. Se marca el estado y la pantalla
        // ofrece el botón.
        NeedsStore = !_session.IsConfigured;

        if (NeedsStore)
        {
            Cashiers.Clear();
            OnPropertyChanged(nameof(HasCashiers));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CanContinue));
            return;
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        Cashiers.Clear();

        foreach (var cashier in await _session.GetCashiersAsync(CancellationToken.None))
        {
            Cashiers.Add(cashier);
        }

        OnPropertyChanged(nameof(HasCashiers));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanContinue));
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var id = NewCashierId.Trim();

        if (string.IsNullOrWhiteSpace(id))
        {
            Message = "Escribe el nombre o código del cajero.";
            return;
        }

        if (NewCashierPassword.Trim().Length < 4)
        {
            Message = "La contraseña debe tener al menos 4 caracteres.";
            return;
        }

        await _session.RegisterCashierAsync(id, CancellationToken.None);
        await _session.SetCashierPasswordAsync(id, NewCashierPassword.Trim());

        NewCashierId = string.Empty;
        NewCashierPassword = string.Empty;

        await ReloadAsync();
        Message = $"{id} quedó registrado.";
    }

    /// <summary>
    /// Quita un cajero de la caja. No borra lo que ya operó: la bitácora guarda su nombre en
    /// cada movimiento, que es lo que sirve para auditar.
    /// </summary>
    [RelayCommand]
    private async Task RemoveAsync(string? cashierId)
    {
        if (string.IsNullOrWhiteSpace(cashierId))
        {
            return;
        }

        await _session.RemoveCashierAsync(cashierId, CancellationToken.None);
        await ReloadAsync();
        Message = $"{cashierId} fue eliminado.";
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        if (!CanContinue)
        {
            Message = "Registra al menos un cajero para poder continuar.";
            return;
        }

        await Shell.Current.GoToAsync(AppRoutes.CashierLogin);
    }
}
