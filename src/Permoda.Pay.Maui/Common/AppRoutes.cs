namespace Permoda.Pay.Maui.Common;

public static class AppRoutes
{
    public const string Home = "//home";
    public const string Setup = "//setup";

    // Destinos de los módulos operativos. Se usan también cuando HiPOS levanta el TEF: el
    // intent de cobro decide a cuál de los dos entra el cajero.
    public const string Activate = "//activate";
    public const string Redeem = "//redeem";

    /// <summary>
    /// Elección del tipo de bono (físico o virtual) antes de activar. Ruta GLOBAL: se empuja
    /// sobre Inicio, así el botón físico de atrás vuelve al menú sin pasos raros.
    /// </summary>
    public const string ActivateMode = "activatemode";

    /// <summary>
    /// Ingreso del cajero: usuario y contraseña. ÚNICA puerta de entrada al turno.
    /// <para>
    /// Ruta GLOBAL (sin "//"): se empuja sobre la pestaña activa en vez de ser un ítem raíz del
    /// Shell. Como ítem raíz, el Shell montaba la misma instancia de página en dos padres y
    /// aparecía una copia vacía encima de la buena.
    /// </para>
    /// <para>
    /// Antes existía además una ruta "login" que abría una pantalla distinta: esa daba el turno
    /// SOLO con elegir un nombre, sin pedir contraseña, y hasta permitía escribir un cajero que no
    /// estaba dado de alta. Convivían dos puertas y una no tenía cerradura. Se eliminó.
    /// </para>
    /// </summary>
    public const string CashierLogin = "cashierlogin";

    /// <summary>Paso 3 del alta: el administrador registra los cajeros con su contraseña.</summary>
    public const string CashierSetup = "//cashiersetup";

    /// <summary>
    /// La caja YA configurada repartiendo su configuración al resto de la tienda.
    /// <para>
    /// Ruta GLOBAL a propósito: el servicio de red vive lo que dura la página, así que tiene que
    /// apilarse y descartarse. Como ítem del Shell quedaría montada de fondo y el servicio
    /// sobreviviría a salir de ella — una caja ofreciendo la llave de producción sin nadie
    /// mirando.
    /// </para>
    /// </summary>
    public const string ReplicationHost = "replicationhost";

    /// <summary>La caja NUEVA copiando la configuración de otra. También global, por simetría.</summary>
    public const string ReplicationJoin = "replicationjoin";

    public const string Admin = "admin";
}
