namespace Permoda.Pay.Maui.Views;

/// <summary>
/// Antesala del Shell: ocupa el instante entre que la app arranca y que se resuelve a dónde va
/// el cajero.
/// <para>
/// Es el PRIMER item del Shell a propósito. Antes lo era Inicio, y como el destino real solo se
/// conoce en runtime —depende de si el cobro lo ordenó HiPOS, de si la terminal está configurada
/// y de si hay cajeros dados de alta—, el menú alcanzaba a dibujarse y desaparecer antes de
/// llegar a la pantalla correcta. Eso era la "pestaña falsa".
/// </para>
/// <para>
/// Esta página no decide nada ni consulta nada: solo se ve bien mientras
/// <c>MainActivity.OnResume</c> resuelve el destino y navega.
/// </para>
/// </summary>
public partial class RouterPage : ContentPage
{
    /// <summary>
    /// A partir de acá la espera deja de ser instantánea y conviene mostrar que algo pasa.
    /// Por debajo de este umbral, anunciar la carga se siente MÁS lento que no anunciarla.
    /// </summary>
    private const int ProgressHintAfterMs = 700;

    private CancellationTokenSource? _pulse;

    public RouterPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Entrada corta y contenida. El logo sube y aparece; el texto lo sigue. Es lo único que
        // el cajero ve si el arranque es rápido, así que tiene que verse deliberado y no como
        // una pantalla a medio dibujar.
        Mark.Opacity = 0;
        Mark.Scale = 0.94;
        Wordmark.Opacity = 0;
        Pulse.Opacity = 0;

        _pulse?.Cancel();
        _pulse = new CancellationTokenSource();
        var token = _pulse.Token;

        _ = ShowProgressIfSlowAsync(token);

        try
        {
            await Task.WhenAll(
                Mark.FadeTo(1, 220, Easing.CubicOut),
                Mark.ScaleTo(1, 260, Easing.CubicOut));

            await Wordmark.FadeTo(1, 200, Easing.CubicOut);
        }
        catch (Exception)
        {
            // La página puede desaparecer a mitad de la animación —que es justamente el caso
            // bueno: el destino se resolvió rápido—. No hay nada que reportar.
            Mark.Opacity = 1;
            Wordmark.Opacity = 1;
        }
    }

    protected override void OnDisappearing()
    {
        _pulse?.Cancel();
        _pulse = null;
        base.OnDisappearing();
    }

    /// <summary>
    /// Muestra la barra solo si la espera se alarga, y la deja latiendo. Un indicador que
    /// aparece siempre convierte un salto instantáneo en una espera anunciada.
    /// </summary>
    private async Task ShowProgressIfSlowAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ProgressHintAfterMs, token);

            await Pulse.FadeTo(1, 180, Easing.CubicOut);

            while (!token.IsCancellationRequested)
            {
                await Pulse.FadeTo(0.35, 620, Easing.SinInOut);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                await Pulse.FadeTo(1, 620, Easing.SinInOut);
            }
        }
        catch (TaskCanceledException)
        {
            // Se resolvió el destino antes del umbral: no hay que mostrar nada.
        }
        catch (Exception)
        {
            // La página se fue a mitad de la animación. Es el caso bueno.
        }
    }
}
