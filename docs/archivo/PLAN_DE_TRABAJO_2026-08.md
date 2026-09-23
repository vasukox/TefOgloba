# [ARCHIVADO] Plan de trabajo — agosto de 2026

> ⚠️ **Documento archivado.** Es el cronograma de un ciclo de construcción ya cerrado, no una
> descripción del sistema. Las fechas, los conteos de pruebas y los pendientes que menciona están
> superados. Para el estado actual: [ESTADO.md](../ESTADO.md).

Proyecto: integración de pagos con bonos Ogloba para KOAJ (Permoda).
Período cubierto: **1 → 25 de agosto de 2026**.

---

## 1. Fechas clave

| Fecha | Hito |
|---|---|
| Sáb 1 ago | Inicio del proyecto |
| 1 – 9 ago | Fases de construcción |
| Lun 10 ago | Pausa por otra integración en curso |
| 11 – 17 ago | Proyecto en stand-by |
| Mar 18 ago | Reanudación (hoy) |
| 18 – 24 ago | Trabajo pendiente (lectores, correos Ogloba, UI/UX) |
| Lun 24 ago | Inicio de QA y pruebas |
| Mar 25 ago | Cierre estimado de QA |

---

## 2. Lo que ya se hizo (1 – 9 de agosto)

### Fase 1 · Puesta en marcha (1 – 2 ago)
- Alineación con KOAJ y Ogloba sobre el alcance del módulo de pagos.
- Definición de la arquitectura general y de los entornos (desarrollo, UAT, producción).
- Configuración del repositorio, del proyecto Android (`apk_name = oglobapay`, `applicationId = com.permoda.tefogloba`) y de la base de URL/credenciales por tienda.

### Fase 2 · Reglas del negocio (3 – 4 ago)
- Modelado del dinero, identificadores y los tipos de transacción (venta, anulación, reembolso, recarga, activación).
- Construcción de la "máquina de estados" que gobierna el ciclo de vida de cada pago.
- Catálogo de errores propio del dominio.

### Fase 3 · Casos de uso (5 – 6 ago)
- Flujo principal de venta con sus tres pasos estrictos, control de tiempo de espera y manejo de reversos.
- Proceso de activación de bonos virtuales (orden + confirmación).
- Mecanismo de recuperación de pagos pendientes al arrancar la app o al reconectar.
- Conexión entre las pantallas y los casos de uso.

### Fase 4 · Conexión con Ogloba y datos seguros (7 – 8 ago)
- Llamadas HTTPS al servicio REST de GiftCard con autenticación por tienda.
- Mapeo de los más de 80 códigos de error que devuelve Ogloba a mensajes entendibles.
- Almacenamiento seguro de la contraseña de Ogloba en el dispositivo.
- Base de datos local cifrada (SQLCipher) para pagos pendientes, con clave respaldada por el Android Keystore.

### Fase 5 · Integración con HiPOS y empaquetado (9 ago)
- Activity Android que expone los cuatro intents que HiPOS invoca (inicializar, finalizar, obtener versión, transaccionar).
- Manifiesto, firma y script de empaquetado del APK.
- Pruebas automatizadas iniciales (más de 110 casos entre todas las suites).

---

## 3. Pausa (10 – 17 de agosto)

- **Lunes 10 de agosto:** se pausó la construcción para atender otra integración en curso del equipo.
- **11 – 17 de agosto:** proyecto en stand-by.
- **Martes 18 de agosto (hoy):** se reanuda la construcción.

---

## 4. Lo que falta (18 – 24 de agosto)

### Frente 1 · Lectores de tarjetas físicos
- Conexión y comunicación con los lectores físicos en el POS.
- Manejo de los eventos del lector dentro del flujo de venta.
- Pruebas con el hardware certificado de HiPOS.

### Frente 2 · Ogloba · envío de correos de bonos virtuales
- Configuración en la plataforma Ogloba del envío de correos con los bonos virtuales.
- Validación extremo a extremo: al cerrar la venta, el cliente recibe el bono en su correo.

### Frente 3 · Ajustes de UI / UX
- Repaso de pantallas con el sistema de diseño KOAJ (colores, tipografía, espaciados).
- Ajustes de comportamiento, animaciones y micro-interacciones.
- Estados vacíos, mensajes de error y casos borde.

---

## 5. QA y pruebas (24 – 25 de agosto)

- **Inicio:** lunes 24 de agosto.
- **Duración aproximada:** 1 día.
- Pruebas funcionales completas: flujo de venta, recuperación, anulación, bonos virtuales y lectores.
- Verificación de la experiencia en el hardware certificado.
- Empaquetado final del APK UAT y archivo de evidencia de validación.

---

## 6. Puntos a vigilar

- **Hardware KOAJ:** la certificación del hardware para HiPOS aún está pendiente; QA validará en equipos reales.
- **Correos Ogloba:** depende de la configuración del lado de Ogloba; requiere coordinación con Ogloba para no retrasar QA.
- **Lectores físicos:** si el proveedor del lector pide ajustes, podría impactar la fecha de cierre del Frente 1.