# Registro de decisiones técnicas

Decisiones que no se deducen leyendo el código, y que costaron trabajo o dinero descubrir. El
formato es fijo: **contexto**, **decisión**, **consecuencia** y **evidencia**. Una decisión sin
evidencia es una preferencia; aquí solo entran las que se midieron.

Los documentos temáticos describen *qué* hace el sistema. Este describe *por qué*, y qué se
descartó.

---

<a id="d-01"></a>
## D-01 · El `apk_name` se lee del APK que distribuye CloudLicense, no se adivina

**Contexto.** HiPOS invoca el módulo con un intent implícito cuya acción contiene el `apk_name` que
ICG registró en CloudLicense. Se probaron cinco nombres y ninguno acertó. El fallo no deja rastro:
sin resolución, Android no arranca nada y no hay línea en `logcat` — "no nos llamaron" es
indistinguible de "nos llamaron con otro nombre". El efecto era un bucle cerrado: la nube pedía un
nombre que el APK no declaraba, HiPOS lo leía como "módulo no instalado", descargaba el APK de
CloudLicense (que resultó ser el nuestro, sin ese nombre) y volvía a empezar en cada arranque.
Instalar a mano no lo rompía.

**Decisión.** El valor real (`permoglobal`) se obtuvo descargando el APK que CloudLicense distribuye
y leyendo su manifiesto con `aapt2`. El manifiesto declara además cuatro candidatos como red de
seguridad, y el despacho interno se hace por la **operación** (último segmento de la acción), no por
el `apk_name`. Cada intent registra con qué nombre llegó.

**Consecuencia.** Ante cualquier duda de enrutamiento, la fuente de verdad es el APK de la nube, no
el repositorio. `permoda` **no se declara**: pertenece a Sistecrédito (`com.pos2pay`) y declararlo
hacía que Android mostrara el selector "Completar acción usando…" en mitad de una venta.

**Evidencia.** Lectura con `aapt2` del APK de CloudLicense. Verificación en terminal con
`cmd package query-activities`.

---

<a id="d-02"></a>
## D-02 · Los importes viajan a Ogloba en pesos enteros

**Contexto.** Un intento previo multiplicaba el importe por 100 con el argumento de que "el
documento lo exige". El documento no dice eso en ninguna parte.

**Decisión.** A Ogloba se envían pesos enteros. La conversión desde la escala de HiPOS (importe
×100, con los dos últimos dígitos como decimales) se hace **en el mapper**, no en el adaptador de
Ogloba: la frontera de escala pertenece al contrato de ICG.

**Consecuencia.** `Money.MinorUnits` y `AmountMinorUnits` conservan un nombre engañoso y contienen
pesos. Los céntimos que lleguen se truncan (nunca se cobra de más) y se registran.

**Evidencia.** Sandbox, 2026-07-28: el importe ×100 se rechazó con `errorCode 73` ("Wrong activation
amount"); el importe en pesos respondió `isSuccessful=true` con la misma tienda y producto.

---

<a id="d-03"></a>
## D-03 · Persistir la transacción antes del Step 2

**Contexto.** Ogloba no tiene operaciones atómicas: una operación son tres llamadas. La tablet puede
apagarse, quedarse sin red o ser cerrada por el usuario en cualquier punto.

**Decisión.** La transacción autorizada en el Step 1 se guarda en la base cifrada **antes** de
confirmar. Si la persistencia falla, se reversa lo autorizado antes de continuar.

**Consecuencia.** Cualquier interrupción entre el Step 1 y el Step 3 es recuperable en el siguiente
arranque, y nunca queda dinero bloqueado. Autorizar sin poder recordarlo se considera peor que no
autorizar.

**Evidencia.** Cubierto por pruebas de `ProcessSaleHandler`, incluidas las rutas de fallo de
persistencia y de reverso.

---

<a id="d-04"></a>
## D-04 · Deshacer un cobro con bono se rechaza por las dos puertas

**Contexto.** HiPOS puede pedir deshacer un cobro por dos intents: `REFUND` (nota de crédito) y
`VOID_TRANSACTION` (anulación tras un `UNKNOWN_RESULT`). Durante un tiempo solo se cerraba `REFUND`.
Al activar `ExecuteVoidWhenAvailable=true` —como hipótesis para desbloquear la papelera de la línea
de pago— HiPOS empezó a enviar los abonos como `VOID_TRANSACTION`, que se colaron por el camino
abierto y se respondieron `ACCEPTED` en silencio.

**Decisión.** Los dos intents se rechazan con los campos del contrato (`TransactionResult=FAILED`,
`ErrorMessageTitle`, `ErrorMessage`) y con un motivo redactado según lo que el cajero está
intentando. `ExecuteVoidWhenAvailable` queda en `false`, para que cada intent signifique lo que
dice el contrato.

**Consecuencia.** Entre mentirle al POS y decirle que no, se elige decirle que no: responder
`ACCEPTED` sin llamar a Ogloba era lo peor de los dos mundos —el POS daba la operación por hecha y
el saldo no volvía—. Mientras quede una sola puerta abierta, basta un cambio de configuración del
POS para que todo vuelva a colarse sin que nadie se entere.

**Evidencia.** Terminal, 2026-09-02 (08:45–08:46): cuatro abonos dados por hechos sin mover un
peso. La hipótesis de la papelera además no funcionó: la línea siguió sin papelera.

---

<a id="d-05"></a>
## D-05 · El identificador del medio de pago no se inventa

**Contexto.** El módulo fiscal reportaba que el medio de pago quedaba sin identificar. Se copió la
solución de otro módulo TEF de las mismas terminales: poner un `PaymentMeanId` por defecto (`2`, el
medio "Tarjeta" sobre el que consolida Sistecrédito).

**Decisión (2026-08-28).** El medio de pago solo se nombra si HiPOS dijo cuál es. Sin id, el
`ModifyDocumentResult` sigue enriqueciendo la línea que HiPOS ya eligió con el `AuthorizationId`,
que es lo único que el módulo fiscal necesita del módulo.

**Consecuencia.** Salió peor nombrar un medio ajeno: HiPOS aplicaba el pago sobre **ese** medio en
lugar del de Ogloba, y el POS respondió "la forma de pago tarjeta de crédito no tiene
equivalencia". Si volviera el error de folios, hace falta el id real del alta de Ogloba en
CloudLicense, y ese lo tiene ICG.

**Corrección (2026-09-08).** La premisa de la decisión anterior era falsa. Sin id, HiPOS **no**
respeta la línea que ya eligió: aplica el pago sobre su medio por defecto, y la venta se imprime
como **EFECTIVO**. Reportado desde caja con el bono ya cobrado — el cliente pagó con bono y la
factura dice efectivo.

Lo que la decisión anterior acertó es que no se nombra un medio **ajeno**. Lo que le faltaba era
el id real de Ogloba, y ya lo tenemos: **`1000037`**, leído del documento que HiPOS mandó a la
terminal el 2026-09-01, en la línea que el propio POS rotula `OGLOBA`.

Regla vigente, por precedencia:

1. El `PaymentMeanId` que mande HiPOS, si lo manda.
2. Si no —lo habitual en el intent de venta, porque la lista `PaymentMeans` del documento todavía
   está vacía cuando nos llama— el de Ogloba, `HiPosPaymentOrchestrator.OglobaPaymentMeanId`.
3. Nunca vacío, y nunca el de otro módulo.

El paso 2 se registra en INFO al aplicarse: si una tienda tuviera otro id, la venta saldría con un
medio equivocado y el log lo dice en claro. Lo correcto a futuro es que el id viaje en el XML
`Parameters` del `INITIALIZE`; hay que pedírselo a ICG.

**Evidencia.** Terminal, 2026-08-28. Además, el 2026-09-02 se confirmó que el error "no cuenta con
folios asociados" (código 109) es respuesta del backend fiscal `hioposreports-co` —la tienda sin
rango de folios DIAN—, no consecuencia de este campo.

---

<a id="d-06"></a>
## D-06 · Un solo comprobante: el del cliente

**Contexto.** Se armaba el comprobante del comercio y se enviaba en los dos extras
(`MerchantReceipt` y `CustomerReceipt`).

**Decisión.** Se envía únicamente `CustomerReceipt`. Se emite también cuando la operación falla.

**Consecuencia.** La térmica sacaba dos tirillas y ambas decían "COPIA COMERCIO": el cliente se
llevaba una copia que no era la suya. El respaldo del comercio es la factura que imprime HiPOS. En
un fallo, el cliente se lleva constancia de que el cobro se intentó y de por qué no pasó.

---

<a id="d-07"></a>
## D-07 · La activación de bonos salió del intent de HiPOS

**Contexto.** Inicialmente se contempló activar bonos como una operación más del intent
`TRANSACTION`.

**Decisión.** La activación es un flujo manual, con su propia sesión de cajero (usuario y
contraseña) y sus propios pasos: medios de pago desglosados y confirmación del cliente. El intent de
HiPOS solo redime, como medio de pago.

**Consecuencia.** Meter la activación dentro del intent significaría activar sin esa autenticación.
El cajero cobra en el POS por el medio que corresponda y después entra al módulo a activar. El
contrato de ICG tampoco define un `TransactionType=ACTIVATION`, así que no había forma limpia de
expresarlo.

---

<a id="d-08"></a>
## D-08 · `SupportsTransactionQuery` queda en `false`

**Contexto.** El módulo implementa `QUERY_TRANSACTION` devolviendo el saldo del bono. Declarar la
bandera en `true` haría que HiPOS lo enviara.

**Decisión.** La bandera queda en `false` hasta implementar la semántica real.

**Consecuencia.** Para el contrato de ICG, `QUERY_TRANSACTION` significa "recuperar una venta que
terminó en `UNKNOWN_RESULT`", y si el módulo responde `ACCEPTED`, HiPOS da esa venta por cobrada.
Devolver un saldo no es eso: declararlo en `true` haría que el POS cerrara ventas sin cobrar
después de un resultado incierto.

---

<a id="d-09"></a>
## D-09 · `CallOnTotalizationCanceled` en `true`, aceptando sin devolver saldo

**Contexto.** El caso que motivó todo: se facturó con bono, la DIAN no integró, y el cajero
necesitaba soltar la línea de pago. La línea aparecía resaltada y sin papelera. Se intentó el camino
por `GET_BEHAVIOR`/`REFUND` sin éxito: en 8 MB de log de terminal, HiPOS **jamás** envió `REFUND` ni
`VOID_TRANSACTION`. Sencillamente no preguntaba, y ninguna bandera documentada cambiaba eso.

**Decisión.** Declarar `CallOnTotalizationCanceled=true` y atender el intent
`TOTALIZATION_CANCELED` respondiendo `OK`, **sin** llamar a Ogloba, dejando en el log una
advertencia con la referencia y el importe.

**Consecuencia.** El POS suelta la línea, pero **el saldo del bono no regresa**. El rastro para
cuadrarlo queda en el log. Devolver el saldo exigiría `/voidTransaction`, que hoy ningún caso de uso
invoca. Quien opere debe conocer este comportamiento.

**Evidencia.** Bandera confirmada por ICG el 2026-09-01; no aparece en la revisión 3.8 del PDF del
contrato que tenemos, es posterior.

> Nota para quien retome esto: `/reversal` **no** sirve para deshacer una redención confirmada. Solo
> deshace un Step 1 que quedó en `Requested` porque no se supo su resultado. Sobre una transacción
> ya confirmada devuelve error y el saldo no vuelve — el cliente quedaría sin factura y sin dinero.

---

<a id="d-10"></a>
## D-10 · Páginas transient, ViewModels singleton, un solo contenedor de servicios

**Contexto.** Todo era singleton. Desde que el módulo comparte tarea con HiPOS, cada cobro termina
con `Finish()` de la Activity, y al abrir el siguiente MAUI pedía a DI el mismo `AppShell`, con su
handler de plataforma ya desconectado. Aparte, se construía un segundo `IServiceProvider` con
`BuildServiceProvider()`.

**Decisión.** Lo que tiene handler de plataforma (páginas, `AppShell`) es **transient**; lo que
guarda estado (ViewModels) es **singleton**. Se captura el `IServiceProvider` del host de MAUI y no
se construye ninguno paralelo. Los ViewModels que guardan credenciales (ingreso de cajero, gestión
de usuarios) también son transient: un singleton conservaría la contraseña escrita y el estado de
desbloqueo entre turnos.

**Consecuencia.** Se eliminaron dos crashes que se veían como "la app a veces no levanta"
(`ObjectDisposedException: ShellToolbarTracker` y clave duplicada en el `ResourceDictionary`), y el
contenedor paralelo dejó de duplicar singletons y `HttpClient`.

**Evidencia.** Terminal, 2026-08-28: tres crashes seguidos en 60 segundos.

---

<a id="d-11"></a>
## D-11 · El intent se procesa en `OnResume`, no en `OnCreate`

**Contexto.** En arranque en frío, los servicios de MAUI todavía no existen cuando corre `OnCreate`
de la Activity que recibe el intent.

**Decisión.** El intent se guarda en `OnCreate` (o en `OnNewIntent`) y se procesa en `OnResume`,
cuando MAUI ya garantizó que los servicios están montados.

**Consecuencia.** Procesarlo en `OnCreate` producía o un `NullReferenceException`, o el arranque del
host de MAUI abriendo la Activity principal por encima, con HiPOS esperando una respuesta que nunca
llegaba.

---

<a id="d-12"></a>
## D-12 · *Marshal methods*, *trimming* y AOT apagados

**Contexto.** Están activos por defecto en configuraciones optimizadas.

**Decisión.** `AndroidEnableMarshalMethods=false`, `PublishTrimmed=false`, `AndroidLinkMode=None`,
`RunAOTCompilation=false`.

**Consecuencia.** Con *marshal methods*, las compilaciones incrementales dejaban `libxamarin-app.so`
con el registro JNI de la build anterior y la app moría en el arranque con `UnsatisfiedLinkError`,
antes de ejecutar una línea propia; desde HiPOS se veía como "el módulo no se puede iniciar". Con
*trimming*, el linker eliminaba constructores de vistas infladas por reflexión (`Arg_NoDefCTor`).
El AOT exige *trimming*, así que también queda apagado. El costo es unos milisegundos de arranque;
el beneficio es no entregar un APK muerto sin darse cuenta.

**Evidencia.** Terminal, 2026-08-28: tres compilaciones incrementales seguidas produjeron APK que
crashean; borrar `obj/` y `bin/` produjo una sana en cada caso.

---

<a id="d-13"></a>
## D-13 · Las tres configuraciones se firman con la misma llave

**Contexto.** Android exige que toda actualización venga firmada con la misma llave que la
instalación previa.

**Decisión.** `Debug`, `UAT` y `Release` se firman con la misma llave, leyendo las contraseñas de
variables de entorno. Si faltan, no se firma con esa llave en lugar de romper el build de quien
clone el repositorio sin acceso a ella.

**Consecuencia.** Se puede pasar de una build de prueba a una de producción en una terminal sin
desinstalar. Cualquier **rotación** de llave, en cambio, obliga a desinstalar la primera vez, lo que
borra PIN, credenciales y cajeros de esa caja
([SEGURIDAD.md](SEGURIDAD.md#estado-del-material-de-firma)).

---

<a id="d-14"></a>
## D-14 · El producto digital del sandbox es `113816`

**Contexto.** La activación virtual nunca funcionó con el producto `113815` (*BONO REGALO B2B*):
rechazaba **cualquier** importe con `errorCode 73`, incluido el mínimo exacto de su propio catálogo.
Se atribuyó el fallo a la tienda ("Unknown store") durante un tiempo.

**Decisión.** Usar `113816` (*TARJETA OBSEQUIO B2C*) en sandbox, con rango $30.000 – $500.000.

**Consecuencia.** No era la tienda: `/test`, `/getProducts`, `/orderCreation` y `/orderCancel`
responden correctamente para la tienda de pruebas, o sea que Order management sí está habilitado.
Era el producto. En producción el valor declarado hoy (`113811`, dotación corporativa) **no** es el
equivalente B2C y hay que confirmarlo con Ogloba antes de salir.

**Evidencia.** Sandbox, 2026-08-24, tienda `K00037`.

---

<a id="d-15"></a>
## D-15 · La versión de la API queda fija en `2.18`

**Contexto.** La colección Postman pública de Ogloba usa `2.20` y apunta a `srl-ts`, un tenant demo.

**Decisión.** `2.18` en los tres ambientes, según el correo de Ogloba para el tenant de KOAJ.

**Consecuencia.** No subir la versión sin confirmación escrita de Ogloba para `co-prod`. La
diferencia entre `2.18` y `2.20` es aditiva (campos nuevos en `/getProducts` y `/getBuInfo`), así
que no hay urgencia funcional.

**Evidencia.** Verificado en vivo contra el sandbox con `/activation`, `/redemption`, `/balance` y
`/getProducts`.

---

<a id="d-16"></a>
## D-16 · Dos topes de tiempo distintos, según si la operación mueve dinero

**Contexto.** Un único tope obligaba a elegir entre esperar minuto y medio en una consulta de saldo
o cortar un cobro antes de que Ogloba responda.

**Decisión.** 90 s para las operaciones que mueven dinero; 15 s para las de solo lectura.

**Consecuencia.** Cortar antes que Ogloba en un cobro produce el peor estado posible: la operación
puede haberse aplicado allá y no saberlo acá. En una consulta, en cambio, un fallo no deja nada a
medias, y ahí vive la latencia que el cajero siente con el cliente en el mostrador.

---

<a id="d-17"></a>
## D-17 · Los mensajes de error los traduce Ogloba

**Contexto.** Mantener una tabla local de más de 80 códigos traducidos.

**Decisión.** Enviar `Accept-Language: es-co` en todas las llamadas y mostrar el `errorMessage` que
devuelve Ogloba. El mapeo local se conserva **solo** para clasificar el fallo como rechazo o como
incierto —esa clasificación decide si se dispara `/reversal`— y como respaldo de texto.

**Consecuencia.** No hay una tabla de traducciones que se desincronice con Ogloba.

**Evidencia.** Verificado 2026-07-28: "Wrong activation amount" → "Monto de activación no válido".

---

<a id="d-18"></a>
## D-18 · Una sola puerta de ingreso de cajero, con contraseña

**Contexto.** Convivían dos rutas de ingreso. Una de ellas daba el turno **solo con elegir un
nombre**, sin contraseña, y permitía incluso escribir un cajero que no estaba dado de alta.

**Decisión.** Una única pantalla de ingreso, con usuario y contraseña, sobre el registro de cajeros
de esa caja. La otra ruta se eliminó.

**Consecuencia.** La operación queda firmada con un cajero real —en la bitácora y en lo que se envía
a Ogloba—, y en una caja compartida no se atribuye dinero a quien no lo movió. Los cobros ordenados
por HiPOS son la excepción: ahí el cajero ya se identificó en el POS para poder facturar, y meter un
login en medio de una venta es fricción sobre una identificación que ya ocurrió.

---

<a id="d-19"></a>
## D-19 · El contrato de HiPOS vive en un proyecto sin dependencias de Android

**Contexto.** La parte del sistema donde un error cuesta dinero es qué se le responde al POS y con
qué forma exacta.

**Decisión.** Todo eso vive en `Permoda.Pay.Maui.HiPos`, que compila a `net10.0` y **no referencia
tipos de Android**. La frontera se cruza con tres puertos (`IHiPosResponseSink`,
`IHiPosStoreConfigurationSink`, `IHiPosCardCapture`) que implementa la capa Android.

**Consecuencia.** 73 pruebas cubren el contrato y corren sin emulador ni dispositivo.

---

<a id="d-20"></a>
## D-20 · El resultado de una operación se comunica con un toast nativo

**Contexto.** El resultado se mostraba en un banner de pantalla completa.

**Decisión.** Toast nativo de Android para el desenlace de una acción. `StatusBannerView` se
conserva para avisos persistentes que deben quedarse a la vista.

**Consecuencia.** El cajero no pierde de vista el formulario del lote mientras confirma resultado
por resultado.

---

<a id="d-21"></a>
## D-21 · Un bono no se anula nunca, y el hash de contraseña no se toca

**Contexto.** Dos puntos quedaron abiertos en la revisión de confiabilidad del 2026-09-22 y se
cerraron el 2026-09-23 por decisión de KOAJ, no por una limitación técnica. Se anotan porque los
dos parecen defectos desde el código y no lo son: sin este registro, alguien los va a "arreglar".

**Decisión.**

*Anulación de un bono.* No existe. Es política de KOAJ: un cobro con bono no se deshace, ni desde
el POS ni por ningún otro camino del módulo. Lo que ahora se llamaba «responder ACCEPTED sin
devolverle el saldo a Ogloba» no es un defecto pendiente de implementar: el escenario no ocurre, y
el módulo lo impide.

*Hash de las contraseñas de cajero.* Se queda como está. Se propuso agregarle sal —hoy es SHA-256
sin sal— y KOAJ decidió no cambiarlo: funciona, y migrar el formato en 512 tiendas arriesga dejar
cajas sin poder operar por un beneficio que no se considera prioritario. Si algún día se cambia,
tiene que ser con migración gradual (validar el formato viejo y actualizar al nuevo en el siguiente
ingreso correcto), nunca de golpe.

**Consecuencia.** La prohibición de anular está cerrada en los cuatro caminos por los que HiPOS
podría pedirla, y eso hay que mantenerlo: `REFUND` y `VOID_TRANSACTION` se rechazan, y las banderas
`ExecuteVoidWhenAvailable` y `SupportsTransactionQuery` van en `false`. Las dos banderas son
sutiles y ya fallaron una vez — ver [D-05](#d-05) y la nota de `HandleBehavior`.

**Evidencia.** Verificado sobre el código el 2026-09-23: los únicos caminos que responden
`ACCEPTED` son una venta realmente autorizada y `BATCH_CLOSE`, donde no se mueve plata (cada venta
ya se reconcilió individualmente). El 2026-09-01 se probó `ExecuteVoidWhenAvailable=true` y hubo
que revertirlo al día siguiente: los abonos dejaron de llegar como `REFUND`, se colaron por
`VOID_TRANSACTION` y se respondieron `ACCEPTED` en silencio — cuatro abonos dados por hechos sin
mover un peso.

---

## Cómo agregar una decisión

1. Siguiente identificador libre, con su ancla `<a id="d-NN"></a>`.
2. Las cuatro secciones: contexto, decisión, consecuencia, evidencia.
3. La evidencia debe ser verificable: una medición fechada, una respuesta de un tercero, o una
   prueba automatizada que la sostenga.
4. Si la decisión invalida una anterior, decirlo en la nueva y anotarlo en la antigua. **No borrar
   la antigua**: el valor de este documento es que explica por qué no se volvió a intentar algo.
