using Android.Util;
using Permoda.Pay.Maui.ViewModels.Setup;

namespace Permoda.Pay.Maui.Views.Setup;

public partial class SetupPage : ContentPage
{
    private const string LogTag = "TefOgloba";

    private readonly SetupViewModel _viewModel;

    public SetupPage(SetupViewModel viewModel)
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
            Log.Error(LogTag, $"[SetupPage] Refresh failed: {exception}");
        }
    }
}
