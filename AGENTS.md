# Guía de contribución

Reglas para trabajar en este repositorio. Lo que **hace** el sistema está documentado en
[`docs/`](docs/README.md); acá está solo cómo se trabaja sobre él.

## Antes de tocar algo

Este módulo mueve dinero de clientes reales en 512 tiendas, dentro de un POS que no controlamos.
Tres lecturas obligatorias según lo que vayas a cambiar:

| Vas a tocar… | Lee primero |
| --- | --- |
| Cualquier cosa | [docs/AMBIENTES.md](docs/AMBIENTES.md) y [docs/DECISIONES.md](docs/DECISIONES.md) |
| Lo nuevo, lo viejo, lo que está en curso | [docs/CHANGELOG.md](../CHANGELOG.md) |
| La respuesta al POS, los intents, los comprobantes | [docs/INTEGRACION_HIPOS.md](docs/INTEGRACION_HIPOS.md) |
| Un importe, una escala, una conversión | [docs/FLUJOS_DE_OPERACION.md §1](docs/FLUJOS_DE_OPERACION.md#1-unidades-monetarias) |
| Captura de datos del cliente en activación | [docs/FLUJOS_DE_OPERACION.md §4.3](docs/FLUJOS_DE_OPERACION.md#43-captura-de-datos-del-cliente-física-y-virtual) y [`CustomerMobileNoBuilder`](../src/Permoda.Pay.Application/CustomerMobileNoBuilder.cs) |
| Una llamada a Ogloba | [docs/OGLOBA_API_REFERENCE.md](docs/OGLOBA_API_REFERENCE.md) |
| Configuración de compilación o de firma | [docs/BUILD_Y_DESPLIEGUE.md](docs/BUILD_Y_DESPLIEGUE.md) |

[docs/DECISIONES.md](docs/DECISIONES.md) existe para que no se vuelva a intentar algo que ya se
midió y falló. Si tu cambio contradice una decisión registrada, la conversación es sobre esa
decisión, no sobre el código.

## Comandos

```pwsh
# Compilar
dotnet restore Permoda.Pay.sln
dotnet build   Permoda.Pay.sln -c Debug
dotnet build   Permoda.Pay.sln -c UAT

# Probar (170 pruebas, 5 suites)
dotnet test Permoda.Pay.sln -c Debug

# Formato
dotnet format Permoda.Pay.sln
dotnet format Permoda.Pay.sln --verify-no-changes
```

Configuraciones válidas: `Debug`, `UAT`, `Release`. Empaquetado y firma:
[docs/BUILD_Y_DESPLIEGUE.md](docs/BUILD_Y_DESPLIEGUE.md).

## Reglas de código

1. **Las dependencias apuntan hacia adentro.** Domain no conoce a nadie; Application solo conoce
   Domain; Infrastructure y Maui.HiPos no se conocen entre sí. Hay pruebas que lo verifican: si
   inviertes una dependencia, el build falla.
2. **Los fallos de negocio son valores, no excepciones.** `Result` en dominio, `PortResult` en
   aplicación. Un rechazo de Ogloba es un retorno, no un `throw`.
3. **`Permoda.Pay.Maui.HiPos` no referencia tipos de Android.** Es lo que permite probar el contrato
   del POS sin dispositivo. Si necesitas algo de la plataforma, agrégalo como puerto.
4. **Ningún dato sensible sale en logs.** PAN enmascarado siempre; nunca contraseñas, PIN ni
   passphrase.
5. **Las rutas del Shell son constantes** de `Common/AppRoutes.cs`, nunca literales en XAML.
6. **Los colores y espaciados salen de los tokens** de `Resources/Styles/`, nunca hex en línea. Ver
   [DESIGN.md](DESIGN.md).
7. **Todo handler de un intent responde y cierra.** Un camino que termina sin resultado deja a HiPOS
   esperando indefinidamente y el cajero pierde la venta.

## Comentarios en el código

Este repositorio usa comentarios largos a propósito, y no son decoración: documentan fallos medidos
en terminal cuya causa no se parece al síntoma. Reglas:

* **Explica el porqué, no el qué.** Si el comentario repite lo que dice la línea siguiente, sobra.
* **Si documentas un fallo, incluye la evidencia**: qué se midió, cuándo, y qué se vio.
* **Que el comentario se entienda sin el backlog.** Quien lea el código en dos años no tiene
  acceso al tablero: escribe el contexto que hace falta, o enlaza el documento de `docs/`
  correspondiente, en lugar de dejar solo un identificador de ticket. La trazabilidad con la
  Historia de Usuario va donde corresponde —nombre de rama, commit y descripción del PR—, no
  como única explicación de una línea de código.
* **Un porqué que aplica a todo el sistema va a [docs/DECISIONES.md](docs/DECISIONES.md)**, y el
  comentario lo enlaza.

## Antes de hacer merge

```pwsh
dotnet format Permoda.Pay.sln --verify-no-changes
dotnet test   Permoda.Pay.sln -c Debug
```

Los dos deben pasar con **0 errores y 0 advertencias**.

Además:

* Si cambiaste el contrato con HiPOS o con Ogloba, valídalo en una terminal real y adjunta el
  `logcat`. Las pruebas no cubren el enrutamiento de intents, la impresión, el lector ni el módulo
  fiscal ([docs/PRUEBAS_Y_CERTIFICACION.md](docs/PRUEBAS_Y_CERTIFICACION.md)).
* Si cambiaste algo que la documentación afirma, actualiza el documento en el mismo cambio. Una
  documentación que contradice al código es peor que no tenerla.
* Si el cambio cierra un pendiente, muévelo en [docs/ESTADO.md](docs/ESTADO.md).

## Qué no se toca sin coordinar

| Valor | Con quién | Por qué |
| --- | --- | --- |
| `apk_name` (`permoglobal`) | ICG / CloudLicense | Es la llave con la que HiPOS encuentra el módulo. Cambiarlo sin actualizar el alta lo vuelve invisible, sin dejar rastro en el log |
| `ApplicationId` (`com.permoda.tefogloba`) | ICG / CloudLicense | Renombrarlo desinstala y reinstala en todas las terminales |
| `ApplicationVersion` / `ModuleVersionCode` | ICG / CloudLicense | Si no coinciden con el alta, HiPOS pide reinstalar el módulo en cada arranque |
| Llave de firma | Equipo Permoda.Pay | Una rotación obliga a desinstalar en cada terminal y borra su configuración |
| Ajustes de *marshal methods*, *trimming* y AOT | — | Están apagados por fallos medidos. Ver [docs/DECISIONES.md](docs/DECISIONES.md#d-12) |
| Tope de 90 s de las operaciones que mueven dinero | — | Cortar antes que Ogloba produce resultados inciertos |
