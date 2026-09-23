using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Permoda.Pay.Application.Abstractions.Payments;
using Permoda.Pay.Maui.Services;

namespace Permoda.Pay.Maui.ViewModels.Pos;

public sealed partial class AuditTabViewModel : ObservableObject
{
    private readonly CashierActivityLog _activityLog;
    private readonly PosSession _session;
    private readonly IGiftCardProvider _provider;

    public AuditTabViewModel(
        CashierActivityLog activityLog,
        PosSession session,
        IGiftCardProvider provider)
    {
        _activityLog = activityLog;
        _session = session;
        _provider = provider;
    }

    public ObservableCollection<CashierActivityEntry> Entries { get; } = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Última validación fallida o mensaje informativo. Se pinta inline en la página,
    /// sin toasts. El cajero la ve al lado del botón que disparó la acción.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastError))]
    private string _lastError = string.Empty;

    public bool HasLastError => !string.IsNullOrWhiteSpace(LastError);

    public Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private async Task RefreshList() => await LoadAsync();

    private async Task LoadAsync()
    {
        Entries.Clear();

        foreach (var entry in await _activityLog.GetAllAsync(CancellationToken.None))
        {
            Entries.Add(entry);
        }

        StatusMessage = Entries.Count == 0
            ? "Sin actividad registrada."
            : $"{Entries.Count} operaciones registradas.";
    }

    [RelayCommand]
    private async Task Clear()
    {
        await _activityLog.ClearAsync(CancellationToken.None);
        await LoadAsync();
        StatusMessage = "Bitácora local vaciada.";
    }

    // Botón para consultar el historial real de Ogloba (paginado
    // por pageNo/numberOfPage). Hoy la Bitácora solo muestra lo local; este botón trae lo
    // que pasó en Ogloba entre fechas.
    [RelayCommand]
    private async Task QueryOglobaHistoryAsync()
    {
        try
        {
            await _session.EnsureLoadedAsync(CancellationToken.None);
            if (_session.Configuration is null)
            {
                LastError = "Configura la terminal antes de consultar Ogloba.";
                return;
            }

            var storeIdResult = Permoda.Pay.Domain.Payments.StoreId.Create(_session.Configuration.StoreId);
            if (storeIdResult.IsFailure)
            {
                LastError = $"StoreId inválido: {storeIdResult.Error.Description}";
                return;
            }

            // Para v1: solo consultamos la primera página (10 últimas). La UI completa de
            // paginación se puede agregar después con un control Picker de página.
            var result = await _provider.QueryTransactionsHistoryAsync(
                storeIdResult.Value,
                transDateFrom: null,
                transDateTo: null,
                pageNo: 1,
                numberOfPage: 10,
                cancellationToken: CancellationToken.None);

            if (result.IsFailure)
            {
                LastError = $"Error consultando Ogloba: {result.Failure.Code} · {result.Failure.Description}";
                return;
            }

            StatusMessage = $"Ogloba página 1: {result.Value.Count} transacciones. Detalle completo en AdminLogExport.";
        }
        catch (Exception ex)
        {
            LastError = $"Error inesperado: {ex.Message}";
        }
    }
}
