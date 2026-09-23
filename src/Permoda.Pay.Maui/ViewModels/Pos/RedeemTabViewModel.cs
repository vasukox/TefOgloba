using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Permoda.Pay.Application.Features.Sales;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Maui.Services;
using Permoda.Pay.Maui.ViewModels;

namespace Permoda.Pay.Maui.ViewModels.Pos;

public sealed partial class RedeemTabViewModel : PosViewModelBase
{
    private readonly ProcessSaleHandler _handler;
    private readonly PosSession _session;
    private readonly CashierActivityLog _activityLog;

    public RedeemTabViewModel(
        ProcessSaleHandler handler,
        PosSession session,
        PosHeaderViewModel header,
        CashierActivityLog activityLog)
    {
        _handler = handler;
        _session = session;
        Header = header;
        _activityLog = activityLog;

        Queue.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasQueueItems));
            OnPropertyChanged(nameof(ProcessButtonText));
            OnPropertyChanged(nameof(CanProcessQueue));
            OnPropertyChanged(nameof(QueueCountText));
            OnPropertyChanged(nameof(TotalSummary));
            NotifyCoverageChanged();
        };

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsBusy))
            {
                OnPropertyChanged(nameof(CanProcessQueue));
            }
        };

        // Un cobro nuevo deja esta pantalla en cero, SIN depender de que la página se recree.
        // Este ViewModel es singleton y la pantalla no se destruye entre cobros, así que sin esto
        // el cobro siguiente heredaba el importe y los bonos del anterior. Ver HiPosFlow.FlowStarted.
        HiPosFlow.FlowStarted += OnHiPosFlowStarted;
    }

    /// <summary>
    /// Vuelve la pantalla a cero para el cobro que acaba de empezar.
    /// <para>
    /// El disparador es el COBRO, no <c>OnAppearing</c>: al terminar una redención la pantalla se
    /// manda al fondo pero no se destruye, así que en el cobro siguiente el Shell ya está en
    /// Redimir y navegar ahí no vuelve a dispararlo.
    /// </para>
    /// <para>
    /// Lo que más importa de este método es el <c>Queue.Clear()</c>. Un texto desactualizado se ve;
    /// un bono del cobro anterior que sigue en la lista NO se ve, y desvirtúa el cálculo de cuánto
    /// le falta pagar al cliente — en un pago combinado, que es justo donde apareció.
    /// </para>
    /// </summary>
    private void OnHiPosFlowStarted() =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                // La lista primero: es lo que mueve plata.
                Queue.Clear();

                AmountText = string.Empty;
                ScannedBarcode = string.Empty;
                LastError = string.Empty;
                LastResult = PosResult.None;
                _lastLookedUpSerial = null;

                // Una consulta de saldo en vuelo del cobro anterior llegaría tarde y pisaría la
                // pantalla del cobro nuevo con el saldo de otro bono.
                _amountLookup?.Cancel();
                _amountLookup = null;

                // El panel de resultado del cobro anterior tapa la pantalla con su velo y deja el
                // foco fuera del campo del serial: el primer escaneo del cobro nuevo caía al vacío.
                ResultLines.Clear();
                ResultHeadline = string.Empty;
                ResultSubline = string.Empty;

                CardBalanceAmount = string.Empty;
                CardOutcomeText = string.Empty;
                CardCoversSale = true;

                SaleAmountBanner =
                    HiPosFlow.Current is { Intent: HiPos.HiPosSaleIntent.Redemption } flow
                        ? $"Valor de la venta: {FormatPesos(flow.AmountPesos)}"
                        : string.Empty;

                OnPropertyChanged(nameof(IsAmountReadOnly));
                NotifyCoverageChanged();
            }
            catch (Exception exception)
            {
#if ANDROID
                Android.Util.Log.Error(
                    "TefOgloba", $"[RedeemTabViewModel] No se pudo reiniciar para el cobro: {exception}");
#endif
            }
        });

    public PosHeaderViewModel Header { get; }



    public event Action? ResultProduced;

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private string _scannedBarcode = string.Empty;

    [ObservableProperty]
    private PosResult _lastResult = PosResult.None;

    /// <summary>
    /// Última validación fallida del draft. Se pinta inline debajo del check como una
    /// línea de error (sin toast). Se limpia automáticamente en cuanto el cajero
    /// retoca el serial o el monto.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastError))]
    private string _lastError = string.Empty;

    public bool HasLastError => !string.IsNullOrWhiteSpace(LastError);

    partial void OnScannedBarcodeChanged(string value)
    {
        LastError = string.Empty;
        ScheduleAmountLookup(value);
    }

    /// <summary>Cancela la consulta de saldo pendiente cuando el serial sigue cambiando.</summary>
    private CancellationTokenSource? _amountLookup;

    /// <summary>Último serial que ya se consultó, para no pedirle a Ogloba lo mismo dos veces.</summary>
    private string? _lastLookedUpSerial;

    /// <summary>
    /// Dispara la consulta del saldo apenas entra el serial, sin esperar al Enter.
    /// <para>
    /// El lector wedge termina cada escaneo con Enter, pero no siempre: con tarjetas físicas se
    /// ha visto llegar el serial sin el Enter final. Atado solo al Enter, el monto se quedaba en
    /// blanco y el cajero no tenía forma de saber si el bono se leyó. Con el rebote de 350 ms se
    /// consulta cuando el serial deja de crecer —el lector escribe de corrido, así que el rebote
    /// no se cumple a mitad del escaneo— y también sirve si el cajero lo digita.
    /// </para>
    /// </summary>
    private void ScheduleAmountLookup(string? value)
    {
        // Solo en cobros de HiPOS: en una redención manual el monto lo pone el cajero y
        // pisárselo con el saldo sería quitarle la decisión.
        if (!IsAmountReadOnly)
        {
            return;
        }

        _amountLookup?.Cancel();
        _amountLookup?.Dispose();
        _amountLookup = null;

        var serial = (value ?? string.Empty).Trim();

        if (serial.Length == 0)
        {
            _lastLookedUpSerial = null;
            AmountText = string.Empty;
            ClearCardInfo();
            return;
        }

        // Un serial a medio escanear no se consulta: sería un rechazo por cada carácter.
        if (serial.Length < 10 || string.Equals(serial, _lastLookedUpSerial, StringComparison.Ordinal))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _amountLookup = cts;

        _ = LookupAfterPauseAsync(serial, cts.Token);
    }

    private async Task LookupAfterPauseAsync(string serial, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(350, cancellationToken);

            var card = BuildCard();

            if (card is null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _lastLookedUpSerial = serial;
            await LoadAmountFromCardAsync(card);
        }
        catch (OperationCanceledException)
        {
            // Llegó otro carácter: la consulta de este serial ya no interesa.
        }
    }
    partial void OnAmountTextChanged(string value)
    {
        LastError = string.Empty;

        // El panel de cobertura cuenta el bono en pantalla, así que tiene que repintarse cuando
        // el monto cambia — no solo cuando se agrega uno a la lista.
        NotifyCoverageChanged();
    }

    // ----- Resumen del lote procesado -----

    /// <summary>
    /// Cierre de la redención: qué bono se cobró, por cuánto y con qué saldo quedó. Persiste
    /// hasta que el cajero lo cierra — es lo que le dice al cliente antes de entregarle el bono.
    /// </summary>
    public ObservableCollection<OperationResultLine> ResultLines { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string _resultHeadline = string.Empty;

    [ObservableProperty]
    private string _resultSubline = string.Empty;

    [ObservableProperty]
    private bool _resultIsSuccess = true;

    public bool HasResult => !string.IsNullOrWhiteSpace(ResultHeadline);

    [RelayCommand]
    private void DismissResult()
    {
        ResultLines.Clear();
        ResultHeadline = string.Empty;
        ResultSubline = string.Empty;

        // "Listo" es lo que cierra el cobro de HiPOS: hasta acá el cajero vio el resultado en
        // pantalla, y recién ahora el POS se entera y el módulo se va.
        if (_pendingHiPosOutcome is { } outcome)
        {
            _pendingHiPosOutcome = null;
            HiPosFlow.Complete(outcome);
            HiPosFlow.CloseModuleScreen();
        }
    }

    /// <summary>Carrito de bonos a redimir — misma mecánica que el de Activar.</summary>
    public ObservableCollection<PendingRedemptionItem> Queue { get; } = new();

    public bool HasQueueItems => Queue.Count > 0;

    public bool CanProcessQueue => HasQueueItems && !IsBusy;

    public string ProcessButtonText => Queue.Count switch
    {
        0 => "Redimir bonos",
        1 => "Redimir 1 bono",
        _ => $"Redimir {Queue.Count} bonos"
    };

    /// <summary>Encabezado del carrito: "Bonos en la lista · N".</summary>
    public string QueueCountText => $"Bonos en la lista · {Queue.Count}";

    /// <summary>Suma de lo que se va a cobrar en el lote, en pesos.</summary>
    public long TotalToRedeem => Queue.Sum(item => item.AmountPesos);

    /// <summary>Pie del lote: "Total a redimir: $45.000 · 1 bono".</summary>
    public string TotalSummary
    {
        get
        {
            var bonos = Queue.Count == 1 ? "1 bono" : $"{Queue.Count} bonos";
            return $"Total a redimir: {FormatPesos(TotalToRedeem)} · {bonos}";
        }
    }

    // ===== Cobertura de la venta =====
    //
    // Cuánto de la factura llevan cubierto los bonos de la lista y cuánto falta. Vive acá y no
    // en la tarjeta recién escaneada porque tiene que verse SIEMPRE: el dato del bono escaneado
    // desaparecía en cuanto se agregaba a la lista, y justo entonces —con el cliente esperando y
    // el cajero por procesar— es cuando hace falta saber cuánto queda por cobrar.

    /// <summary>Total de la factura que ordenó HiPOS. Cero en una redención manual.</summary>
    private long SaleTotalPesos =>
        HiPosFlow.Current is { Intent: HiPos.HiPosSaleIntent.Redemption } flow
            ? flow.AmountPesos
            : 0;

    /// <summary>El panel solo aplica cuando hay una factura contra la cual medir.</summary>
    public bool HasCoverage => SaleTotalPesos > 0;

    /// <summary>
    /// Lo que va a aportar el bono que está en pantalla y todavía NO se ha agregado a la lista.
    /// <para>
    /// Sin esto, entre escanear el bono y agregarlo a la lista la cifra grande mostraba el total
    /// de la venta SIN descontar nada —como si el bono no existiera— mientras el detalle de
    /// abajo ya decía bien cuánto faltaba. Dos números que se contradecían en la misma pantalla,
    /// con el cliente mirando, y el equivocado era el grande.
    /// </para>
    /// </summary>
    private long ScannedNotQueuedPesos =>
        Common.MoneyInput.TryParsePesos(AmountText, out var pesos) && pesos > 0 ? pesos : 0;

    /// <summary>Lo que aportan los bonos de la lista MÁS el que está en pantalla.</summary>
    private long AppliedPesos => TotalToRedeem + ScannedNotQueuedPesos;

    /// <summary>Lo que suman los bonos, topado al valor de la factura.</summary>
    private long CoveredPesos => Math.Min(AppliedPesos, SaleTotalPesos);

    /// <summary>Lo que le queda por pagar al cliente con otro medio.</summary>
    private long PendingPesos => Math.Max(0, SaleTotalPesos - AppliedPesos);

    public bool IsFullyCovered => HasCoverage && PendingPesos == 0;

    /// <summary>Fracción cubierta, para la barra de progreso. Entre 0 y 1.</summary>
    public double CoverageRatio =>
        SaleTotalPesos <= 0 ? 0 : Math.Clamp((double)CoveredPesos / SaleTotalPesos, 0, 1);

    /// <summary>
    /// Porcentaje cubierto, redondeado. Va junto a la cifra grande: el peso dice cuánto falta,
    /// el porcentaje cuánto se lleva — el cliente pregunta las dos cosas.
    /// </summary>
    public string CoveragePercentText =>
        HasCoverage ? $"{Math.Round(CoverageRatio * 100)}% cubierto" : string.Empty;

    public string SaleTotalText => FormatPesos(SaleTotalPesos);

    public string CoveredText => FormatPesos(CoveredPesos);

    /// <summary>La cifra grande: lo que falta, o el total cubierto cuando ya alcanza.</summary>
    public string CoverageAmountText =>
        IsFullyCovered ? FormatPesos(CoveredPesos) : FormatPesos(PendingPesos);

    /// <summary>Rótulo de la cifra grande. Dice qué es ese número, sin tecnicismos.</summary>
    public string CoverageLabel =>
        IsFullyCovered ? "LA VENTA QUEDA CUBIERTA" : "LE QUEDA POR PAGAR AL CLIENTE";

    /// <summary>Detalle de apoyo: de cuánto es la venta y cuánto ponen los bonos.</summary>
    public string CoverageDetailText
    {
        get
        {
            if (!HasCoverage)
            {
                return string.Empty;
            }

            // Se cuenta el bono en pantalla junto con los de la lista: para el cajero ya está
            // aportando, aunque técnicamente todavía no se haya agregado.
            var applied = Queue.Count + (ScannedNotQueuedPesos > 0 ? 1 : 0);
            var bonos = applied == 1 ? "1 bono" : $"{applied} bonos";

            return applied == 0
                ? $"Venta de {SaleTotalText}. Escanea un bono para empezar a cubrirla."
                : $"{bonos} cubren {CoveredText} de {SaleTotalText}.";
        }
    }

    private void NotifyCoverageChanged()
    {
        OnPropertyChanged(nameof(HasCoverage));
        OnPropertyChanged(nameof(IsFullyCovered));
        OnPropertyChanged(nameof(CoverageRatio));
        OnPropertyChanged(nameof(CoveragePercentText));
        OnPropertyChanged(nameof(SaleTotalText));
        OnPropertyChanged(nameof(CoveredText));
        OnPropertyChanged(nameof(CoverageAmountText));
        OnPropertyChanged(nameof(CoverageLabel));
        OnPropertyChanged(nameof(CoverageDetailText));
    }

    public async Task EnsureSessionAsync()
    {

        await _session.EnsureLoadedAsync(CancellationToken.None);
        Header.Refresh();

        // Cobro ordenado por HiPOS. El monto NO se precarga con el valor de la factura: se llena
        // solo, con el saldo del bono, en cuanto el cajero lo escanea (ver LoadAmountFromCardAsync).
        //
        // Precargarlo con el valor de la prenda era engañoso: mostraba una cifra que el bono
        // podía no tener, y el cajero se enteraba del faltante recién al procesar. Lo que hay que
        // ver antes de cobrar es cuánto va a poner el bono.
        if (HiPosFlow.Current is { Intent: HiPos.HiPosSaleIntent.Redemption } flow)
        {
            AmountText = string.Empty;
            SaleAmountBanner = $"Valor de la venta: {FormatPesos(flow.AmountPesos)}";
        }
        else
        {
            SaleAmountBanner = string.Empty;
        }

        OnPropertyChanged(nameof(IsAmountReadOnly));

        // El panel de cobertura depende del flujo de HiPOS, que recién queda disponible acá.
        NotifyCoverageChanged();
    }

    /// <summary>
    /// El monto no se digita cuando el cobro viene de HiPOS: lo determina el saldo del bono
    /// contra el valor de la factura, y dejarlo editable permitiría cobrar una cifra distinta a
    /// cualquiera de las dos. En una redención manual sí lo elige el cajero.
    /// </summary>
    public bool IsAmountReadOnly => HiPosFlow.Current is { Intent: HiPos.HiPosSaleIntent.Redemption };

    /// <summary>
    /// Saldo del bono recién escaneado, formateado y en grande.
    /// <para>
    /// Va aparte del monto a redimir a propósito. Son dos cifras distintas y confundirlas cuesta
    /// plata: cuando el bono vale más que la compra, lo que se redime es la compra y lo que el
    /// cliente pregunta es cuánto le queda. Antes esto vivía dentro de una nota de 13px que el
    /// cajero no leía, y por eso parecía que la app "no traía el valor del bono".
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCardInfo))]
    private string _cardBalanceAmount = string.Empty;

    public bool HasCardInfo => !string.IsNullOrWhiteSpace(CardBalanceAmount);

    /// <summary>
    /// La consecuencia, dicha completa: si falta plata por cobrar, o cuánto saldo le queda al
    /// cliente. Es lo que el cajero necesita ANTES de procesar — enterarse al final es tarde.
    /// </summary>
    [ObservableProperty]
    private string _cardOutcomeText = string.Empty;

    /// <summary>
    /// El bono cubre la venta completa. Falso cuando queda un resto por cobrar con otro medio:
    /// la pantalla lo pinta como advertencia, porque implica trabajo extra para el cajero.
    /// </summary>
    [ObservableProperty]
    private bool _cardCoversSale = true;

    private void ClearCardInfo()
    {
        CardBalanceAmount = string.Empty;
        CardOutcomeText = string.Empty;
        CardCoversSale = true;
    }

    /// <summary>
    /// Llena el monto a redimir con lo que el bono va a pagar de ESTA factura.
    /// <para>
    /// Es el saldo del bono TOPADO al valor de la venta. El campo no es editable: el cajero no
    /// elige cuánto se redime.
    /// </para>
    /// <para>
    /// El tope no es un detalle: sin él, un bono de $300.000 sobre una factura de $89.900 le
    /// descontaba al cliente los $300.000 completos y HiPOS cerraba la venta con un saldo NEGATIVO
    /// en otro medio de pago (visto en terminal el 2026-08-28). Un bono que vale más que la compra
    /// simplemente paga la compra y conserva el resto como saldo — eso es lo que el cliente espera
    /// y lo que el POS puede cuadrar.
    /// </para>
    /// </summary>
    private async Task LoadAmountFromCardAsync(CardIdentifier card)
    {
        if (HiPosFlow.Current is not { Intent: HiPos.HiPosSaleIntent.Redemption } flow)
        {
            // Redención manual: el monto lo decide el cajero, no se le impone.
            return;
        }

        SetBusy("Consultando el saldo del bono…");
        var result = await _session.GetBalanceAsync(card, cancellationToken: CancellationToken.None);
        SetIdle("Listo.");

        if (result.IsFailure)
        {
            ClearCardInfo();
            LastError = Common.OperationMessages
                .ForFailure(result.Failure.Code, result.Failure.Description).Text;
            return;
        }

        var balance = result.Value;

        if (!balance.IsActive)
        {
            ClearCardInfo();
            LastError = $"El bono está en estado {balance.Status} y no se puede redimir.";
            return;
        }

        if (balance.BalanceMinorUnits <= 0)
        {
            ClearCardInfo();
            LastError = "El bono no tiene saldo.";
            return;
        }

        // Nunca más de lo que vale la factura: cobrar de más deja el documento descuadrado.
        var cardBalance = balance.BalanceMinorUnits;
        var toRedeem = Math.Min(cardBalance, flow.AmountPesos);

        AmountText = Common.MoneyInput.Format(toRedeem.ToString());

        var missing = flow.AmountPesos - toRedeem;
        var leftOnCard = cardBalance - toRedeem;

        // El saldo del bono se muestra SIEMPRE y sin recortar, valga más o menos que la venta.
        // Es el dato que el cajero le repite al cliente.
        CardBalanceAmount = FormatPesos(cardBalance);
        CardCoversSale = missing <= 0;

        // Los tres casos, dichos en positivo y desde el cliente.
        //
        // El del bono corto decía "No alcanza (...) POR COBRAR con otro medio de pago", en
        // mayúsculas y pintado como advertencia. No es una advertencia: que un bono cubra parte
        // de la compra es lo normal, y el cajero solo necesita saber cuánto le queda por cobrar.
        // Redactado como alarma lo leía como un error del bono y llamaba a preguntar.
        CardOutcomeText = missing > 0
            ? $"Cubre {FormatPesos(toRedeem)} de la compra. Al cliente le quedan "
              + $"{FormatPesos(missing)} por pagar."
            : leftOnCard > 0
                ? $"Cubre la compra completa ({FormatPesos(toRedeem)}). Al cliente le quedan "
                  + $"{FormatPesos(leftOnCard)} de saldo en el bono."
                : "Cubre la compra exacta. El bono queda en cero.";
    }

    /// <summary>
    /// Aviso que solo aparece cuando el módulo lo levantó HiPOS: le recuerda al cajero por
    /// cuánto es la factura que está cobrando. Sin esto tendría que confiar en que el monto
    /// precargado es el correcto, sin nada que lo confirme en pantalla.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaleAmountBanner))]
    private string _saleAmountBanner = string.Empty;

    public bool HasSaleAmountBanner => !string.IsNullOrWhiteSpace(SaleAmountBanner);


    private CardIdentifier? BuildCard()
    {
        var raw = (ScannedBarcode ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Sin catálogo hardcodeado: el cajero decide si lo que tipeó es digital o físico
        // según el serial. Reglas: puro numérico de 13-14 dígitos = físico (113817...).
        // Cualquier otra cosa = digital (gencode 113815, etc.). El caller puede
        // sobreescribir el Kind vía el argumento opcional.
        var looksPhysical = raw.All(char.IsDigit) && raw.Length is >= 10 and <= 16;

        var result = looksPhysical
            ? CardIdentifier.CreatePhysicalCard(raw)
            : CardIdentifier.CreateDigitalGencode(raw);

        return result.IsSuccess ? result.Value : null;
    }

    [RelayCommand]
    private async Task CheckBalanceAsync()
    {

        await _session.EnsureLoadedAsync(CancellationToken.None);

        var card = BuildCard();
        if (card is null)
        {
            LastError = "Tarjeta requerida. Escanea o digita el serial del bono.";
            return;
        }

        SetBusy("Consultando saldo…");
        var result = await _session.GetBalanceAsync(card, cancellationToken: CancellationToken.None);
        SetIdle("Listo.");

        if (result.IsFailure)
        {
            LastError = Common.OperationMessages
                .ForFailure(result.Failure.Code, result.Failure.Description).Text;
            return;
        }
    }

    /// <summary>
    /// Handler del escaneo del lector de barras wedge de las tablets C9H. Llamado desde
    /// <c>OnBarcodeCompleted</c> en la vista cuando el Entry recibe un Enter (todos los
    /// lectores integrados emiten Enter al final del scan). Valida que el serial
    /// sea parseable como CardIdentifier y, si lo es, lo deja en <see cref="ScannedBarcode"/>
    /// para que el cajero confirme con "Agregar bono a la lista".
    /// </summary>
    public async Task HandleScannedBarcodeAsync()
    {
        var barcode = (ScannedBarcode ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(barcode))
        {
            return;
        }

        var looksPhysical = barcode.All(char.IsDigit) && barcode.Length is >= 10 and <= 16;
        var result = looksPhysical
            ? CardIdentifier.CreatePhysicalCard(barcode)
            : CardIdentifier.CreateDigitalGencode(barcode);

        if (result.IsFailure)
        {
            LastError = $"'{barcode}' no es un serial de bono válido. Verifica que esté completo (físico: 13-16 dígitos; digital: gencode del producto).";
            ScannedBarcode = string.Empty;
            ClearCardInfo();
            return;
        }

        // Escanear el bono es lo que resuelve cuánto se va a cobrar: el monto sale de su saldo.
        //
        // El Enter del lector llega ANTES de que venza el rebote de 350 ms, así que acá se cancela
        // el rebote y se marca el serial como ya consultado. Sin esto, cada escaneo con Enter
        // pedía el saldo DOS veces: una por este camino y otra por el rebote, que no sabía que ya
        // se había consultado.
        _amountLookup?.Cancel();
        _lastLookedUpSerial = barcode;

        await LoadAmountFromCardAsync(result.Value);
    }

    // "Agregar bono" NO valida saldo contra Ogloba (podría quedar obsoleto si otro ítem del
    // mismo lote redime la misma tarjeta antes) — solo empuja al carrito. El saldo se valida
    // en fresco justo antes de redimir cada ítem, dentro de ProcessQueueAsync.
    [RelayCommand]
    private void AddToQueue()
    {


        if (string.IsNullOrWhiteSpace(ScannedBarcode))
        {
            LastError = "Escanea o digita el serial del bono.";
            return;
        }

        if (!Common.MoneyInput.TryParsePesos(AmountText, out var pesos) || pesos <= 0)
        {
            LastError = "Ingresa un monto en pesos (COP).";
            return;
        }

        var card = BuildCard();
        if (card is null)
        {
            LastError = $"'{ScannedBarcode}' no es un serial de bono válido.";
            return;
        }

        // PendingRedemptionItem guarda un TestCard sintético solo con el serial (sin catálogo):
        // el flujo solo usa .Number y .Kind para mostrar y para llamar a Ogloba.
        var kind = card.Kind == CardIdentifierKind.PhysicalCard ? "Física" : "Digital (gencode)";
        var productCode = card.Kind == CardIdentifierKind.PhysicalCard ? "113817" : "113815";
        Queue.Add(new PendingRedemptionItem(new TestCard(card.Value, productCode, kind), pesos));
        ScannedBarcode = string.Empty;
        AmountText = string.Empty;
    }

    [RelayCommand]
    private void RemoveFromQueue(PendingRedemptionItem item) => Queue.Remove(item);

    /// <summary>Vacía el lote y los campos de captura sin tocar Ogloba.</summary>
    [RelayCommand]
    private void ClearQueue()
    {
        Queue.Clear();
        ScannedBarcode = string.Empty;
        AmountText = string.Empty;
        LastResult = PosResult.None;
    }

    /// <summary>
    /// Devuelve un ítem de la lista al formulario de captura para editarlo. El cajero
    /// retoca el serial o el monto y vuelve a marcarlo con el check ✓ para reenviarlo.
    /// </summary>
    [RelayCommand]
    private void ReturnRow(PendingRedemptionItem? item)
    {
        if (item is null || !item.IsRemovable)
        {
            return;
        }

        Queue.Remove(item);
        ScannedBarcode = item.Card.Number;
        AmountText = item.AmountPesos.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-CO"));
    }

    [RelayCommand]
    private async Task ProcessQueueAsync()
    {
        await _session.EnsureLoadedAsync(CancellationToken.None);
        Header.Refresh();

        if (!_session.IsConfigured)
        {
            LastError = "Terminal sin configurar. Ve al apartado Config.";
            return;
        }

        // Turno abierto O último cajero de la caja: redimir es lo que ordena HiPOS, y ahí no se
        // abre turno a propósito (el cajero ya se identificó en el POS). Exigir turno activo
        // rompía todo cobro que llegara después de una activación manual, que cierra el turno.
        if (!_session.HasOperatingCashier)
        {
            LastError = "Esta caja todavía no tiene un cajero registrado. "
                + "Abre el módulo Ogloba, selecciona tu cajero una vez y vuelve a cobrar.";
            return;
        }

        if (Queue.Count == 0)
        {
            return;
        }

        var configuration = _session.Configuration!;
        var cashier = _session.OperatingCashierId!;
        int approved = 0, rejected = 0;

        DismissResult();
        var lines = new List<OperationResultLine>();
        var redeemedTotal = 0L;

        // El pago parcial solo tiene sentido cuando HiPOS fijó el importe: ahí el bono es un
        // medio de pago más y el POS cobra la diferencia. En un cobro manual el cajero eligió
        // el monto a propósito, así que cobrar menos en silencio sería cobrar mal.
        var allowPartial = HiPosFlow.Current is { Intent: HiPos.HiPosSaleIntent.Redemption };
        ProcessSaleResult? lastApproved = null;
        string? lastError = null;

        foreach (var item in Queue.ToList())
        {
            item.Status = QueueItemStatus.Processing;

            var card = item.Card.Kind.Contains("igital", StringComparison.OrdinalIgnoreCase)
                ? CardIdentifier.CreateDigitalGencode(item.Card.Number)
                : CardIdentifier.CreatePhysicalCard(item.Card.Number);

            if (card.IsFailure)
            {
                item.Status = QueueItemStatus.Rejected;
                item.ResultDetail = "Tarjeta inválida.";
                rejected++;
                lines.Add(new OperationResultLine(item.Card.Number, "Tarjeta inválida.", OperationResultKind.Rejected));
                continue;
            }

            // Lo que se va a cobrar de ESTE bono. Arranca en lo pedido y baja si el bono no da
            // para tanto y el cobro admite pago parcial.
            var amountToRedeem = item.AmountPesos;

            // Techo duro del lote: entre TODOS los bonos no se puede cobrar más que la factura.
            // Un bono solo ya viene topado desde la captura, pero dos bonos en la lista sumaban
            // por encima del total y el POS cerraba la venta con un saldo negativo en otro medio.
            if (HiPosFlow.Current is { } capFlow)
            {
                var stillToCover = capFlow.AmountPesos - redeemedTotal;

                if (stillToCover <= 0)
                {
                    item.Status = QueueItemStatus.Rejected;
                    item.ResultDetail = "La venta ya quedó cubierta por los bonos anteriores.";
                    lines.Add(new OperationResultLine(
                        item.Card.Number,
                        "No se cobró: la venta ya estaba cubierta.",
                        OperationResultKind.Rejected));
                    continue;
                }

                amountToRedeem = Math.Min(amountToRedeem, stillToCover);
            }

            // Manual Tef Ogloba v4: validar saldo/vigencia ANTES de redimir, en fresco por ítem.
            SetBusy($"Validando saldo de {item.Subtitle}…");
            var balanceResult = await _session.GetBalanceAsync(card.Value, cancellationToken: CancellationToken.None);

            if (balanceResult.IsSuccess)
            {
                var balance = balanceResult.Value;

                if (!balance.IsActive)
                {
                    item.Status = QueueItemStatus.Rejected;
                    item.ResultDetail = $"La tarjeta está en estado {balance.Status}.";
                    rejected++;
                    lines.Add(new OperationResultLine(
                        item.Card.Number, $"La tarjeta está en estado {balance.Status}.", OperationResultKind.Rejected));
                    continue;
                }

                // Confirmado en vivo (2026-07-28): /balance responde en PESOS, igual que
                // /activation, /redemption y /orderCreation — sin ×100. Comparar tal cual.
                // Se compara contra amountToRedeem, que ya viene topado al saldo pendiente de la
                // factura. Comparar contra item.AmountPesos ignoraba ese tope y volvía a subir el
                // cobro al valor original del bono.
                if (amountToRedeem > balance.BalanceMinorUnits)
                {
                    if (allowPartial && balance.BalanceMinorUnits > 0)
                    {
                        // Pago parcial: se cobra lo que el bono tiene y HiPOS pide el resto por
                        // otro medio. Rechazar el bono entero porque no alcanza obligaría al
                        // cliente a no usarlo, cuando el saldo que tiene es plata suya.
                        amountToRedeem = balance.BalanceMinorUnits;
                        item.ResultDetail = $"Pago parcial: el bono cubre {FormatPesos(balance.BalanceMinorUnits)}.";
                    }
                    else
                    {
                        item.Status = QueueItemStatus.Rejected;
                        item.ResultDetail = $"Saldo insuficiente: tiene {FormatPesos(balance.BalanceMinorUnits)}.";
                        rejected++;
                        lines.Add(new OperationResultLine(
                            item.Card.Number,
                            $"Saldo insuficiente: tiene {FormatPesos(balance.BalanceMinorUnits)}.",
                            OperationResultKind.Rejected));
                        continue;
                    }
                }
            }

            SetBusy($"Redimiendo {item.Subtitle}…");

            var command = new ProcessSaleCommand(
                configuration.StoreId,
                configuration.TerminalId,
                cashier,
                item.Card.Number,
                amountToRedeem,
                DefaultCurrency,
                GiftCardOperation.Redemption);

            var result = await _handler.HandleAsync(command, CancellationToken.None);
            var success = result.Outcome == ProcessSaleOutcome.Approved;

            // Mismo traductor que en Activar: el cajero lee el motivo, no el código.
            var failure = success
                ? null
                : Common.OperationMessages.ForFailure(result.Error.Code, result.Error.Description);

            item.Status = success ? QueueItemStatus.Approved : QueueItemStatus.Rejected;
            item.ResultDetail = success
                ? $"Ref {result.ReferenceNumber} · Saldo restante {FormatPesos(result.RemainingBalanceMinorUnits ?? 0)}"
                : failure!.Text;

            if (success) approved++; else rejected++;

            lines.Add(new OperationResultLine(
                result.MaskedCardNumber is { Length: > 0 } masked ? masked : item.Card.Number,
                success
                    ? $"Ref {result.ReferenceNumber} · {FormatPesos(amountToRedeem)} · queda {FormatPesos(result.RemainingBalanceMinorUnits ?? 0)}"
                    : failure!.Text,
                success ? OperationResultKind.Approved : OperationResultKind.Rejected,
                failure?.Technical));

            if (success)
            {
                redeemedTotal += amountToRedeem;
                lastApproved = result;
            }
            else
            {
                lastError = $"{result.Error.Code}: {result.Error.Description}";
            }

            var detail = success
                ? $"{result.MaskedCardNumber} · ref {result.ReferenceNumber} · {FormatPesos(amountToRedeem)}"
                : $"{result.Error.Code}: {result.Error.Description}";

            await _activityLog.RecordAsync(
                new CashierActivityEntry(
                    DateTimeOffset.UtcNow, cashier, configuration.TerminalId, "Redención", detail, success),
                CancellationToken.None);

            ShowResult(result);
        }

        SetIdle("Listo.");

        for (var i = Queue.Count - 1; i >= 0; i--)
        {
            if (Queue[i].Status == QueueItemStatus.Approved)
            {
                Queue.RemoveAt(i);
            }
        }

        var headline = approved switch
        {
            0 => "Ningún bono se redimió",
            1 => "1 bono redimido",
            _ => $"{approved} bonos redimidos"
        };

        var parts = new List<string>();

        if (approved > 0)
        {
            parts.Add($"{FormatPesos(redeemedTotal)} cobrados");
        }

        if (rejected > 0)
        {
            parts.Add($"{rejected} rechazado{(rejected == 1 ? "" : "s")}");
        }

        // Cuando el bono no cubrió toda la venta, lo que falta va en el titular y no escondido
        // en una línea: es lo que el cajero tiene que cobrar por otro medio antes de cerrar, y
        // enterarse cuando ya volvió a HiPOS es enterarse tarde.
        if (HiPosFlow.Current is { } flow && approved > 0)
        {
            var missing = flow.AmountPesos - redeemedTotal;

            if (missing > 0)
            {
                headline = $"Faltan {FormatPesos(missing)} por cobrar";
                parts.Insert(0, $"el bono cubrió {FormatPesos(redeemedTotal)} de {FormatPesos(flow.AmountPesos)}");
                parts.Add("cobra el resto con otro medio de pago");
            }
        }

        foreach (var line in lines)
        {
            ResultLines.Add(line);
        }

        ResultIsSuccess = approved > 0;
        ResultSubline = string.Join(" · ", parts);
        ResultHeadline = headline;

        if (rejected > 0)
        {
            LastError = $"{rejected} rechazado{(rejected == 1 ? "" : "s")} en este lote. Toca ↩ para reintentar o descartar.";
        }

        if (!allowPartial)
        {
            return;
        }

        // Cobro de HiPOS: el resultado se queda en pantalla y el POS no recibe nada hasta que
        // el cajero lo acepte. Si el bono falló, no se responde: se deja el motivo a la vista
        // para que pueda pasar otro bono sin salir del cobro.
        if (approved == 0)
        {
            _pendingHiPosOutcome = null;
            return;
        }

        _pendingHiPosOutcome = new HiPos.HiPosOperationOutcome(
            Accepted: true,
            Pending: false,
            AmountAppliedPesos: redeemedTotal,
            Reference: lastApproved?.ReferenceNumber,
            CardNumber: lastApproved?.MaskedCardNumber,
            RemainingBalancePesos: lastApproved?.RemainingBalanceMinorUnits,
            ErrorMessage: lastError);
    }

    /// <summary>
    /// Resultado listo para entregarle a HiPOS, en espera de que el cajero lo acepte. Mientras
    /// esté aquí, el POS sigue esperando y el módulo no se cierra.
    /// </summary>
    private HiPos.HiPosOperationOutcome? _pendingHiPosOutcome;

    private void ShowResult(ProcessSaleResult result)
    {
        string headline;
        string maskedCard;
        string reference;
        string balanceText;

        switch (result.Outcome)
        {
            case ProcessSaleOutcome.Approved:
                headline = "Redención aprobada";
                maskedCard = result.MaskedCardNumber ?? string.Empty;
                reference = $"Ref {result.ReferenceNumber}";
                balanceText = $"Saldo restante {FormatPesos(result.RemainingBalanceMinorUnits ?? 0)}";
                break;
            case ProcessSaleOutcome.Unknown:
                headline = "Resultado incierto";
                maskedCard = string.Empty;
                reference = result.ReferenceNumber is null ? string.Empty : $"Ref {result.ReferenceNumber}";
                balanceText = $"{result.Error.Code}: {result.Error.Description}";
                break;
            default:
                headline = "Redención rechazada";
                maskedCard = string.Empty;
                reference = $"Código {result.Error.Code}";
                balanceText = result.Error.Description;
                break;
        }

        LastResult = new PosResult(
            result.Outcome == ProcessSaleOutcome.Approved,
            headline, maskedCard, reference, balanceText, HasResult: true);

        ResultProduced?.Invoke();
    }
}
