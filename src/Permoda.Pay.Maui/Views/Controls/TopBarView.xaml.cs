namespace Permoda.Pay.Maui.Views.Controls;

/// <summary>
/// Barra superior común de las pantallas operativas: menú, volver, marca, migaja de pan y chip
/// del cajero en turno. El chip lee del <c>Header</c> del ViewModel de la página; la migaja se
/// pasa por <see cref="ParentText"/> y <see cref="SectionText"/>.
/// </summary>
public partial class TopBarView : ContentView
{
    public static readonly BindableProperty ParentTextProperty = BindableProperty.Create(
        nameof(ParentText), typeof(string), typeof(TopBarView), string.Empty,
        propertyChanged: OnParentTextChanged);

    public static readonly BindableProperty SectionTextProperty = BindableProperty.Create(
        nameof(SectionText), typeof(string), typeof(TopBarView), "Inicio");

    public TopBarView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplySystemBarInset();
    }

    /// <summary>Nivel anterior de la migaja (p. ej. "Inicio"). Vacío en la pantalla de inicio.</summary>
    public string ParentText
    {
        get => (string)GetValue(ParentTextProperty);
        set => SetValue(ParentTextProperty, value);
    }

    /// <summary>Sección actual, en negrita.</summary>
    public string SectionText
    {
        get => (string)GetValue(SectionTextProperty);
        set => SetValue(SectionTextProperty, value);
    }

    /// <summary>
    /// El módulo entró desde HiPOS. En ese modo la barra queda cerrada: sin menú lateral y sin
    /// volver atrás, porque el cajero está a mitad de un cobro del POS y cualquier otra salida
    /// deja a HiPOS esperando una respuesta que nunca llega. La única salida es el botón que
    /// devuelve el control a HiPOS.
    /// </summary>
    private static bool InHiPosFlow => Services.HiPosFlow.IsActive;

    /// <summary>
    /// Instancia (no estática) porque el XAML enlaza contra ella con
    /// <c>Source={x:Reference Root}</c>, y un enlace de instancia no resuelve propiedades
    /// estáticas.
    /// </summary>
    public bool IsHiPosMode => InHiPosFlow;

    public bool ShowMenu => !InHiPosFlow;

    public bool ShowBack => HasParent && !InHiPosFlow;

    public bool ShowBackToHiPos => InHiPosFlow;

    /// <summary>Cierra el módulo sin cobrar y devuelve el control a HiPOS.</summary>
    public static System.Windows.Input.ICommand BackToHiPosCommand { get; } =
        new Command(() =>
        {
            Services.HiPosFlow.Complete(null);
            Services.HiPosFlow.CloseModuleScreen();
        });

    public bool HasParent => !string.IsNullOrWhiteSpace(ParentText);

    /// <summary>
    /// Separa la barra de la altura real de la barra de estado de Android.
    /// <para>
    /// Las pantallas ocultan la barra del Shell, así que el contenido pasa a dibujarse bajo la
    /// barra de estado del sistema —reloj, batería, notificaciones— y la marca quedaba tapada.
    /// Android 15+ además impone edge-to-edge, con lo que no basta con un margen fijo: se lee el
    /// inset real del sistema y se convierte de píxeles a unidades independientes de densidad,
    /// que es en lo que trabaja MAUI.
    /// </para>
    /// </summary>
    private void ApplySystemBarInset()
    {
        try
        {
            var activity = Platform.CurrentActivity;
            var decorView = activity?.Window?.DecorView;

            if (decorView is null)
            {
                return;
            }

            var insets = AndroidX.Core.View.ViewCompat
                .GetRootWindowInsets(decorView)?
                .GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.StatusBars());

            var topPixels = insets?.Top ?? 0;

            if (topPixels <= 0)
            {
                return;
            }

            var density = DeviceDisplay.Current.MainDisplayInfo.Density;
            Padding = new Thickness(0, topPixels / (density <= 0 ? 1 : density), 0, 0);
        }
        catch
        {
            // El inset es cosmético: si la plataforma no lo entrega, la barra se dibuja sin
            // separación extra antes que dejar la pantalla sin encabezado.
        }
    }

    private static void OnParentTextChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((TopBarView)bindable).OnPropertyChanged(nameof(HasParent));
}
