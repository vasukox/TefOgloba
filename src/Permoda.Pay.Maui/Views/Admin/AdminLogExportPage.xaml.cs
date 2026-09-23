using Permoda.Pay.Maui.ViewModels;

namespace Permoda.Pay.Maui.Views.Admin;

public partial class AdminLogExportPage : ContentPage
{
    public AdminLogExportPage(AdminLogExportViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
