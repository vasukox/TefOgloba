using Permoda.Pay.Maui.ViewModels.Pos;

namespace Permoda.Pay.Maui.Views.Pos;

/// <summary>
/// Elección del tipo de bono antes de activar. Ver <see cref="ActivateModeViewModel"/> para por
/// qué esta decisión salió del formulario de activación.
/// </summary>
public partial class ActivateModePage : ContentPage
{
    public ActivateModePage(ActivateModeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
