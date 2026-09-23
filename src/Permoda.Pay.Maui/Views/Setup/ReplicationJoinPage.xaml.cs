using Permoda.Pay.Maui.ViewModels.Setup;

namespace Permoda.Pay.Maui.Views.Setup;

public partial class ReplicationJoinPage : ContentPage
{
    public ReplicationJoinPage(ReplicationJoinViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
