using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Permoda.Pay.Application;
using Permoda.Pay.Application.Features.Sales;
using Permoda.Pay.Domain.Common;
using Permoda.Pay.Domain.GiftCards;
using Permoda.Pay.Maui.Services;
using Permoda.Pay.Maui.ViewModels;

namespace Permoda.Pay.Maui.ViewModels.Pos;

/// <summary>
/// Flujo "Activar bono" rediseñado: en vez de un solo form que empuja a una cola, ahora
/// el cajero ve un formulario editable POR bono, con "+" para agregar más, y un único CTA
/// al final "Procesar N bonos". Cada fila pasa por Pendiente → Procesando → Aprobado/
/// Rechazado, igual que antes, pero el cajero puede seguir agregando/retocando filas
/// mientras otras ya se procesaron. KOAJ sigue sin manejar cancelaciones: este flujo
/// solo dispara /activation o /orderCreation → /orderConfirm → /reconciliation.
/// </summary>
public sealed partial class ActivateTabViewModel : PosViewModelBase
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    // Cédula colombiana (y similares): solo dígitos, 6 a 15 caracteres. El helper
    // CustomerMobileNoBuilder usa esta misma cadena sin transformación para el campo note/message.
    [GeneratedRegex(@"^\d{6,15}$")]
    private static partial Regex DocumentNumberPattern();

    // Rango de respaldo, para cuando el catálogo todavía no está cargado (el cajero entra directo
    // a "Activar" sin pasar por Inicio). Tiene que coincidir con lo que Ogloba acepta: si la UI
    // deja tipear un monto que Ogloba rechaza, el cajero se entera con el error 73 "Wrong
    // activation amount" y el cliente esperando.
    //
    // Valores OFICIALES, de la hoja PARAMETROS-OGLOBA del documento de credenciales:
    // Og_CompraMinima = 30.000 y Og_CompraMaxima = 1.000.000.
    //
    // El máximo estaba en 500.000 porque era lo que respondía el catálogo del producto 113816 en
    // sandbox (medido el 2026-08-24). Ese es el tope de ESE producto de prueba, no el del
    // negocio: con él, un bono de 800.000 se rechazaba en pantalla sin haber preguntado nunca a
    // Ogloba. Cuando el catálogo carga, sus valores mandan sobre estos.
    private const long MinActivationPesos = 30_000;
    private const long MaxActivationPesos = 1_000_000;

    private readonly ProcessSaleHandler _handler;
    private readonly ActivateVirtualGiftCardHandler _virtualHandler;
    private readonly PosSession _session;
    private readonly CashierActivityLog _activityLog;
    private readonly ActivationReceiptStore _receipts;

    public ActivateTabViewModel(
        ProcessSaleHandler handler,
        ActivateVirtualGiftCardHandler virtualHandler,
        PosSession session,
        PosHeaderViewModel header,
        CashierActivityLog activityLog,
        ActivationReceiptStore receipts)
    {
        _handler = handler;
        _virtualHandler = virtualHandler;
        _session = session;
        Header = header;
        _activityLog = activityLog;
        _receipts = receipts;

        // Arranca SIN filas: la página muestra un estado vacío amigable con CTA
        // "Agregar primer bono". Antes se inicializaba con una fila en blanco, que
        // se veía como un form huérfano sin contexto ni información.

        Rows.CollectionChanged += (_, _) => OnEmptyStateChanged();
        OnEmptyStateChanged();

        // Cuando IsBusy cambia (en el base class), refresh de CanProcess.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsBusy))
            {
                OnPropertyChanged(nameof(CanProcess));
            }
        };

        // Mismo motivo que en Redimir: este ViewModel es singleton y la pantalla no se destruye
        // entre cobros, así que sin esto el siguiente heredaría las filas del anterior.
        HiPosFlow.FlowStarted += OnHiPosFlowStarted;
    }

    /// <summary>
    /// Deja la pantalla en cero para el cobro que acaba de empezar.
    /// <para>
    /// Se engancha al COBRO y no a <c>OnAppearing</c>, porque al terminar uno la pantalla se manda
    /// al fondo sin destruirse: en el siguiente, el Shell ya está acá y navegar a la ruta donde ya
    /// estás no vuelve a disparar la aparición.
    /// </para>
    /// <para>
    /// Lo crítico es <c>Rows.Clear()</c>. Un banner viejo se ve; una fila del cobro anterior que
    /// sigue en la lista NO se ve, y se activaría de nuevo con el siguiente lote.
    /// </para>
    /// </summary>
    private void OnHiPosFlowStarted() =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                Rows.Clear();

                DraftCardValue = string.Empty;
                DraftAmountText = string.Empty;
                LastError = string.Empty;

                ResultLines.Clear();
                ResultHeadline = string.Empty;
                ResultSubline = string.Empty;

                SaleAmountBanner =
                    HiPosFlow.Current is { Intent: HiPos.HiPosSaleIntent.Activation } flow
                        ? $"Valor de la venta: {FormatPesos(flow.AmountPesos)}"
                        : string.Empty;

                OnPropertyChanged(nameof(HasSaleAmountBanner));
                OnEmptyStateChanged();
            }
            catch (Exception exception)
            {
#if ANDROID
                Android.Util.Log.Error(
                    "TefOgloba", $"[ActivateTabViewModel] No se pudo reiniciar para el cobro: {exception}");
#endif
            }
        });

    private void OnEmptyStateChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(ProcessButtonText));
        OnPropertyChanged(nameof(CanProcess));
        OnPropertyChanged(nameof(RowsCountText));
        OnPropertyChanged(nameof(TotalToActivate));
        OnPropertyChanged(nameof(SummaryText));
    }

    public PosHeaderViewModel Header { get; }

    /// <summary>Lista de filas editables. "+ Agregar" inserta; "✕" en cada fila elimina.</summary>
    public ObservableCollection<ActivationFormRow> Rows { get; } = new();

    // IsEmpty y HasRows son propiedades get-only → bindings no se actualizan
    // automáticamente al cambiar Rows.Count. Hay que notificar manualmente en
    // OnEmptyStateChanged cuando Rows.CollectionChanged se dispara.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPhysical))]
    [NotifyPropertyChangedFor(nameof(IsVirtual))]
    [NotifyPropertyChangedFor(nameof(ProcessButtonText))]
    private ActivationFormMode _selectedMode = ActivationFormMode.Physical;

    public bool IsPhysical => SelectedMode == ActivationFormMode.Physical;
    public bool IsVirtual => !IsPhysical;

    /// <summary>
    /// El tipo de bono ya se eligió en la pantalla anterior, así que el segmentado de esta no se
    /// muestra. Cambiar de tipo con un bono a medio capturar solo servía para equivocarse: el
    /// mismo campo pasa de ser un serial a ser un correo.
    /// </summary>
    [ObservableProperty]
    private bool _isModeLocked;

    /// <summary>
    /// Entra a activar con el tipo ya decidido. Limpia lo que hubiera quedado de una activación
    /// anterior: si no, el serial del bono pasado aparece en el formulario del siguiente cliente.
    /// </summary>
    public void StartWithMode(ActivationFormMode mode)
    {
        SelectedMode = mode;
        IsModeLocked = true;
        DraftCardValue = string.Empty;
        DraftAmountText = string.Empty;
        LastError = string.Empty;

        ClearCustomer();
    }

    /// <summary>
    /// Borra los datos del cliente de la activación anterior.
    /// <para>
    /// La cédula y el nombre identifican a UNA persona, así que arrastrarlos a la siguiente
    /// activación no es una comodidad: es el riesgo de emitirle un bono a un cliente con los
    /// datos de otro. Y como el formulario ya viene lleno, el cajero no tiene por qué notarlo.
    /// </para>
    /// <para>
    /// El tipo de documento SÍ se conserva: es una preferencia de la caja, no un dato de la
    /// persona, y en KOAJ casi siempre es CC. Volver a elegirlo en cada venta sería fricción sin
    /// ninguna ganancia.
    /// </para>
    /// </summary>
    private void ClearCustomer()
    {
        CustomerName = string.Empty;
        CustomerDocumentNumber = string.Empty;
    }

    // ----- Captura (formulario superior) -----

    /// <summary>Serial de la tarjeta física o correo del cliente, según el modo.</summary>
    [ObservableProperty]
    private string _draftCardValue = string.Empty;

    [ObservableProperty]
    private string _draftAmountText = string.Empty;

    /// <summary>
    /// Última validación fallida del draft. Se pinta inline debajo del check como una
    /// línea de error (sin toast). Se limpia automáticamente en cuanto el cajero
    /// retoca el serial o el monto.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastError))]
    private string _lastError = string.Empty;

    public bool HasLastError => !string.IsNullOrWhiteSpace(LastError);

    partial void OnDraftCardValueChanged(string value) => LastError = string.Empty;
    partial void OnDraftAmountTextChanged(string value) => LastError = string.Empty;
    partial void OnSelectedModeChanged(ActivationFormMode value)
    {
        DraftCardValue = string.Empty;
        LastError = string.Empty;

        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(ProcessButtonText));
        OnPropertyChanged(nameof(CanProcess));
        OnPropertyChanged(nameof(DraftCardLabel));
        OnPropertyChanged(nameof(DraftCardPlaceholder));
        OnPropertyChanged(nameof(AmountHelpText));
    }

    public string DraftCardLabel => IsPhysical ? "SERIAL DEL BONO (10-16 DÍGITOS)" : "CORREO DEL CLIENTE";

    public string DraftCardPlaceholder => IsPhysical
        ? "8472 9501 3284 6671"
        : "cliente@correo.com";

    /// <summary>
    /// Rango que Ogloba acepta para el producto que se va a emitir. Sale del catálogo real
    /// (`/getProducts` → <c>activationMinAmt</c> / <c>activationMaxAmt</c>), no de una constante:
    /// cada producto tiene el suyo — el bono virtual B2C tope en 500.000 mientras otros llegan a
    /// 1.000.000. Si el catálogo no está cargado, se cae a las constantes del sandbox.
    /// </summary>
    private (long Min, long Max) ActivationRange
    {
        get
        {
            // En virtual conocemos el itemCode exacto; en física el producto lo determina el PAN
            // de la tarjeta y solo se sabe cuando Ogloba responde.
            if (IsVirtual)
            {
                var itemCode = _session.Environment.DigitalProductCode;
                var product = _session.Products.FirstOrDefault(p => p.ItemCode == itemCode);

                if (product is not null)
                {
                    var min = product.ActivationMinAmtMinorUnits ?? MinActivationPesos;
                    var max = product.ActivationMaxAmtMinorUnits ?? MaxActivationPesos;

                    if (max > 0 && max >= min)
                    {
                        return (min, max);
                    }
                }
            }

            return (MinActivationPesos, MaxActivationPesos);
        }
    }

    /// <summary>
    /// Granularidad del monto de activación: solo se emiten bonos en múltiplos de esta cifra.
    /// <para>
    /// Decisión de KOAJ (2026-09-03): los bonos se venden en denominaciones redondas —30.000,
    /// 40.000, 50.000…—, no en cifras arbitrarias.
    /// </para>
    /// <para>
    /// Es una constante para poder moverla de un solo lado. Ogloba además expone la suya por
    /// producto en <c>/getProducts</c> como <c>activateAmtInterval</c> (docs §3.16, v2.19); el
    /// día que se quiera respetar la del catálogo en vez de esta, se lee de ahí igual que ya se
    /// hace con el rango mínimo y máximo.
    /// </para>
    /// </summary>
    private const long ActivationStepPesos = 10_000;

    public string AmountHelpText
    {
        get
        {
            var (min, max) = ActivationRange;
            return $"Entre {FormatPesos(min)} y {FormatPesos(max)}, en múltiplos de {FormatPesos(ActivationStepPesos)}.";
        }
    }

    // ----- Resumen del lote procesado -----

    /// <summary>
    /// Lo que el cajero le repite al cliente cuando termina: qué bono salió, a qué correo y por
    /// cuánto. Se conserva en pantalla hasta que él la cierre, en vez de desvanecerse.
    /// </summary>
    public ObservableCollection<OperationResultLine> ResultLines { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string _resultHeadline = string.Empty;

    [ObservableProperty]
    private string _resultSubline = string.Empty;

    /// <summary>Tono del panel: verde si salió al menos un bono, rojo si no salió ninguno.</summary>
    [ObservableProperty]
    private bool _resultIsSuccess = true;

    public bool HasResult => !string.IsNullOrWhiteSpace(ResultHeadline);

    [RelayCommand]
    private async Task DismissResultAsync()
    {
        var activated = ResultIsSuccess;

        ResultLines.Clear();
        ResultHeadline = string.Empty;
        ResultSubline = string.Empty;

        // El cliente se borra ACÁ además de en StartWithMode. No es redundante: en un cobro
        // ordenado por HiPOS el módulo entra derecho a Activar sin pasar por la pantalla de tipo
        // de bono, así que StartWithMode no corre y los datos del cliente anterior seguirían en
        // el formulario de la siguiente venta.
        ClearCustomer();

        // "Listo" es lo que cierra el cobro de HiPOS: hasta acá el cajero vio el resultado en
        // pantalla, y recién ahora el POS se entera y el módulo se va.
        if (_pendingHiPosOutcome is { } outcome)
        {
            _pendingHiPosOutcome = null;
            HiPosFlow.Complete(outcome);
            HiPosFlow.CloseModuleScreen();
            return;
        }

        // Activación manual: el turno se cierra al confirmar que el bono QUEDÓ ACTIVADO, no
        // antes. Si falló, la sesión sigue abierta para que el cajero reintente sin volver a
        // digitar su contraseña — cerrarla ahí lo castigaría por un error que no fue suyo.
        if (!activated)
        {
            return;
        }

        _session.LogoutCashier();
        Header.Refresh();

        await Shell.Current.GoToAsync(Common.AppRoutes.CashierLogin);
    }

    /// <summary>Encabezado de la lista: "Bonos a activar · N".</summary>
    public string RowsCountText => $"Bonos a activar · {Rows.Count}";

    /// <summary>Suma de los montos del lote, en pesos.</summary>
    public long TotalToActivate
    {
        get
        {
            var total = 0L;
            foreach (var row in Rows)
            {
                if (Common.MoneyInput.TryParsePesos(row.AmountText, out var amount))
                {
                    total += amount;
                }
            }
            return total;
        }
    }

    /// <summary>Pie del lote: "Resumen: 1 bono · $50.000".</summary>
    public string SummaryText
    {
        get
        {
            var bonos = Rows.Count == 1 ? "1 bono" : $"{Rows.Count} bonos";
            return $"Resumen: {bonos} · {FormatPesos(TotalToActivate)}";
        }
    }

    /// <summary>
    /// Monto de una fila ya validada. Pasa por <see cref="Common.MoneyInput"/> porque el campo
    /// muestra separadores de miles y un <c>long.Parse</c> crudo reventaría con "50.000".
    /// Las filas llegan aquí solo después de <see cref="ActivationFormRow.Validate"/>, así que
    /// un valor no parseable sería un fallo de programación, no una entrada del cajero.
    /// </summary>
    private static long ParseRowAmount(ActivationFormRow row) =>
        Common.MoneyInput.TryParsePesos(row.AmountText, out var amount) ? amount : 0;

    public int PendingCount
    {
        get
        {
            var n = 0;
            foreach (var row in Rows)
            {
                if (row.Status == ActivationRowStatus.Pending)
                {
                    n++;
                }
            }
            return n;
        }
    }

    /// <summary>True cuando no hay ninguna fila: la página muestra estado vacío con CTA.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>True cuando hay al menos una fila: la página muestra el formulario y el "+".</summary>
    public bool HasRows => Rows.Count > 0;

    public string ProcessButtonText
    {
        get
        {
            var pending = PendingCount;
            return pending switch
            {
                0 => "Agrega al menos un bono",
                1 => "Activar 1 bono",
                _ => $"Activar {pending} bonos"
            };
        }
    }

    public bool CanProcess => PendingCount > 0 && !IsBusy;

    public async Task EnsureSessionAsync()
    {

        await _session.EnsureLoadedAsync(CancellationToken.None);
        Header.Refresh();

        // Si el cajero entra directo a "Activar" sin pasar por Inicio, /getProducts no se ha
        // llamado y ActivationRange cae a las constantes (límite verificado del producto 113816).
        // Cargar el catálogo aquí garantiza que el rango UI refleje el de Ogloba, no el fallback.
        // Best-effort: si falla (sin red, sin config), se sigue trabajando con el rango constante.
        await _session.LoadProductsAsync(CancellationToken.None);
        OnPropertyChanged(nameof(AmountHelpText));

        // La pantalla del cliente es un periférico: puede estar desconectada justo hoy. Se
        // consulta al entrar y no una vez al arrancar la app.

        // Si llegamos aquí porque HiPOS ordenó un cobro, el importe ya lo fijó la factura: se
        // deja puesto para que el cajero solo capture el bono. Tipearlo de nuevo sería pedirle
        // que copie un número que el POS ya sabe, y cualquier diferencia descuadraría la venta.
        if (HiPosFlow.Current is { Intent: HiPos.HiPosSaleIntent.Activation } flow)
        {
            DraftAmountText = Common.MoneyInput.Format(flow.AmountPesos.ToString());
            SaleAmountBanner = $"Valor del recaudo: {FormatPesos(flow.AmountPesos)}";
            IsCollectionFlow = true;
        }
        else
        {
            SaleAmountBanner = string.Empty;
            IsCollectionFlow = false;
        }

        OnPropertyChanged(nameof(CanLeaveToMenu));
    }

    /// <summary>
    /// Se puede salir al menú. Falso cuando el módulo lo levantó HiPOS: el POS está esperando una
    /// respuesta y volver al menú lo dejaría colgado. En ese modo la única salida es "Volver a
    /// HiPOS", que sí le contesta.
    /// </summary>
    public bool CanLeaveToMenu => !HiPosFlow.IsActive;

    // ----- Recaudo desde HiPOS -----

    /// <summary>
    /// El módulo lo levantó HiPOS para un recaudo. Solo entonces se piden los datos del cliente:
    /// en una activación normal desde la app no hay a quién atribuirle el recaudo, y pedirlos
    /// sería fricción sin destino.
    /// </summary>
    [ObservableProperty]
    private bool _isCollectionFlow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaleAmountBanner))]
    private string _saleAmountBanner = string.Empty;

    public bool HasSaleAmountBanner => !string.IsNullOrWhiteSpace(SaleAmountBanner);

    /// <summary>
    /// Pie del panel de resultado. Se llena solo cuando aplica: era un texto fijo sobre los bonos
    /// virtuales y salía también cuando la activación fallaba, sugiriendo que algo iba a llegar
    /// por correo cuando no se había emitido nada.
    /// </summary>
    [ObservableProperty]
    private string _resultFootNote = string.Empty;

    [ObservableProperty]
    private string _customerName = string.Empty;

    /// <summary>Tipo de documento. Los valores son los que usa KOAJ en caja.</summary>
    public IReadOnlyList<string> DocumentTypes { get; } = ["CC", "CE", "NIT", "TI", "PAS"];

    [ObservableProperty]
    private string _customerDocumentType = "CC";

    [ObservableProperty]
    private string _customerDocumentNumber = string.Empty;

    /// <summary>Resumen del cliente para la bitácora y para lo que se le devuelve a HiPOS.</summary>
    private string CustomerSummary =>
        string.IsNullOrWhiteSpace(CustomerName) && string.IsNullOrWhiteSpace(CustomerDocumentNumber)
            ? string.Empty
            : $"{CustomerName.Trim()} · {CustomerDocumentType} {CustomerDocumentNumber.Trim()}".Trim(' ', '·');

    /// <summary>
    /// String concatenado que se va a mandar a Ogloba en el campo note/message. Se muestra al
    /// cajero como preview para que verifique antes de activar.
    /// </summary>
    public string CustomerMobileNoPreview =>
        CustomerMobileNoBuilder.Build(CustomerDocumentNumber, CustomerName);

    /// <summary>True cuando hay al menos cédula o nombre capturados (muestra el preview).</summary>
    public bool HasCustomerData =>
        !string.IsNullOrWhiteSpace(CustomerDocumentNumber) || !string.IsNullOrWhiteSpace(CustomerName);

    partial void OnCustomerNameChanged(string value) =>
        NotifyCustomerDataChanged();

    partial void OnCustomerDocumentNumberChanged(string value) =>
        NotifyCustomerDataChanged();

    partial void OnCustomerDocumentTypeChanged(string value) =>
        NotifyCustomerDataChanged();

    private void NotifyCustomerDataChanged()
    {
        OnPropertyChanged(nameof(CustomerMobileNoPreview));
        OnPropertyChanged(nameof(HasCustomerData));
        OnPropertyChanged(nameof(CustomerSummary));
    }

    /// <summary>
    /// Llamado por el code-behind cuando el Entry del serial recibe Enter (todo lector wedge
    /// emite Enter al terminar el escaneo). El serial ya está en <see cref="DraftCardValue"/>
    /// por el binding, así que solo intentamos agregarlo al lote.
    /// </summary>
    public void HandleScannedSerial(string serial)
    {
        if (!string.IsNullOrWhiteSpace(serial))
        {
            DraftCardValue = serial.Trim();
        }

        if (!string.IsNullOrWhiteSpace(DraftAmountText))
        {
            AddDraft();
        }
    }

    [RelayCommand]
    private void SelectMode(string mode)
    {
        if (Enum.TryParse<ActivationFormMode>(mode, out var m))
        {
            SelectedMode = m;
        }
    }

    /// <summary>
    /// Valida la captura y la agrega al lote. Se valida ANTES de encolar para que el cajero
    /// corrija en el momento, en vez de descubrir el error cuando ya mandó el lote a Ogloba.
    /// </summary>
    [RelayCommand]
    private void AddDraft()
    {
        var row = new ActivationFormRow(SelectedMode)
        {
            CardValue = (DraftCardValue ?? string.Empty).Trim(),
            AmountText = (DraftAmountText ?? string.Empty).Trim()
        };

        var validation = row.Validate();

        if (validation.IsFailure)
        {
            LastError = validation.Error.Description;
            return;
        }

        var amount = validation.Value;
        var (min, max) = ActivationRange;

        if (amount < min || amount > max)
        {
            LastError =
                $"Monto fuera de rango. Este bono admite entre {FormatPesos(min)} y {FormatPesos(max)}. " +
                "Corrige el monto antes de agregarlo al lote.";
            return;
        }

        // Solo denominaciones redondas. El mensaje dice el valor VÁLIDO MÁS CERCANO en vez de
        // enunciar la regla a secas: el cajero está con el cliente enfrente y lo que necesita es
        // qué escribir, no que le expliquen la política.
        if (amount % ActivationStepPesos != 0)
        {
            var nearest = Math.Clamp(
                (long)Math.Round((double)amount / ActivationStepPesos) * ActivationStepPesos,
                min,
                max);

            LastError =
                $"Solo se admiten valores múltiplos de {FormatPesos(ActivationStepPesos)}. "
                + $"El más cercano es {FormatPesos(nearest)}.";
            return;
        }

        Rows.Add(row);
        DraftCardValue = string.Empty;
        DraftAmountText = string.Empty;
    }

    /// <summary>
    /// Devuelve una fila de la lista al formulario de captura para editarla. El cajero
    /// retoca el serial/correo o el monto y vuelve a marcarla con el check ✓ para reenviarla
    /// a la cola.
    /// </summary>
    [RelayCommand]
    private void ReturnRow(ActivationFormRow? row)
    {
        if (row is null || !row.IsRemovable)
        {
            return;
        }

        Rows.Remove(row);
        DraftCardValue = row.CardValue;
        DraftAmountText = row.AmountText;
        OnPropertyChanged(nameof(DraftCardLabel));
        OnPropertyChanged(nameof(DraftCardPlaceholder));
    }

    /// <summary>
    /// Resultado listo para entregarle a HiPOS, en espera de que el cajero lo acepte. Mientras
    /// esté aquí, el POS sigue esperando y el módulo no se cierra.
    /// </summary>
    private HiPos.HiPosOperationOutcome? _pendingHiPosOutcome;

    /// <summary>
    /// Deja preparado lo que se le va a responder a HiPOS cuando el cajero acepte el resultado.
    /// Si no se emitió ningún bono no se prepara nada: el motivo queda a la vista y el cajero
    /// puede intentar con otro bono sin salir del recaudo.
    /// </summary>
    private void BuildHiPosOutcome(int approved, int pendingReview, long total)
    {
        if (HiPosFlow.Current is not { Intent: HiPos.HiPosSaleIntent.Activation })
        {
            _pendingHiPosOutcome = null;
            return;
        }

        if (approved + pendingReview == 0)
        {
            _pendingHiPosOutcome = null;
            return;
        }

        var customer = CustomerSummary;

        _pendingHiPosOutcome = new HiPos.HiPosOperationOutcome(
            Accepted: approved > 0,
            Pending: approved == 0 && pendingReview > 0,
            AmountAppliedPesos: total,
            Reference: _lastActivationReference,
            CardNumber: _lastActivationCard,
            Customer: string.IsNullOrWhiteSpace(customer) ? null : customer);
    }

    private string? _lastActivationReference;
    private string? _lastActivationCard;

    [RelayCommand]
    private void RemoveRow(ActivationFormRow? row)
    {
        if (row is null || !row.IsRemovable)
        {
            return;
        }

        Rows.Remove(row);
    }

    /// <summary>Vacía el lote y la captura. No toca Ogloba.</summary>
    [RelayCommand]
    private void ClearAll()
    {
        Rows.Clear();
        DraftCardValue = string.Empty;
        DraftAmountText = string.Empty;
    }

    [RelayCommand]
    private async Task ProcessAllAsync()
    {
        // Cobro ordenado por HiPOS: la llamada a Ogloba y el comprobante los arma el módulo de
        // pago, que es quien tiene que responderle al POS. Aquí solo se le entrega el bono que
        // el cajero capturó; procesarlo también desde esta pantalla lo cobraría dos veces.

        await _session.EnsureLoadedAsync(CancellationToken.None);
        Header.Refresh();

        if (!_session.IsConfigured)
        {
            LastError = "Terminal sin configurar. Ve al apartado Config.";
            return;
        }

        if (!_session.HasActiveCashier)
        {
            LastError = "Sin cajero. Inicia turno como cajero antes de operar.";
            return;
        }

        // Datos del cliente: obligatorios para activar. El nombre y la cédula se concatenan
        // en "CC-NombreApellido" y se mandan a Ogloba en el campo note/message — sin esos
        // datos la activación no procede, porque perderíamos la trazabilidad del cliente
        // asociada al bono.
        if (string.IsNullOrWhiteSpace(CustomerDocumentNumber))
        {
            LastError = "Falta el número de documento del cliente.";
            return;
        }

        if (!DocumentNumberPattern().IsMatch(CustomerDocumentNumber.Trim()))
        {
            LastError = "El documento debe tener entre 6 y 15 dígitos, sin puntos ni comas.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CustomerName))
        {
            LastError = "Falta el nombre del cliente.";
            return;
        }

        // Snapshot de las filas pendientes (por si el cajero agrega/quita mientras
        // procesamos — la operación trabaja sobre el set original).
        var pending = Rows.Where(r => r.Status == ActivationRowStatus.Pending).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        // Validamos primero todas las filas para evitar gastar llamadas a Ogloba
        // con un set inválido.
        var invalid = new List<(ActivationFormRow Row, string Error)>();
        foreach (var row in pending)
        {
            var v = row.Validate();
            if (v.IsFailure)
            {
                invalid.Add((row, v.Error.Description));
                row.ResultDetail = v.Error.Description;
            }
        }

        if (invalid.Count > 0)
        {
            LastError = $"{invalid.Count} fila(s) no se pueden enviar. Corrige el serial/correo o el monto y vuelve a intentar.";
            return;
        }

        var configuration = _session.Configuration!;
        var cashier = _session.ActiveCashierId!;
        int approved = 0, pendingReview = 0, rejected = 0;

        // El resumen anterior se retira antes de empezar: dejarlo mientras corre el lote nuevo
        // haría que el cajero lea el resultado equivocado. Se limpia directo y no por el comando
        // de "Listo", que además cierra el turno.
        ResultLines.Clear();
        ResultHeadline = string.Empty;
        ResultSubline = string.Empty;

        var lines = new List<OperationResultLine>();
        var activatedTotal = 0L;
        var emails = new List<string>();

        foreach (var row in pending)
        {
            row.Status = ActivationRowStatus.Processing;
            row.ResultDetail = string.Empty;
            SetBusy($"Procesando fila…");

            if (row.IsPhysical)
            {
                var command = new ProcessSaleCommand(
                    configuration.StoreId,
                    configuration.TerminalId,
                    cashier,
                    row.CardValue,
                    ParseRowAmount(row),
                    DefaultCurrency,
                    GiftCardOperation.Activation,
                    CardIdentifierKind.PhysicalCard,
                    CustomerDocumentNumber: CustomerDocumentNumber,
                    CustomerName: CustomerName);

                var result = await _handler.HandleAsync(command, CancellationToken.None);
                var success = result.Outcome == ProcessSaleOutcome.Approved;

                // El motivo se traduce a lenguaje de mostrador. Antes se pintaba crudo —"73:
                // Wrong activation amount"— y dejaba al cajero sin saber qué hacer.
                var failure = success
                    ? null
                    : Common.OperationMessages.ForFailure(result.Error.Code, result.Error.Description);

                row.Status = success ? ActivationRowStatus.Approved : ActivationRowStatus.Rejected;
                row.ResultDetail = success
                    ? $"Ref {result.ReferenceNumber} · Saldo {FormatPesos(result.RemainingBalanceMinorUnits ?? 0)}"
                    : failure!.Text;

                if (success)
                {
                    approved++;
                }
                else
                {
                    rejected++;
                }

                lines.Add(new OperationResultLine(
                    row.CardValue,
                    success
                        ? $"Ref {result.ReferenceNumber} · {FormatPesos(ParseRowAmount(row))}"
                        : failure!.Text,
                    success ? OperationResultKind.Approved : OperationResultKind.Rejected,
                    failure?.Technical));

                if (success)
                {
                    activatedTotal += ParseRowAmount(row);
                    _lastActivationReference = result.ReferenceNumber;
                    _lastActivationCard = result.MaskedCardNumber ?? row.CardValue;

                    // Comprobante SIEMPRE que se active. La activación no pasa por HiPOS, así
                    // que nadie más lo genera: si no se guarda acá, no existe en ninguna parte.
                    // Va el serial escaneado, no el enmascarado del resultado: es el que el
                    // cliente necesita para redimir.
                    await _receipts.SaveAsync(
                        configuration.StoreId,
                        configuration.TerminalId,
                        result.ReferenceNumber ?? string.Empty,
                        row.CardValue,
                        ParseRowAmount(row),
                        "COP",
                        result.RemainingBalanceMinorUnits,
                        eGiftCardUrl: null,
                        CancellationToken.None);
                }

                // La bitácora guarda el código crudo, no el texto traducido: es la que se lee para
                // rastrear un caso, y ahí el dato exacto vale más que la redacción.
                var detail = success
                    ? $"Bono física · ref {result.ReferenceNumber} · {FormatPesos(ParseRowAmount(row))} · cliente {CustomerSummary}"
                    : $"{result.Error.Code}: {result.Error.Description}";

                await _activityLog.RecordAsync(
                    new CashierActivityEntry(
                        DateTimeOffset.UtcNow, cashier, configuration.TerminalId, "Activación física",
                        detail, success),
                    CancellationToken.None);
            }
            else
            {
                // BONO DIGITAL POR /activation, no por Order management.
                //
                // El módulo usaba /orderCreation + /orderConfirm. Ogloba lo corrigió el
                // 2026-09-09: «orderCreation, orderConfirm, orderStatus y orderCancel normalmente
                // se utilizan para generar lotes de tarjetas (alto volumen). Para la activación de
                // una tarjeta digital en tienda física deben usar la API activation e incluir dos
                // parámetros: gencode y email».
                //
                // El gencode va donde iría el serial de una tarjeta física, y el correo del
                // cliente en su campo propio: Ogloba le envía el bono a esa dirección al
                // confirmarse la activación. O sea, el MISMO camino que el bono físico —
                // /activation + /confirmTransaction— con dos campos distintos.
                //
                // Esto además desbloqueó producción: el APIM de Permoda no publica los recursos
                // de Order management, y por eso dábamos el bono digital por imposible. No lo era.
                var command = new ProcessSaleCommand(
                    configuration.StoreId,
                    configuration.TerminalId,
                    cashier,
                    _session.Environment.DigitalProductCode,
                    ParseRowAmount(row),
                    DefaultCurrency,
                    GiftCardOperation.Activation,
                    CardIdentifierKind.DigitalGencode,
                    CustomerDocumentNumber: CustomerDocumentNumber,
                    CustomerName: CustomerName,

                    // La fila captura el CORREO del cliente cuando el modo es virtual — es lo que
                    // el cajero escribió, no un serial.
                    CustomerEmail: row.CardValue);

                var result = await _handler.HandleAsync(command, CancellationToken.None);
                var success = result.Outcome == ProcessSaleOutcome.Approved;

                var failure = success
                    ? null
                    : Common.OperationMessages.ForFailure(result.Error.Code, result.Error.Description);

                row.Status = success ? ActivationRowStatus.Approved : ActivationRowStatus.Rejected;

                // El serial que Ogloba devuelve al emitir. Es lo que el cliente necesita si
                // pregunta, y con lo que soporte rastrea el bono si el correo no llega.
                var issued = string.IsNullOrWhiteSpace(result.CardNumber)
                    ? string.Empty
                    : $" · bono {result.MaskedCardNumber}";

                row.ResultDetail = success
                    ? $"Ref {result.ReferenceNumber}{issued} · enviado a {row.CardValue}"
                    : failure!.Text;

                if (success)
                {
                    approved++;
                }
                else
                {
                    rejected++;
                }

                lines.Add(new OperationResultLine(
                    row.CardValue,
                    success
                        ? $"Ref {result.ReferenceNumber}{issued} · {FormatPesos(ParseRowAmount(row))}"
                        : failure!.Text,
                    success ? OperationResultKind.Approved : OperationResultKind.Rejected,
                    failure?.Technical));

                if (success)
                {
                    activatedTotal += ParseRowAmount(row);
                    emails.Add(row.CardValue);
                    _lastActivationReference = result.ReferenceNumber;
                    _lastActivationCard = result.MaskedCardNumber ?? row.CardValue;

                    // El comprobante lleva el serial COMPLETO: es lo que permite redimir el bono
                    // si el correo nunca llega. Enmascararlo acá lo dejaría inservible.
                    await _receipts.SaveAsync(
                        configuration.StoreId,
                        configuration.TerminalId,
                        result.ReferenceNumber ?? string.Empty,
                        result.CardNumber ?? row.CardValue,
                        ParseRowAmount(row),
                        DefaultCurrency,
                        result.RemainingBalanceMinorUnits,
                        result.EGiftCardUrl,
                        CancellationToken.None);
                }

                // La bitácora guarda además el link del bono: es con lo que se resuelve un
                // "no me llegó el correo" sin tener que entrar al back office de Ogloba.
                // El serial va ENMASCARADO acá: la bitácora se exporta y se comparte, y con el
                // serial completo cualquiera que la lea podría redimir el bono.
                var detail = success
                    ? $"Bono virtual · ref {result.ReferenceNumber} · {FormatPesos(ParseRowAmount(row))} · enviado a {row.CardValue} · cliente {CustomerSummary}"
                      + (string.IsNullOrWhiteSpace(result.MaskedCardNumber) ? string.Empty : $" · bono {result.MaskedCardNumber}")
                      + (string.IsNullOrWhiteSpace(result.EGiftCardUrl) ? string.Empty : $" · {result.EGiftCardUrl}")
                    : $"{result.Error.Code}: {result.Error.Description}";

                await _activityLog.RecordAsync(
                    new CashierActivityEntry(
                        DateTimeOffset.UtcNow, cashier, configuration.TerminalId, "Activación virtual",
                        detail, success),
                    CancellationToken.None);
            }
        }

        SetIdle("Listo.");

        // Resumen: primero lo que salió bien, porque es lo que el cajero le dice al cliente.
        var emitted = approved + pendingReview;

        var headline = emitted switch
        {
            0 => "Ningún bono se activó",
            1 => "1 bono activado",
            _ => $"{emitted} bonos activados"
        };

        var parts = new List<string>();

        if (emitted > 0)
        {
            parts.Add(FormatPesos(activatedTotal));
        }

        if (emails.Count > 0)
        {
            parts.Add(emails.Count == 1
                ? $"enviado a {emails[0]}"
                : $"enviados a {emails.Count} correos");
        }

        if (pendingReview > 0)
        {
            parts.Add($"{pendingReview} en emisión");
        }

        if (rejected > 0)
        {
            parts.Add($"{rejected} rechazado{(rejected == 1 ? "" : "s")}");
        }

        // Si no salió NINGÚN bono, el subtítulo deja de ser un conteo y pasa a ser el MOTIVO.
        // "Ningún bono se activó · 1 rechazado" no le dice nada al cajero: ya sabe que no salió.
        // Lo que necesita es por qué, y en el mismo renglón grande, no buscándolo en una lista.
        if (emitted == 0 && rejected > 0)
        {
            var reasons = lines
                .Where(line => line.IsRejected)
                .Select(line => line.Detail)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            parts.Clear();

            parts.Add(reasons.Count == 1
                ? reasons[0]
                : $"Los {rejected} bonos fallaron por motivos distintos. Revisa cada uno abajo.");
        }

        // La nota del correo SOLO cuando hay bonos virtuales que efectivamente salieron. Antes
        // era fija, así que aparecía también en los errores: al cajero le decían que el bono
        // llegaría por correo justo debajo del mensaje de que no se activó nada.
        ResultFootNote = emails.Count > 0
            ? "Los bonos virtuales llegan al correo del cliente. Puede tardar unos minutos."
            : string.Empty;

        // Las filas se cargan ANTES del titular: el titular es lo que hace visible el panel, y
        // la animación escalonada necesita que las líneas ya existan cuando eso ocurre.
        foreach (var line in lines)
        {
            ResultLines.Add(line);
        }

        ResultIsSuccess = emitted > 0;
        ResultSubline = string.Join(" · ", parts);
        ResultHeadline = headline;

        BuildHiPosOutcome(approved, pendingReview, activatedTotal);

        if (rejected > 0)
        {
            LastError = $"{rejected} rechazado{(rejected == 1 ? "" : "s")} en este lote. Toca ↩ para reintentar o descartar.";
        }

        // Limpiamos filas aprobadas/en-proceso para que el cajero pueda seguir
        // agregando sin acumular. Las rechazadas se quedan para corrección.
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (Rows[i].Status is ActivationRowStatus.Approved or ActivationRowStatus.PendingReview)
            {
                Rows.RemoveAt(i);
            }
        }
    }
}
