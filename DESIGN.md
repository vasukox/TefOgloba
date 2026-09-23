---
name: Permoda.Pay Ogloba (TefOgloba)
description: Módulo de cobro electrónico de bonos Ogloba para HiPOS en tablets KOAJ · Android API 33
colors:
  primary: "#24509E"
  primary-deep: "#163864"
  primary-tint: "#E4ECF9"
  accent: "#1C86D6"
  accent-tint: "#DCEBFA"
  success: "#3F7D4E"
  success-tint: "#E4F0E6"
  error: "#B23A2A"
  error-tint: "#F7E7E4"
  info: "#3B6BAA"
  info-tint: "#E4ECF6"
  background: "#F1F5FB"
  surface: "#FFFFFF"
  surface-alt: "#EAF0F8"
  border: "#DCE3EE"
  divider: "#E1E6EE"
  text-primary: "#152238"
  text-secondary: "#5B6B84"
  text-muted: "#94A3B8"
  on-dark: "#FFFFFF"
  on-dark-muted: "#B7C4D9"
  on-hero: "#FFFFFF"
  on-hero-muted: "#C9DCF2"
  nav-surface: "#0E2A4D"
  nav-surface-muted: "#7C93B8"
typography:
  display:
    fontFamily: "Roboto, system-ui, sans-serif"
    fontSize: "26sp"
    fontWeight: 700
    lineHeight: 1.15
    letterSpacing: "normal"
  title:
    fontFamily: "Roboto, system-ui, sans-serif"
    fontSize: "17sp"
    fontWeight: 700
    lineHeight: 1.25
    letterSpacing: "normal"
  body:
    fontFamily: "Roboto, system-ui, sans-serif"
    fontSize: "14sp"
    fontWeight: 400
    lineHeight: 1.4
    letterSpacing: "normal"
  label:
    fontFamily: "Roboto, system-ui, sans-serif"
    fontSize: "12sp"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "1.3sp"
    textTransform: "uppercase"
  caption:
    fontFamily: "Roboto, system-ui, sans-serif"
    fontSize: "13sp"
    fontWeight: 400
    lineHeight: 1.35
    letterSpacing: "normal"
  input:
    fontFamily: "Roboto, system-ui, sans-serif"
    fontSize: "16sp"
    fontWeight: 400
    lineHeight: 1.3
    letterSpacing: "normal"
rounded:
  sm: "12"
  md: "14"
  lg: "18"
  xl: "22"
  xxl: "26"
  pill: "28"
spacing:
  xs: "4"
  sm: "8"
  md: "12"
  lg: "16"
  xl: "20"
  xxl: "22"
  xxxl: "24"
  huge: "28"
components:
  card:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.lg}"
    padding: "20"
  hero-card:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-hero}"
    rounded: "{rounded.xl}"
    padding: "22"
  field-card:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.xxl}"
    padding: "20,4"
  input-field:
    backgroundColor: "{colors.surface-alt}"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.md}"
    padding: "16,4"
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-dark}"
    typography: "{typography.body}"
    rounded: "{rounded.pill}"
    height: "56"
    padding: "0,20"
  button-primary-pressed:
    backgroundColor: "{colors.primary-deep}"
    textColor: "{colors.on-dark}"
    rounded: "{rounded.pill}"
  button-success:
    backgroundColor: "{colors.success}"
    textColor: "{colors.on-dark}"
    typography: "{typography.body}"
    rounded: "{rounded.pill}"
    height: "56"
  button-accent:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.on-dark}"
    typography: "{typography.body}"
    rounded: "{rounded.pill}"
    height: "56"
  button-secondary:
    backgroundColor: "transparent"
    textColor: "{colors.primary}"
    typography: "{typography.body}"
    rounded: "{rounded.pill}"
    height: "52"
  button-danger:
    backgroundColor: "{colors.error}"
    textColor: "{colors.on-dark}"
    typography: "{typography.body}"
    rounded: "{rounded.pill}"
    height: "56"
  button-borderless:
    backgroundColor: "transparent"
    textColor: "{colors.primary}"
    typography: "{typography.body}"
  status-banner-success:
    backgroundColor: "{colors.success-tint}"
    textColor: "{colors.success}"
    rounded: "{rounded.lg}"
    padding: "16,20"
  status-banner-error:
    backgroundColor: "{colors.error-tint}"
    textColor: "{colors.error}"
    rounded: "{rounded.lg}"
    padding: "16,20"
  status-banner-info:
    backgroundColor: "{colors.info-tint}"
    textColor: "{colors.info}"
    rounded: "{rounded.lg}"
    padding: "16,20"
  nav-shell:
    backgroundColor: "{colors.nav-surface}"
    textColor: "{colors.on-dark}"
    height: "56"
  hero-header:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-hero}"
    rounded: "{rounded.xl}"
    padding: "22"
---

# Design System: Permoda.Pay Ogloba (TefOgloba)

## Overview

**Creative North Star: "El Hilo del Cajero"**

El sistema visual guía al cajero KOAJ por el camino natural de una venta con bono Ogloba. El azul corporativo marca el trayecto (header, CTAs, foco, éxito), el gris-azulado sirve de descanso visual entre acciones, y el azul noche cierra el ciclo con la barra de navegación HiPOS. Cada flujo —Activar, Redimir— se siente como un paso discreto dentro de una secuencia coherente, nunca como una pantalla aislada. La estética es sobria, táctil y cercana: el cajero debe poder operar a un metro de distancia con luz de tienda, sin leer párrafos, y terminar la venta sin dudar de qué botón pulsar.

El sistema existe dentro de un POS HiPOS de ICG sobre tablets Android 13+; por eso todos los componentes respetan las convenciones Material 3 (touch targets ≥ 48dp, ripple en pressed, safe-area insets, edge-to-edge) y se montan sobre la Roboto del sistema. No se usan fuentes externas — la APK debe arrancar rápido en hardware modesto y todo glifo fuera de Roboto es un lastre.

**Key Characteristics:**

- Azul corporativo como única voz cromática de marca; ámbar, verde y rojo entran solo como acento funcional (advertencia, éxito, error) — nunca decorativo.
- Sombras con más cuerpo que antes (Opacity 0.10–0.25) en los CTAs, para una sensación "enterprise" con profundidad real; el resto del sistema mantiene sombras casi imperceptibles.
- Radios grandes (18–28) y generosos (14–16 en inputs): una geometría amable, sin esquinas afiladas.
- Botones píldora (`CornerRadius=28`) con relleno en degradado diagonal (`Primary → PrimaryDeep`) como única forma de CTA primario — refuerza la metáfora del "hilo" continuo con más cuerpo visual.
- Estados discretos pero inequívocos: `Pressed` anima la escala (no salta) y el botón "se hunde" (la sombra se contrae); nunca se usan animaciones agresivas ni rebote.

## Colors

Paleta azul "enterprise" de tres voces: azul Koaj (camino), gris-azulado/hueso frío (descanso), acentos funcionales (advertencia/éxito/error, cada uno en su semántica universal). La profundidad y los roles se construyen por capas tonales, no por sombras.

### Primary

- **Azul Koaj** (`#24509E`): color principal de marca. Se usa en CTAs primarios (`PrimaryButton`), en el header del Scaffold (gradiente con `PrimaryDeep`), en el borde y texto del `SecondaryButton`, en el ícono de éxito del `StatusBanner`. Es el "camino" del cajero — siempre está visible pero nunca satura.
- **Azul Profundo** (`#163864`): versión profunda del primario. Aparece como estado `Pressed` de los CTAs primarios, en la cabecera del Scaffold (parte final del gradiente) y en el hover del header. Refuerza la acción sin cambiar de matiz.
- **Azul Koaj Suave** (`#E4ECF9`): tinte para fondos de hover sutil, fondos de tarjetas en estado neutro, separadores cromáticos suaves. Nunca como fondo de página completo.

### Secondary

- **Celeste Acento** (`#1C86D6`): único acento de marca (antes era ámbar; el ámbar quedó reservado exclusivamente para `Warning`). Reservado para `AccentButton` (operaciones de "destacar", nunca destructivas) y momentos de foco puntual. No se usa como decoración.

### Status

Los colores funcionales NO siguen la paleta azul a propósito: son semántica universal (verde=éxito, rojo=error, ámbar=advertencia) y cambiarlos por consistencia de marca rompería la comprensión inmediata del cajero.

- **Verde Bosque** (`#3F7D4E`): éxito confirmado, voucher aceptado, transacción reconciliada. Acompaña al `SuccessButton` y a la variante success del `StatusBanner`.
- **Rojo Ladrillo** (`#B23A2A`): error, transacción rechazada, voucher inválido. Acompaña al `DangerButton` y a la variante error del `StatusBanner`. Nunca como color decorativo.
- **Ámbar Advertencia** (`#C77700`): advertencia puntual (`Warning`), independiente del acento de marca.
- **Azul Información** (`#3B6BAA`): información neutral, aviso técnico, estado pendiente de recuperación. Acompaña a la variante info del `StatusBanner`.

### Neutral

- **Hueso Frío** (`#F1F5FB`): fondo de página (`Background`). Es la "superficie de mostrador": donde apoyan todas las tarjetas. Frío y corporativo, no cálido.
- **Blanco Superficie** (`#FFFFFF`): fondo de tarjetas y de inputs en estado normal. Sobre el Hueso Frío se ve como una capa elevada.
- **Superficie Alterna** (`#EAF0F8`): fondo de inputs (más oscuro que el fondo de página, para señalar "esto se toca"). Diferencia visual entre input y tarjeta sin usar sombra.
- **Borde Hairline** (`#DCE3EE`): borde 1dp de tarjetas, inputs y separadores. Casi invisible pero delimita sin ruidos.
- **Divisor** (`#E1E6EE`): separadores internos, divisores de lista, espaciadores.
- **Texto Primario** (`#152238`): cuerpo de texto, títulos, importes. Negro azulado, no negro puro.
- **Texto Secundario** (`#5B6B84`): subtítulos, captions, etiquetas de campo en su variant normal.
- **Texto Atenuado** (`#94A3B8`): placeholders, helper text deshabilitado, estados no interactivos.

### Over

- **Blanco Puro** (`#FFFFFF`): texto sobre fondos oscuros primarios (Primary, Success, Error, NavSurface).
- **Blanco Atenuado** (`#B7C4D9`): texto secundario sobre fondos oscuros (etiquetas, breadcrumbs en header oscuro).
- **Blanco Hero Atenuado** (`#C9DCF2`): texto secundario sobre el gradiente del header (eyebrow, subtítulos del hero).
- **Azul Noche** (`#0E2A4D`): superficie de navegación del Shell — cabecera del flyout y barra superior. Texto blanco sobre este fondo; íconos activos en azul claro (`NavSurfaceMuted` `#7C93B8`).
- **Azules apagados sobre Shell** (`#7C93B8`): íconos/texto inactivos del flyout; texto de marca "HI-POS" en footer.

### Named Rules

**The One-Voice Rule.** El Azul Koaj es el único color de marca. Ningún componente puede introducir un azul de matiz distinto, ni un verde o violeta decorativo, ni gradientes que no pasen por `Primary → PrimaryDeep`. Su rareza protege su significado: cuando aparece, el cajero sabe que es acción.

**The Hue of Trust Rule.** Los estados éxito/error/info usan sus tintes (`SuccessTint`, `ErrorTint`, `InfoTint`) como fondo, no sus versiones saturadas. Un `StatusBanner` de éxito nunca rellena la pantalla de verde Bosque — rellena de `SuccessTint` con texto en `Success`. Así el mensaje se lee sin gritar.

**The Hairline Border Rule.** Las tarjetas y los campos usan borde 1dp en `Border` (`#DCE3EE`), nunca una sombra dura. La sombra suave acompaña, no reemplaza, al borde. Quitar el borde y subir la sombra rompe la calma del sistema.

## Typography

**Display Font:** Roboto (Android system default).
**Body Font:** Roboto.
**Label Font:** Roboto Bold uppercase con letter-spacing.

**Character:** Una sola voz tipográfica, técnica y modesta. Sin fuentes externas, sin variantes decorativas. La jerarquía se construye por tamaño y peso, no por elección de familias. El label uppercase con letter-spacing (`CharacterSpacing=1.3`) da el único matiz editorial — sirve para que las etiquetas de campo se lean como rótulos de formulario, no como cuerpo de párrafo.

### Hierarchy

- **Display** (Bold 26sp, line-height 1.15): títulos de página (`PageTitle`) y valores destacados en HeroCards (`HeroValue`). Una sola Display por pantalla; marca el objetivo de la vista.
- **Title** (Bold 17sp, line-height 1.25): títulos de tarjeta (`CardTitle`). Conecta el Display con el contenido debajo. Hasta 3-4 por pantalla en flujos densos.
- **Body** (Regular 14sp, line-height 1.4): texto por defecto en cualquier `Label` o párrafo. Cuerpo principal de la información.
- **Label** (Bold 12sp uppercase, letter-spacing 1.3sp): etiquetas de campo (`FieldLabel`), eyebrows del hero (`HeroLabel`). Van en gris secundario sobre fondo claro, o atenuados sobre el hero.
- **Caption** (Regular 13sp, line-height 1.35): texto auxiliar, notas al pie, hints, referencias (`Caption`, `HeroCaption`). Más pequeño que body, no compite con él.
- **Input** (Regular 16sp): texto dentro de `Entry`, `Editor` y `Picker`. Ligeramente más grande que el body para legibilidad táctil; nunca menor a 14sp.

### Named Rules

**The Single-Family Rule.** Solo Roboto. Cargar cualquier otra fuente (Inter, Manrope, custom) rompe el principio de APK ligera en hardware modesto KOAJ. Si una pantalla pide más personalidad visual, lo resuelve con color y radio, no con tipografía.

**The Uppercase Label Rule.** Toda etiqueta de campo (`FieldLabel`, `HeroLabel`) va en mayúsculas con `CharacterSpacing=1.3`. Mezclar mayúsculas y minúsculas en rótulos de campo crea ambigüedad sobre qué es editable y qué es informativo.

## Layout

El sistema sigue el patrón **Scaffold de tres bloques** encabezado azul / contenido scroll centrado / footer HiPOS. Es la única plantilla estructural autorizada.

- **Encabezado:** gradiente `Primary → PrimaryDeep`, esquinas **inferiores** redondeadas (`rounded.xl`, 22dp), padding 22. Back button circular 46×46 transparente a la izquierda; eyebrow en `OnHeroMuted` (12sp Bold uppercase), título en `OnHero` (26sp Bold), subtítulo opcional en `OnHeroMuted` (13sp). El trailing slot de la derecha reserva espacio para el logo de marca del comercio (Permoda/KOAJ), nunca logos de terceros.
- **Contenido:** `ScrollView` con padding `(20, 24, 20, 28)`, `MaximumWidthRequest` entre 760–820 (las tablets HiPOS son ~1280dp; este cap mantiene densidad de lectura cómoda). Contenido centrado (`HorizontalOptions="Center"`). Las tarjetas (Card, HeroCard, FieldCard) llevan padding interno consistente de 16–22.
- **Footer:** barra `NavSurface` (#0E2A4D) con altura 56dp, texto de marca centrado en `OnDark` con `CharacterSpacing=2.5`. La marca por defecto es "HI-POS" con su tagline; nunca se sustituye por una marca de PSP.

Espaciado vertical entre bloques: múltiplos de 4 (`xs=4`, `sm=8`, `md=12`, `lg=16`, `xl=20`, `xxl=22`, `xxxl=24`, `huge=28`). El ritmo es: separación entre tarjetas `lg` (16), entre tarjetas y botones `xl` (20), antes de un CTA final `xxl` (22).

Densidad media-baja: los touch targets críticos (botones primarios, campos de entrada) ocupan ≥ 48dp de alto; los CTAs primarios 56dp; las filas de lista 56–64dp. Esto es innegociable para uso con pulgar en mostrador.

## Elevation & Depth

**Plano por defecto, elevado al pulsar.** Las superficies reposan planas; la única elevación visible está reservada a tres contextos: el header en gradiente (que ya implica jerarquía por color), el `HeroCard` del resumen de transacción (la "joya" de la pantalla), y los CTAs primarios cuando el cajero los pulsa.

No hay sombras estructurales: ningún componente "flota" sobre la página en estado de reposo. La profundidad se construye por capas tonales — `Surface` (#FFFFFF) sobre `Background` (#F1F5FB), `SurfaceAlt` (#EAF0F8) dentro de inputs — y por el borde hairline de 1dp. Los CTAs primarios son la excepción: llevan más cuerpo de sombra (ver abajo) para el tacto "enterprise".

### Shadow Vocabulary

- **Card Soft** (`Shadow Brush=ShadowColor, Offset=0,2, Radius=12, Opacity=0.06`): sombra opcional de `Card`. Acompaña al borde hairline; no lo reemplaza.
- **Hero Soft** (`Shadow Brush=ShadowColor, Offset=0,6, Radius=18, Opacity=0.18`): sombra del `HeroCard`. Es la única sombra estructural fuera de los botones; el HeroCard "flota" sobre el fondo de página porque es el dato que el cajero mirará al final del flujo.
- **Button Elevated** (`Shadow Brush=ShadowColor, Offset=0,4, Radius=14, Opacity=0.22`): sombra en reposo del `PrimaryButton` y sus variantes en degradado. Sirve para que el CTA se sienta "tocable" antes de pulsarlo.
- **Button Sunken** (`Shadow Brush=ShadowColor, Offset=0,1, Radius=6, Opacity=0.10`): sombra del CTA en estado `Pressed` — se contrae para que el botón se sienta "hundido" físicamente, reforzando el feedback táctil.

### Named Rules

**The Flat-At-Rest Rule.** Una superficie en reposo nunca proyecta sombra dura. Si una pantalla pide jerarquía adicional entre dos tarjetas, se resuelve con borde + tono, no con más sombra. Los CTAs primarios son la única excepción declarada.

**The Pressed-Does-The-Talking Rule.** El estado `Pressed` de un CTA cambia el fondo (`Background` del degradado → color deep sólido) y contrae la sombra (`Button Elevated` → `Button Sunken`); el `Scale` (0.96→1.0) lo anima `PremiumButtonPressAnimation` sobre los eventos `Pressed`/`Released` del control — ya no es un salto instantáneo del `VisualStateManager`, es una transición real (90ms bajada / 140ms subida, sin rebote). No se añaden ripples de color ni overlays; la opacidad baja (`Opacity=0.5`) en `Disabled` cierra el ciclo.

## Shapes

Forma amable, generosa, sin esquinas afiladas. La lengua del sistema son los radios grandes y las superficies suaves.

- **Píldora (`28`)** para todos los CTAs primarios (`PrimaryButton`, `SuccessButton`, `AccentButton`, `DangerButton`) y para el `SecondaryButton`. Ningún botón primario puede tener un radio menor a 28.
- **Tarjeta media (`18`)** para `Card` — tarjetas de contenido general, listas, secciones.
- **Tarjeta hero (`22`)** para `HeroCard` — resumen de transacción, balance actual, tarjeta destacada.
- **Field card (`26`)** para `FieldCard` — contenedor que envuelve un campo de formulario con etiqueta, valor y borde inferior.
- **Input (`14`)** para `InputField` — campo de entrada directo (Entry, Picker). Radio más cerrado porque el campo es pequeño y se apoya en el `FieldCard` cuando se necesita contenedor.
- **Default Button (`12`)** solo para botones inline o de toolbar; no se usa en CTAs primarios.

Bordes siempre 1dp en `Border` color (`#DCE3EE`); stroke `Transparent` solo en `HeroCard` (donde el gradiente hace de borde implícito).

## Components

### Buttons

- **Forma:** píldora (`28`), altura 56dp en primarios / 52dp en `SecondaryButton`. Texto 16sp Bold sobre `OnDark` (primarios) o `Primary` (secondary).
- **Primario:** relleno en degradado diagonal `Primary → PrimaryDeep` (`PrimaryButtonBrush`), texto `OnDark`, sombra elevada (`Button Elevated`). Estado `Pressed` → fondo sólido `PrimaryDeep` + sombra contraída (`Button Sunken`). Estado `Disabled` → `Opacity=0.5`.
- **Variantes (BasedOn):** `SuccessButton` (degradado verde Bosque), `AccentButton` (degradado celeste acento), `DangerButton` (degradado rojo Ladrillo). Mismas reglas de estado, cada una con su propio par base→deep.
- **Secundario:** fondo `Transparent`, borde 1.5dp en `Primary`, texto `Primary`. Estado `Pressed` → fondo `PrimaryTint`. Se usa para acciones complementarias ("Cancelar", "Volver") sin competir con el CTA primario.
- **Borderless:** fondo `Transparent`, texto `Primary` Bold. Estado `Pressed` → `Opacity=0.6`. Para acciones inline tipo "¿Olvidó su PIN?".
- **Feedback de presión:** el `Scale` (0.96 al bajar → 1.0 al soltar) lo anima `PremiumButtonPressAnimation` — un handler registrado una sola vez en `MauiProgram` sobre los eventos `Pressed`/`Released` de TODO `Button` de la app, no un behavior por página ni un salto del `VisualStateManager`.

### Cards / Containers

- **Card (radio 18):** fondo `Surface`, borde 1dp `Border`, padding 20, sombra `Card Soft`. La unidad de contenido por defecto.
- **HeroCard (radio 22):** gradiente `Primary → PrimaryDeep`, stroke `Transparent`, padding 22, sombra `Hero Soft`. Una por pantalla como máximo. Alberga el dato culminante: el saldo, el `referenceNumber`, el resultado de la transacción.
- **FieldCard (radio 26):** fondo `Surface`, borde 1dp `Border`, padding `(20, 4)`. Envuelve un `FieldLabel` + valor + icono derecho. Usado para mostrar datos no editables con etiqueta.
- **InputField (radio 14):** fondo `SurfaceAlt`, borde 1dp `Border`, padding `(16, 4)`. El campo de entrada en sí. Se apoya sobre un `FieldCard` para contexto.

### Inputs / Fields

- **Entry:** texto 16sp `TextPrimary`, placeholder `TextMuted`, altura mínima 48dp. Sin borde propio (lo da el `InputField` contenedor). `BackgroundColor=Transparent`.
- **Picker:** igual que `Entry`; `TitleColor=TextSecondary`. Altura mínima 48dp.
- **Editor:** texto 13sp `TextPrimary`, placeholder `TextMuted`, altura mínima 44dp (más bajo porque se usa para notas).
- **Disabled:** el componente gana `Opacity=0.5` vía `VisualStateManager`.

### Status Banner

> **Estado actual:** el resultado de una operación se comunica con un **toast nativo de Android** (`IToastService` / `AndroidToastService`, registrado en `MauiProgram`), que reemplazó al banner de pantalla completa. `StatusBannerView` y sus estilos siguen definidos y vigentes para estados persistentes en pantalla (avisos que deben quedarse a la vista); el toast es para el desenlace de una acción. Las reglas de color y forma de abajo aplican a ambos.

- Tres variantes (`Success`, `Error`, `Info`); comparten forma (radio 18, padding `(16, 20)`) y estructura (ícono a la izquierda, texto multilínea a la derecha).
- Fondo en tinte (`SuccessTint`, `ErrorTint`, `InfoTint`), texto y ícono en color saturado (`Success`, `Error`, `Info`).
- Una sola StatusBanner visible por pantalla. Se ubica justo debajo del header, antes del primer bloque de contenido.
- Se anima con `EntranceBehavior` (fade + slide Y de 10→0, 300ms `Easing.CubicOut`) cuando cambia de estado; se desvanece (`FadeTo 0`, 200ms) al cerrar.

### Navigation

- **Shell + Flyout (Android):** barra superior `NavSurface` (#0E2A4D), texto en `OnDark`. La navegación operativa vive en un `Flyout` (`FlyoutBehavior="Flyout"`, ancho 300) con cabecera en `PrimaryDark` y un ícono SVG por ruta; los ítems inactivos van en `NavSurfaceMuted` (#7C93B8).
- **Vías:** cinco entradas visibles en el menú (Inicio, Consultar saldo, Bitácora, Usuarios, Configuración). *Activar bono*, *Redimir saldo* y *Registrar cajeros* existen como rutas pero **no se ofrecen en el menú**: mueven dinero de una factura y se entra a ellas por flujo (las levanta HiPOS, o el selector de tipo de bono). *Tipo de bono* e *Ingreso de cajero* son rutas globales que se empujan sobre la pestaña activa, nunca ítems raíz del Shell. No hay flujo de devolución ni de cancelación de bonos — KOAJ no los procesa. Todas definidas como constantes en `Common/AppRoutes.cs`; nunca literales en XAML.
- **Chrome alternativo:** el modo de pantalla completa (modo quiosco) oculta `Shell.NavBarIsVisible=True` cuando el flujo es guiado (p. ej. confirmación de pago). El Scaffold pasa a ser el único chrome.

### Scaffold (signature component)

- **Estructura:** tres bloques Vertical (header / content / footer). Header `HeroCard` invertido, content `ScrollView`, footer `NavSurface`.
- **Header:** gradiente `HeroBrush` (Primary → PrimaryDeep), esquinas inferiores `rounded.xl`, padding 22. Slots fijos: back button (46×46 transparente), eyebrow + title + subtitle, trailing.
- **Footer:** barra 56dp `NavSurface`, texto centrado `OnDark` con `CharacterSpacing=2.5` mostrando la marca "HI-POS".
- **Inserción obligatoria:** toda `ContentPage` de la app va envuelta en `ScaffoldView` salvo la pantalla de login inicial y la de confirmación de cierre (que usan chrome propio).

## Do's and Don'ts

Concreto, no editorial. Cada regla sale del sistema ya implementado.

### Do:

- **Do** usa siempre `"{StaticResource NombreDelToken}"` para cualquier color, no hex inline.
- **Do** respeta el ritmo de espaciado en múltiplos de 4 (4, 8, 12, 16, 20, 22, 24, 28).
- **Do** centra el contenido del `ScrollView` con `MaximumWidthRequest` entre 760–820.
- **Do** usa el `VisualStateManager` nativo de MAUI para estados `Pressed`/`Disabled` en botones y entradas; no añadas behaviors de presión.
- **Do** aplica `EntranceBehavior` escalonado (DelayMs 60, 120, 180…) a bloques de contenido cuando una pantalla aparece por primera vez o cambia de estado.
- **Do** enmascara el PAN del bono siempre como `113817******5937` en cualquier componente visible.
- **Do** muestra mensajes de error del Ogloba en español, cortos y accionables ("Saldo insuficiente" en lugar del `errorCode: 53` crudo).

### Don't:

- **Don't** introduzcas colores fuera de la paleta del sistema. Si necesitas un tono nuevo, propón antes un token, no un hex inline.
- **Don't** uses `NavigationPage`, `TabBar` ni flyouts adicionales al del `AppShell`. El chrome es responsabilidad del Shell y del Scaffold.
- **Don't** imites tarjetas con `Border` + `CornerRadius` arbitrario; usa los estilos `Card` o `HeroCard`.
- **Don't** uses radios fuera de la escala `{12, 14, 18, 22, 26, 28}`.
- **Don't** uses sombras con `Opacity > 0.25` en el contenido; la jerarquía la llevan el color y el radio.
- **Don't** animes `Width`/`Height` (lento en MAUI); anima `Opacity`, `TranslationX/Y`, `Scale`, `Rotation`.
- **Don't** muestres logos ni nombres de Ogloba, ICG, HiPOS, Sistecrédito en UI o comprobantes — la regla HiOSTORE de "no third-party brands" se respeta literalmente.
- **Don't** registres una ruta Shell como literal en XAML; usa siempre la constante en `Common/AppRoutes.cs`.
