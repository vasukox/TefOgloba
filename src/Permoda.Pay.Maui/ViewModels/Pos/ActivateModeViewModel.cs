using CommunityToolkit.Mvvm.Input;
using Permoda.Pay.Maui.Common;

namespace Permoda.Pay.Maui.ViewModels.Pos;

/// <summary>
/// Paso previo a activar: qué clase de bono se va a activar, físico o virtual.
/// <para>
/// Antes esa elección era un segmentado dentro de la pantalla de activación, y arrastraba dos
/// problemas: la pantalla mostraba campos de los dos tipos (serial y correo comparten el mismo
/// campo, que cambia de significado según el modo) y el cajero podía cambiar de modo con un bono
/// ya capturado. Sacarla afuera deja la pantalla de activación dedicada a UN tipo, sin ambigüedad.
/// </para>
/// </summary>
public sealed partial class ActivateModeViewModel(
    ActivateTabViewModel activate,
    Configuration.AppEnvironment environment)
{
    /// <summary>
    /// El bono digital se puede emitir siempre: va por <c>/activation</c> con el gencode digital,
    /// el mismo endpoint que el físico. Lo único que cambia entre los dos es el gencode.
    /// </summary>
    public bool CanActivateVirtual => true;

    /// <summary>
    /// Cómo se le entrega el bono al cliente. Es siempre lo mismo: lo envía Ogloba.
    /// <para>
    /// El módulo NO entrega bonos digitales en papel. Su trabajo termina al emitirlo; la entrega
    /// al cliente es de Ogloba, igual en todos los ambientes.
    /// </para>
    /// </summary>
    public string VirtualDeliveryText =>
        "Ogloba lo envía al correo del cliente. Se captura su dirección.";

    [RelayCommand]
    private async Task ChooseAsync(string? mode)
    {

        var selected = string.Equals(mode, "virtual", StringComparison.OrdinalIgnoreCase)
            ? ActivationFormMode.Virtual
            : ActivationFormMode.Physical;

        // El modo se fija ANTES de navegar: la pantalla de activación se dibuja ya sabiendo qué
        // capturar, sin un parpadeo del formulario equivocado.
        activate.StartWithMode(selected);

        await Shell.Current.GoToAsync(AppRoutes.Activate);
    }

    [RelayCommand]
    private async Task BackToMenuAsync() => await Shell.Current.GoToAsync(AppRoutes.Home);
}
