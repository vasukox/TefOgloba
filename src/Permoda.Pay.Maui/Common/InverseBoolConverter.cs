using System.Globalization;

namespace Permoda.Pay.Maui.Common;

/// <summary>
/// Invierte un valor booleano para bindings como IsEnabled. Permite escribir
/// <c>IsEnabled="{Binding IsBusy, Converter={StaticResource InverseBool}}"</c> sin
/// agregar una propiedad IsNotBusy redundante en cada ViewModel.
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }

        return true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }

        return false;
    }
}
