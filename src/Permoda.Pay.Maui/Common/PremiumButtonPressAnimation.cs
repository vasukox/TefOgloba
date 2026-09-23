using System.Runtime.CompilerServices;
using Microsoft.Maui.Handlers;

namespace Permoda.Pay.Maui.Common;

/// <summary>
/// Feedback táctil animado (en vez del salto instantáneo que aplicaba el VisualStateManager)
/// para TODO <see cref="Button"/> de la app, sin tener que enganchar un behavior página por
/// página. Se registra una sola vez desde <c>MauiProgram.CreateMauiApp</c> vía
/// <c>ConfigureMauiHandlers</c>: el mapper corre cada vez que MAUI conecta el handler nativo
/// de un botón, así que usamos <see cref="ConditionalWeakTable{TKey,TValue}"/> para no
/// suscribirnos dos veces al mismo botón si MAUI reconecta el handler (rotación de pantalla,
/// recarga de página).
/// </summary>
public static class PremiumButtonPressAnimation
{
    private static readonly ConditionalWeakTable<Button, object?> Attached = new();

    public static void Register()
    {
        ButtonHandler.Mapper.AppendToMapping("PremiumPressAnimation", (_, view) =>
        {
            if (view is Button button)
            {
                Attach(button);
            }
        });
    }

    private static void Attach(Button button)
    {
        if (Attached.TryGetValue(button, out _))
        {
            return;
        }

        Attached.Add(button, null);

        // Bajada rápida (feedback inmediato, 90ms) y subida un poco más lenta (140ms) con
        // desaceleración natural — igual que el resto de los behaviors de la app (animate.md:
        // "natural deceleration... do not use bounce or elastic curves by reflex").
        button.Pressed += async (_, _) => await button.ScaleToAsync(0.96, 90, Easing.CubicOut);
        button.Released += async (_, _) => await button.ScaleToAsync(1.0, 140, Easing.CubicOut);
    }
}
