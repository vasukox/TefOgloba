# Documentación — Permoda.Pay Ogloba

Índice de la documentación del módulo. Cada documento tiene un dueño temático y una única
responsabilidad: si un dato aparece en dos sitios, uno de los dos es la fuente y el otro enlaza.

> **Antes de empezar**: lee el **[compendio maestro `00-COMPENDIO.md`](00-COMPENDIO.md)**. Es el mapa
> navegable de toda la documentación, con escenarios cubiertos, comandos habituales, capa de
> arquitectura, ciclo de 3 pasos de Ogloba y reglas de oro. Si vienes a actualizar este módulo
> por primera vez, ese archivo te dice por dónde.

## Fuentes de verdad

| Tema | Documento | Contiene |
| --- | --- | --- |
| Compendio maestro | [00-COMPENDIO.md](00-COMPENDIO.md) | Mapa navegable de toda la documentación, con escenarios cubiertos, comandos habituales y reglas de oro |
| Arquitectura | [ARQUITECTURA.md](ARQUITECTURA.md) | Capas, reglas de dependencia, proyectos, componentes clave y su responsabilidad |
| **Ambientes** | [AMBIENTES.md](AMBIENTES.md) | Los tres ambientes, sus URLs, códigos de producto, credenciales, tiendas y cómo se selecciona cada uno |
| Integración HiPOS | [INTEGRACION_HIPOS.md](INTEGRACION_HIPOS.md) | Contrato de ICG: `apk_name`, acciones, banderas de comportamiento, extras de entrada y salida, comprobantes |
| API de Ogloba | [OGLOBA_API_REFERENCE.md](OGLOBA_API_REFERENCE.md) | Referencia REST completa: 18 endpoints, schemas, códigos de error, changelog, estado de implementación |
| Flujos de operación | [FLUJOS_DE_OPERACION.md](FLUJOS_DE_OPERACION.md) | Ciclo de 3 pasos de Ogloba, redención, activación (física + virtual + captura de cliente), recuperación, unidades monetarias, pantallas |
| Seguridad | [SEGURIDAD.md](SEGURIDAD.md) | Cifrado en reposo, credenciales, firma de la APK, permisos, cumplimiento HiOSTORE |
| Build y despliegue | [BUILD_Y_DESPLIEGUE.md](BUILD_Y_DESPLIEGUE.md) | Compilación, empaquetado, firma, instalación, actualización y alta en CloudLicense |
| Pruebas y certificación | [PRUEBAS_Y_CERTIFICACION.md](PRUEBAS_Y_CERTIFICACION.md) | 170 pruebas automatizadas + 12 escenarios manuales UAT + captura de evidencia |
| Operación y soporte | [OPERACION_Y_SOPORTE.md](OPERACION_Y_SOPORTE.md) | Runbook: diagnóstico con ADB/logcat, síntomas conocidos y su causa raíz |
| Decisiones técnicas | [DECISIONES.md](DECISIONES.md) | Registro de decisiones: qué se decidió, por qué, y qué evidencia la sostiene |
| Estado y pendientes | [ESTADO.md](ESTADO.md) | Qué está listo, qué falta, qué depende de terceros y qué riesgo tiene |

Fuera de `docs/`, en la raíz del repositorio:

| Documento | Contiene |
| --- | --- |
| [../README.md](../README.md) | Tarjeta resumen del módulo, capacidades, arranque rápido |
| [../AGENTS.md](../AGENTS.md) | Convenciones para contribuir: comandos, estilo, reglas antes de hacer merge |
| [../PRODUCT.md](../PRODUCT.md) | Definición de producto: usuarios, propósito, alcance y compromisos de marca |
| [../DESIGN.md](../DESIGN.md) | Sistema de diseño de la UI: tokens de color, tipografía, componentes y reglas |
| [../CHANGELOG.md](../CHANGELOG.md) | Bitácora de cambios por versión |

## Archivo

[`archivo/`](archivo/) guarda documentos históricos que **no son fuente de verdad**: se conservan
por trazabilidad de cómo se llegó a la implementación actual, y pueden contradecir el código. No
citarlos como referencia normativa.

## Cómo mantener esta documentación

1. **Un dato, un lugar.** Antes de agregar un dato, buscar si ya vive en otro documento y enlazarlo.
2. **Lo que se afirma se verifica contra el código o contra la terminal.** Cada afirmación operativa
   debe poder rastrearse a un archivo del repositorio o a una medición fechada.
3. **Los porqués van a [DECISIONES.md](DECISIONES.md).** Los documentos temáticos describen el
   comportamiento actual; el registro de decisiones explica por qué es así y qué se descartó.
4. **La documentación describe el sistema, no el proceso con el que se construyó.** Un lector nuevo
   no tiene acceso al backlog, así que un identificador de ticket no explica nada por sí solo: el
   documento tiene que sostenerse sin él. La trazabilidad con la Historia de Usuario vive en el
   control de versiones (rama, commit, PR) y en el tablero.
5. **Sin secretos.** Contraseñas, keystores y credenciales de producción no van en Markdown; van en
   `SecureStorage`, en variables de entorno o en el gestor de secretos del equipo.
