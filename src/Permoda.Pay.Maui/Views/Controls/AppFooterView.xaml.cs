using System.Windows.Input;

namespace Permoda.Pay.Maui.Views.Controls;

/// <summary>
/// Pie común de las pantallas operativas: acceso a la configuración de la terminal,
/// identificación (versión · caja · tienda) y estado de sincronización con Ogloba.
/// </summary>
public partial class AppFooterView : ContentView
{
    public static readonly BindableProperty SetupCommandProperty = BindableProperty.Create(
        nameof(SetupCommand), typeof(ICommand), typeof(AppFooterView));

    public AppFooterView()
    {
        InitializeComponent();
    }

    /// <summary>Comando que abre la pantalla de configuración de la terminal.</summary>
    public ICommand? SetupCommand
    {
        get => (ICommand?)GetValue(SetupCommandProperty);
        set => SetValue(SetupCommandProperty, value);
    }
}