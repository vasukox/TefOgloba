using Android.App;
using Android.Runtime;

namespace Permoda.Pay.Maui;

// Faltaba esta clase por completo. Sin un Application-derived class marcado
// con [Application] (y sin android:name en el <application> del manifest), Android arranca
// la app con el Application genérico del framework y MainActivity SÍ se crea con normalidad
// (por eso la app "abre"), pero MauiProgram.CreateMauiApp() nunca se invoca: no se construye
// el MauiApp, no se instancia App ni AppShell, y ningún log de [MauiProgram]/[App]/[AppShell]
// llega a dispararse. No hay excepción que ver en logcat porque, desde Android, nada falló;
// simplemente no existe ningún contenido MAUI que mostrar. Resultado: pantalla negra sin
// ningún rastro en logs. Esta clase es la pieza que conecta Android con MAUI.
[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
