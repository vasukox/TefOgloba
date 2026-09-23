# Archivo

Documentos históricos. **No son fuente de verdad.**

Se conservan porque explican cómo se llegó a la implementación actual —qué se sabía, cuándo, y con
qué información se tomaron decisiones—, pero contienen afirmaciones que el código ya desmintió.
Citar un documento de esta carpeta como referencia normativa es un error.

Para el estado actual del sistema, ir siempre al [índice de documentación](../README.md).

| Documento | Qué es | Por qué está archivado |
| --- | --- | --- |
| [DOSSIER_INTEGRACION_2026.md](DOSSIER_INTEGRACION_2026.md) | Dossier técnico inicial de la integración: endpoints de Ogloba, contrato de HiPOS, códigos de error, escenarios de certificación, correspondencia con el proveedor | Fue el punto de partida y quedó superado por la implementación. Su descripción del contrato de HiPOS (cuatro acciones) y de la API de Ogloba (nueve endpoints) es incompleta frente a lo que el módulo hace hoy. Contiene además correspondencia y credenciales de prueba que no deben propagarse |
| [PLAN_DE_TRABAJO_2026-08.md](PLAN_DE_TRABAJO_2026-08.md) | Plan y cronograma del ciclo de construcción de agosto de 2026 | Es un documento de proyecto, no de producto. Las fechas y los conteos que cita ya no corresponden |

## Qué se rescató de aquí, y a dónde fue

| Contenido del archivo | Documento vigente |
| --- | --- |
| Contrato de intents de HiPOS, comprobantes, banderas de comportamiento | [INTEGRACION_HIPOS.md](../INTEGRACION_HIPOS.md) |
| Endpoints de Ogloba, schemas y códigos de error | [OGLOBA_API_REFERENCE.md](../OGLOBA_API_REFERENCE.md) |
| Ciclo de 3 pasos y máquina de estados | [FLUJOS_DE_OPERACION.md](../FLUJOS_DE_OPERACION.md) |
| Escenarios de certificación y captura de evidencia | [PRUEBAS_Y_CERTIFICACION.md](../PRUEBAS_Y_CERTIFICACION.md) |
| Compromisos HiOSTORE | [SEGURIDAD.md](../SEGURIDAD.md#7-cumplimiento-hiostore) |
| Tiendas, credenciales y datos de prueba | [AMBIENTES.md](../AMBIENTES.md) |
