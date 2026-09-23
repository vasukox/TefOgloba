using System.Windows.Input;

namespace Permoda.Pay.Maui.Views.Controls;

/// <summary>
/// Chip compacto que muestra el cajero en turno y abre el flujo de "Cambiar cajero"
/// cuando se toca. Se mantiene siempre visible (en cada pestaña operativa) como
/// referencia persistente de quién está operando la caja.
/// </summary>
public partial class CashierChip : ContentView
{
    public static readonly BindableProperty CashierNameProperty = BindableProperty.Create(
        nameof(CashierName), typeof(string), typeof(CashierChip), "—");

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(CashierChip));

    public CashierChip()
    {
        InitializeComponent();

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (Command?.CanExecute(null) == true)
            {
                Command.Execute(null);
            }
        };
        Root.GestureRecognizers.Add(tap);
    }

    public string CashierName
    {
        get => (string)GetValue(CashierNameProperty);
        set => SetValue(CashierNameProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public string Initials
    {
        get
        {
            var name = (CashierName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
            {
                return "·";
            }

            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
            }

            return parts[0].Length >= 2
                ? $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[0][1])}"
                : char.ToUpperInvariant(parts[0][0]).ToString();
        }
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(CashierName))
        {
            OnPropertyChanged(nameof(Initials));
        }
    }
}