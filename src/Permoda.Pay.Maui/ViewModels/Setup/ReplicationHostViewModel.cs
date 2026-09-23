using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Permoda.Pay.Application.Abstractions.Time;
using Permoda.Pay.Application.Features.Provisioning;
using Permoda.Pay.Maui.Services;

namespace Permoda.Pay.Maui.ViewModels.Setup;

/// <summary>
/// La caja YA configurada repartiendo su configuración a las demás de la tienda.
/// <para>
/// El servicio vive exactamente lo que dura esta pantalla: se enciende al entrar y se apaga al
/// salir. No arranca con la app ni sobrevive a cerrarla — eso sería una caja ofreciendo la llave
/// de producción de la tienda en la red todo el día, y casi siempre sin nadie mirando.
/// </para>
/// </summary>
public sealed partial class ReplicationHostViewModel : ObservableObject
{
    private readonly PosSession _session;
    private readonly IClock _clock;

    private PairingHost? _host;
    private CancellationTokenSource? _lifetime;

    public ReplicationHostViewModel(PosSession session, IClock clock)
    {
        _session = session;
        _clock = clock;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCode))]
    private string _code = string.Empty;

    public bool HasCode => Code.Length > 0;

    /// <summary>
    /// El código separado en dos tercios ("427 913"). Seis dígitos corridos se leen mal a un metro
    /// de distancia, que es justo la distancia a la que va a estar el que teclea en la otra caja.
    /// </summary>
    public string CodeDisplay =>
        Code.Length == 6 ? $"{Code[..3]} {Code[3..]}" : Code;

    [ObservableProperty]
    private string _timeRemaining = string.Empty;

    [ObservableProperty]
    private string _transfers = "Ninguna caja todavía";

    /// <summary>
    /// La IP de esta caja. Se muestra SIEMPRE, no solo cuando algo falla: el descubrimiento
    /// automático va por difusión UDP y la difusión puede estar bloqueada en la red de la tienda
    /// aunque las cajas se vean entre sí. Tenerla a la vista convierte un callejón sin salida en
    /// un dato que se teclea.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddress))]
    private string _localAddress = string.Empty;

    public bool HasAddress => LocalAddress.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    [ObservableProperty]
    private bool _isRunning;

    public async Task StartAsync()
    {
        await StopAsync();

        // Se exporta una vez acá solo para comprobar que la caja está en condiciones de repartir.
        // El sobre de verdad se arma en cada entrega (ver la fábrica de abajo).
        var probe = await _session.ExportForReplicationAsync(CancellationToken.None);

        if (probe is null)
        {
            Message =
                "Esta caja todavía no puede compartir su configuración. Revisa que estén la "
                + "tienda, la llave y el PIN de administrador.";
            return;
        }

        try
        {
            // La fábrica se ejecuta en CADA entrega: entre la primera caja y la tercera el
            // administrador pudo dar de alta otro cajero, y la tercera tiene que recibir la lista
            // de verdad y no una foto de hace ocho minutos.
            _host = PairingHost.Start(
                async token => await _session.ExportForReplicationAsync(token) ?? probe,
                _clock);
        }
        catch (Exception exception)
        {
            // Lo normal acá es que el puerto esté ocupado porque ya hay otra ventana abierta.
            Message = $"No se pudo abrir la ventana de replicación. {exception.Message}";
            return;
        }

        Code = _host.Code;
        OnPropertyChanged(nameof(CodeDisplay));
        LocalAddress = PairingDiscovery.LocalAddress() ?? string.Empty;
        IsRunning = true;
        Message = string.Empty;

        _lifetime = new CancellationTokenSource();

        // Responder "¿quién reparte?" es un atajo para no teclear la IP. Si la red no deja
        // difundir, esto simplemente no contesta y la pantalla sigue mostrando la dirección.
        _ = PairingDiscovery.RespondAsync(probe.StoreId, _lifetime.Token);
        _ = CountdownAsync(_lifetime.Token);
    }

    /// <summary>
    /// Refresca el tiempo restante y el conteo de cajas una vez por segundo, y cierra sola la
    /// ventana cuando se vence — el operador tiene que ver que se cerró, no descubrirlo cuando la
    /// siguiente caja falle.
    /// </summary>
    private async Task CountdownAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_host is null)
                {
                    return;
                }

                var left = _host.TimeRemaining;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    TimeRemaining = $"{(int)left.TotalMinutes}:{left.Seconds:D2}";
                    Transfers = _host.SuccessfulTransfers switch
                    {
                        0 => "Ninguna caja todavía",
                        1 => "1 caja configurada",
                        var n => $"{n} cajas configuradas"
                    };

                    if (_host.AttemptsRemaining < PairingSecret.MaxAttempts)
                    {
                        Message = _host.AttemptsRemaining == 0
                            ? "Se agotaron los intentos. Genera un código nuevo."
                            : $"Código equivocado en otra caja. Quedan {_host.AttemptsRemaining} intentos.";
                    }
                });

                if (left == TimeSpan.Zero || _host.AttemptsRemaining == 0)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        var expired = left == TimeSpan.Zero;
                        await StopAsync();
                        Message = expired
                            ? "La ventana se cerró. Genera un código nuevo si falta alguna caja."
                            : "Se agotaron los intentos. Genera un código nuevo.";
                    });

                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Se salió de la pantalla.
        }
    }

    /// <summary>Un código nuevo: ventana nueva, intentos nuevos.</summary>
    [RelayCommand]
    private async Task RegenerateAsync() => await StartAsync();

    /// <summary>
    /// Apaga el servicio. Se llama al salir de la pantalla y al vencer el plazo, y tiene que
    /// poder llamarse dos veces sin quejarse porque las dos cosas pueden pasar casi a la vez.
    /// </summary>
    public async Task StopAsync()
    {
        if (_lifetime is not null)
        {
            await _lifetime.CancelAsync();
            _lifetime.Dispose();
            _lifetime = null;
        }

        if (_host is not null)
        {
            await _host.DisposeAsync();
            _host = null;
        }

        IsRunning = false;
        Code = string.Empty;
        OnPropertyChanged(nameof(CodeDisplay));
        TimeRemaining = string.Empty;
    }
}
