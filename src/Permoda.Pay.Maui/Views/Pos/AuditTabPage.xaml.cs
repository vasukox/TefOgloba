using Android.Util;
using Permoda.Pay.Maui.ViewModels.Pos;

namespace Permoda.Pay.Maui.Views.Pos;

public partial class AuditTabPage : ContentPage
{
    private const string LogTag = "TefOgloba";

    private readonly AuditTabViewModel _viewModel;

    public AuditTabPage(AuditTabViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = SafeRefreshAsync();
    }

    private async Task SafeRefreshAsync()
    {
        try
        {
            await _viewModel.RefreshAsync();
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"[AuditTabPage] Refresh failed: {exception}");
        }
    }
}
