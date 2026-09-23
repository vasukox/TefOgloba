namespace Permoda.Pay.Maui.Common;

/// <summary>
/// Qué se le muestra al cajero cuando algo no sale.
/// <para>
/// Los errores llegan como un par código/descripción pensado para depurar: códigos con puntos
/// (<c>ogloba.timeout</c>, <c>cashier_id.required</c>), números crudos de Ogloba (<c>53</c>,
/// <c>230040</c>) y descripciones en inglés que vienen tal cual de su API. Ponerlos en pantalla
/// —"73: Invalid amount"— deja al cajero con un cliente enfrente y nada que hacer.
/// </para>
/// <para>
/// Acá se traduce a una frase que dice DOS cosas: qué pasó y qué hacer. El código técnico no se
/// pierde: viaja aparte en <see cref="OperationMessage.Technical"/> para que soporte pueda
/// rastrear el caso, pero deja de ser el titular.
/// </para>
/// <para>
/// El orden de resolución es a propósito, de más específico a más genérico:
/// </para>
/// <list type="number">
/// <item>Códigos NUESTROS (red, configuración, validaciones del dominio).</item>
/// <item>Códigos NUMÉRICOS de Ogloba — la tabla oficial de docs/OGLOBA_API_REFERENCE.md §5.</item>
/// <item>Palabras clave del mensaje en inglés, por si llega un código que la tabla no cubre.</item>
/// <item>Respaldo honesto: no inventa un diagnóstico y muestra el código para poder reportarlo.</item>
/// </list>
/// </summary>
public static class OperationMessages
{
    /// <summary>
    /// Traduce un fallo a lenguaje de mostrador.
    /// </summary>
    /// <param name="code">Código técnico (nuestro o de Ogloba).</param>
    /// <param name="description">Descripción cruda, normalmente en inglés.</param>
    public static OperationMessage ForFailure(string? code, string? description)
    {
        var normalizedCode = (code ?? string.Empty).Trim();
        var rawDescription = (description ?? string.Empty).Trim();
        var technical = BuildTechnical(code, rawDescription);

        var mapped = ForInternalCode(normalizedCode.ToLowerInvariant())
            ?? ForOglobaCode(normalizedCode)
            ?? ForDescription(rawDescription);

        if (mapped is not null)
        {
            return new OperationMessage(mapped, technical);
        }

        // No se reconoció el error. El código va DENTRO del texto, no solo aparte: esconderlo deja
        // al cajero y a soporte sin nada que reportar, y un mensaje genérico sin código no se
        // puede diagnosticar ni por teléfono ni leyendo el log después.
        return new OperationMessage(
            "La operación no se pudo completar y no se cobró nada. Vuelve a intentarlo; si sigue igual, "
            + $"reporta a soporte este código: {technical ?? "sin código"}",
            technical,
            IsUnknown: true);
    }

    /// <summary>
    /// Códigos que produce el módulo, no Ogloba: red, configuración de la caja y validaciones del
    /// dominio. Son deterministas, así que se resuelven exacto.
    /// </summary>
    private static string? ForInternalCode(string code) => code switch
    {
        // Conexión y disponibilidad. El cajero no puede arreglar el servidor, pero sí puede
        // reintentar o cobrar por otro medio: eso es lo que se le dice.
        "ogloba.timeout" =>
            "Ogloba no respondió a tiempo. Vuelve a intentarlo; si sigue igual, cobra por otro medio.",
        "ogloba.network_error" =>
            "No hay conexión con Ogloba. Revisa la red de la tienda y vuelve a intentarlo.",
        "ogloba.server_error" =>
            "Ogloba está presentando fallas en este momento. Vuelve a intentarlo en unos minutos.",
        "ogloba.http_error" =>
            "Ogloba rechazó la comunicación. Vuelve a intentarlo; si sigue igual, avisa a soporte.",
        "ogloba.malformed_response" =>
            "Ogloba respondió algo que el módulo no pudo leer. No se cobró nada: vuelve a intentarlo.",

        // La operación no está habilitada todavía en esta tienda. No es una falla ni algo que el
        // cajero pueda reintentar: se le dice qué SÍ puede hacer y que avise, en vez de dejarlo
        // reintentando algo que nunca va a funcionar.
        "ogloba.operation_not_published" =>
            "Esta operación todavía no está habilitada en la tienda. No se cobró nada. "
            + "Los bonos físicos y el cobro con bono funcionan normalmente; avisa a soporte.",

        // Credenciales / configuración de la tienda: no es problema del bono ni del cliente.
        "ogloba.authentication_failed" =>
            "Ogloba rechazó las credenciales de la tienda. Avisa al administrador para revisar la configuración.",
        "ogloba.credentials_missing" =>
            "Esta caja no tiene configuradas las credenciales de Ogloba. Entra a Configuración y complétalas.",
        "session.not_configured" =>
            "Esta caja todavía no está configurada. Entra a Configuración y completa los datos de la tienda.",

        "ogloba.card_inactive" =>
            "El bono no está activo, así que no se puede redimir. Verifica el serial o consulta su saldo.",

        // Datos que faltan. Salvo el serial, son de configuración: el cajero no los digita.
        "card_identifier.required" =>
            "Falta el serial del bono. Escanéalo o digítalo para continuar.",
        "card_identifier.too_long" =>
            "El serial es más largo de lo que acepta Ogloba. Verifica que sea el número del bono.",
        "cashier_id.required" =>
            "Esta caja no tiene un cajero registrado. Abre el módulo Ogloba, selecciona tu cajero una vez y vuelve a intentarlo.",
        "cashier_id.too_long" =>
            "El identificador del cajero es demasiado largo. Avisa al administrador para corregirlo.",
        "store_id.required" or "store_id.too_long" =>
            "El código de tienda está mal configurado. Avisa al administrador.",
        "terminal_id.required" or "terminal_id.too_long" =>
            "El código de caja está mal configurado. Avisa al administrador.",

        // Intents de HiPOS. El cajero no los provoca, pero saber que el problema viene del POS le
        // ahorra buscarlo en el bono.
        "hipos.configuration.missing" or "hipos.configuration.incomplete" =>
            "HiPOS no envió la configuración de la tienda. Cierra y vuelve a abrir el cobro desde el POS.",
        "hipos.transaction.incomplete" =>
            "HiPOS envió el cobro incompleto. Cancela y vuelve a iniciarlo desde el POS.",
        "hipos.store.unknown" =>
            "La tienda que envió HiPOS no coincide con la configurada en esta caja. Avisa al administrador.",
        "hipos.transaction.unsupported" =>
            "Esta operación no se puede pagar con bonos Ogloba. Usa otro medio de pago.",
        "hipos.transaction.amount_invalid" =>
            "El importe que envió HiPOS no es válido. Cancela y vuelve a iniciar el cobro desde el POS.",
        "hipos.card.missing" =>
            "Falta el serial del bono. Escanéalo o digítalo para continuar.",
        "hipos.reference.missing" =>
            "Falta la referencia de la operación original. Avisa a soporte con el número de la factura.",

        _ => null
    };

    /// <summary>
    /// Tabla oficial de códigos de Ogloba (docs/OGLOBA_API_REFERENCE.md §5, "Códigos de error").
    /// <para>
    /// Están agrupados por a QUIÉN le toca resolverlo, que es la única clasificación que le sirve
    /// al cajero en el mostrador: lo que resuelve él, lo que resuelve el cliente, lo que necesita
    /// al administrador y lo que solo puede escalarse. Si mañana Ogloba agrega códigos, se
    /// agregan acá y NO al respaldo genérico.
    /// </para>
    /// </summary>
    private static string? ForOglobaCode(string code) => code switch
    {
        // ---- El bono no sirve para esta operación (lo resuelve el cliente con otro bono) ----
        "52" =>
            "Ogloba no reconoce este bono. Verifica que el serial esté completo y bien escaneado.",
        "81" =>
            "El serial no tiene el formato que espera Ogloba. Verifica que sea el número del bono y esté completo.",
        "24" or "25" =>
            "Este bono no pertenece a esta tienda y no se puede usar acá.",
        "56" =>
            "El bono está vencido y ya no se puede usar.",
        "240" =>
            "Este bono ya no es válido.",
        "57" or "112" =>
            "El bono fue dado de baja en Ogloba y ya no se puede usar.",
        "85" =>
            "Este bono ya estaba activado. Consulta su saldo antes de volver a intentarlo.",
        "161" or "232" =>
            "Este bono ya fue usado.",
        "111" =>
            "Este bono ya fue reembolsado.",
        "104" or "236" =>
            "El bono no está activo todavía. Hay que activarlo antes de redimirlo.",
        "105" or "241" =>
            "El bono está bloqueado. No se puede usar.",
        "110" =>
            "El bono está suspendido. No se puede usar.",
        "106" =>
            "El bono no está bloqueado, así que no hay nada que desbloquear.",
        "151" =>
            "Este bono no está disponible para la venta.",
        "152" =>
            "Este bono no se puede activar.",
        "153" =>
            "Este bono no sirve para pagar. Consulta su estado.",
        "154" =>
            "Este bono no admite recargas.",
        "237" =>
            "Este bono no admite reembolso.",
        "231" =>
            "El PIN del bono es incorrecto.",

        // ---- Saldo y montos (lo resuelve el cajero cambiando la cifra o cobrando el resto) ----
        "53" =>
            "El bono no tiene saldo suficiente para este cobro. Cobra la diferencia con otro medio de pago.",
        "156" =>
            "El monto es menor al mínimo que acepta este bono.",
        "157" or "155" =>
            "El monto supera el máximo que acepta este bono.",
        "73" or "158" or "159" =>
            "El monto no es válido para este bono. Verifica el valor con el que se vende.",
        "54" =>
            "Falta el monto de activación. Escribe por cuánto se vende el bono.",
        "160" =>
            "El monto no puede ser negativo.",
        "235" =>
            "El bono quedaría en saldo negativo. Verifica el monto.",
        "234" =>
            "Este bono no admite pago parcial: hay que cubrir el total o usar otro medio de pago.",

        // ---- Configuración de la tienda o de la caja (necesita al administrador) ----
        "23" =>
            "Ogloba no reconoce esta tienda. Revisa el código de tienda en Configuración.",
        "50" or "239" =>
            "Ogloba no reconoce esta caja. Revisa el código de caja en Configuración.",
        "222" =>
            "Esta caja está inactiva en Ogloba. Avisa al administrador para que la habilite.",
        "22" =>
            "Ogloba no reconoce el producto de este bono. Avisa al administrador: el código de producto está mal configurado.",
        "59" or "230040" =>
            "Ogloba no reconoce a este cajero. Abre el módulo Ogloba, selecciona tu cajero y vuelve a intentarlo; si sigue igual, avisa al administrador.",
        "38" or "39" =>
            "La tienda no tiene cupo disponible en Ogloba para emitir bonos. Avisa al administrador.",
        "401" =>
            "Ogloba rechazó las credenciales de la tienda. Avisa al administrador para revisar la configuración.",

        // ---- Anulaciones y devoluciones ----
        "109" =>
            "La transacción ya quedó confirmada y no se puede anular.",
        "230" =>
            "Esta transacción ya estaba anulada.",
        "233" =>
            "No se pudo anular la transacción. Avisa a soporte con el número de la factura.",
        "238" =>
            "Ya pasó el plazo para anular esta transacción.",

        // ---- Órdenes de bono virtual ----
        "23005" =>
            "No se pudo crear la orden del bono virtual. Verifica el correo del cliente y vuelve a intentarlo.",
        "23010" =>
            "Ogloba no reconoce el número de orden.",
        "23013" =>
            "No se pudo emitir el bono virtual. Vuelve a intentarlo; si sigue igual, avisa a soporte.",
        "23014" =>
            "No se pudo devolver la orden. Avisa a soporte.",
        "23016" =>
            "La orden ya fue devuelta.",

        // ---- Cliente ----
        "86" =>
            "Ogloba no encontró los datos del cliente asociado al bono.",
        "322" =>
            "El cliente está bloqueado en Ogloba.",

        // ---- Fraude: no se reintenta, se reporta ----
        "99" or "229" =>
            "Ogloba bloqueó la operación por seguridad. No insistas: reporta el caso al administrador.",

        // ---- Del lado de Ogloba. No se cobró nada; reintentar es lo único que puede hacer ----
        //
        // La tabla oficial define una veintena de códigos distintos con el MISMO significado
        // ("Internal processing error"). Se agrupan a propósito: distinguirlos en pantalla no le
        // cambia nada al cajero, y el número exacto queda igual en el detalle técnico.
        "162" or "206" or "207" or "208" or "209" or "210" or "211" or "212" or "213" or "214"
            or "215" or "216" or "217" or "218" or "219" or "220" or "221" or "226" or "227"
            or "228" or "998" or "999" =>
            "Ogloba tuvo un error interno y no se cobró nada. Vuelve a intentarlo; si sigue igual, cobra por otro medio.",

        "252" =>
            "Ogloba tiene demasiadas operaciones pendientes en este momento. Espera unos segundos y vuelve a intentarlo.",

        _ => null
    };

    /// <summary>
    /// Red de seguridad por texto, para códigos que la tabla oficial no cubra (Ogloba ha devuelto
    /// mensajes sin código en sandbox). Se conserva aunque la tabla esté completa: un código nuevo
    /// sin mapear cae acá antes que al respaldo genérico.
    /// </summary>
    private static string? ForDescription(string description)
    {
        var text = description.ToLowerInvariant();

        return true switch
        {
            _ when Contains(text, "insufficient", "not enough", "balance not enough") =>
                "El bono no tiene saldo suficiente para este cobro. Cobra la diferencia con otro medio de pago.",
            _ when Contains(text, "expired", "expiry", "no longer valid") =>
                "El bono está vencido y ya no se puede usar.",
            _ when Contains(text, "already activated", "already active") =>
                "Este bono ya estaba activado. Consulta su saldo antes de volver a intentarlo.",
            _ when Contains(text, "already used", "already redeemed") =>
                "Este bono ya fue usado.",
            _ when Contains(text, "unknown card", "not found", "does not exist", "invalid card") =>
                "Ogloba no reconoce este bono. Verifica que el serial esté completo y bien escaneado.",
            _ when Contains(text, "blocked", "suspended", "archived", "blacklisted") =>
                "El bono está bloqueado o dado de baja. No se puede usar.",
            _ when Contains(text, "cancelled", "canceled", "refunded") =>
                "El bono ya fue anulado o reembolsado.",
            _ when Contains(text, "invalid amount", "amount missing", "amount too large") =>
                "El monto no es válido para este bono. Verifica el valor con el que se vende.",
            _ when Contains(text, "inactive", "not active") =>
                "El bono no está activo todavía. Hay que activarlo antes de redimirlo.",
            _ when Contains(text, "invalid cashier", "cashier id") =>
                "Ogloba no reconoce a este cajero. Selecciona tu cajero en el módulo y vuelve a intentarlo.",
            _ when Contains(text, "unknown store", "unknown terminal", "unknown reference") =>
                "Ogloba no reconoce esta tienda o esta caja. Revisa la configuración de la terminal.",
            _ when Contains(text, "internal processing", "unexpected error") =>
                "Ogloba tuvo un error interno y no se cobró nada. Vuelve a intentarlo; si sigue igual, cobra por otro medio.",
            _ when Contains(text, "fraud") =>
                "Ogloba bloqueó la operación por seguridad. No insistas: reporta el caso al administrador.",
            _ when Contains(text, "timeout", "timed out") =>
                "Ogloba no respondió a tiempo. Vuelve a intentarlo; si sigue igual, cobra por otro medio.",
            _ when Contains(text, "duplicate") =>
                "Ogloba ya había recibido esta operación. Consulta el saldo del bono antes de repetirla.",
            _ => null
        };
    }

    private static bool Contains(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.Ordinal));

    /// <summary>
    /// El rastro para soporte: código y mensaje original. Nunca es el titular, pero no se pierde
    /// —sin él, un caso raro es irrastreable.
    /// </summary>
    private static string? BuildTechnical(string? code, string description)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(code))
        {
            parts.Add(code.Trim());
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            parts.Add(description);
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}

/// <param name="Text">Lo que lee el cajero: qué pasó y qué hacer.</param>
/// <param name="Technical">Código y mensaje original, para soporte. Se muestra en letra chica.</param>
/// <param name="IsUnknown">
/// No se reconoció el error: <see cref="Text"/> es el respaldo genérico y ya lleva el código
/// adentro. Sirve para no prometer un diagnóstico que no tenemos.
/// </param>
public sealed record OperationMessage(string Text, string? Technical, bool IsUnknown = false);
