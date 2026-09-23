using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Permoda.Pay.Maui.Common;
using Permoda.Pay.Maui.Services;

namespace Permoda.Pay.Maui.ViewModels;

public sealed partial class AdminLogExportViewModel : ObservableObject
{
    private readonly InMemoryTransactionAudit _audit;
    private readonly InMemoryOglobaTrafficLog _traffic;
    private readonly UatLogExporter _exporter;

    public AdminLogExportViewModel(
        InMemoryTransactionAudit audit,
        InMemoryOglobaTrafficLog traffic,
        UatLogExporter exporter)
    {
        _audit = audit;
        _traffic = traffic;
        _exporter = exporter;
        EntriesCountLabel = $"Audit: {_audit.Entries.Count} · Tráfico: {_traffic.Entries.Count}";
    }

    [ObservableProperty]
    private string _statusMessage = "Listo para exportar";

    [ObservableProperty]
    private string _exportedJson = string.Empty;

    [ObservableProperty]
    private string _entriesCountLabel = string.Empty;

    [RelayCommand]
    private void Export()
    {
        ExportedJson = _exporter.Export(_audit.Entries, _traffic.Entries);
        EntriesCountLabel = $"Audit: {_audit.Entries.Count} · Tráfico: {_traffic.Entries.Count}";
        StatusMessage = "Exportación generada. Copie el JSON y péguelo en el XLSX UAT.";
    }

    [RelayCommand]
    private void Clear()
    {
        _audit.Clear();
        _traffic.Clear();
        ExportedJson = string.Empty;
        EntriesCountLabel = "Audit: 0 · Tráfico: 0";
        StatusMessage = "Bitácora y log de tráfico vaciados.";
    }
}