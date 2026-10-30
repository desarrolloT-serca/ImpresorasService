namespace ImpresorasService.Domain;

/// <summary>
/// Lo que significa cada código de error de un trabajo (last_error_code), para las personas: un texto
/// corto y qué hacer. El código no cambia (filtros, recuentos, histórico); la API manda los dos y así el
/// ERP, el PHP y la web dicen lo mismo. Incluye los motivos de IPP que la impresora da tal cual
/// (media-empty-error…). Un código que no esté aquí se enseña tal cual.
/// </summary>
public static class ErroresTrabajo
{
    // También lo lee quien ve el trabajo en Error, con los intentos ya agotados (auditoría M4).
    private const string SeReintenta = "Se reintenta solo unas veces; si acaba en error, reintenta.";
    private const string CompruebaAntes = "Puede haber salido: compruébalo antes de reimprimir.";
    private const string RevisaImpresora = "Revisa la impresora; el trabajo sigue cuando se resuelva.";

    private static readonly Dictionary<string, (string Texto, string QueHacer)> Codigos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Del servicio, de la cola de Windows o de la entrada de trabajos.
            ["ROUTE_NOT_FOUND"] = ("Sin impresora asignada", "Revisa las reglas de enrutado de la tienda y el tipo de documento; después, reintenta."),
            ["PDF_DOWNLOAD_FAILED"] = ("No se pudo descargar el documento", "Comprueba que el ERP da el PDF en esa dirección y vuelve a mandarlo desde el ERP."),
            ["PDF_MISSING"] = ("Sin documento que imprimir", "El PDF ya no está (se guarda 90 días) o no llegó: vuelve a mandarlo desde el ERP."),
            ["PDF_INVALID"] = ("Documento dañado", "El PDF está vacío o corrupto: vuelve a generarlo en el ERP."),
            ["PRINTER_INVALID"] = ("Impresora desactivada o borrada", "Actívala o cambia la regla a otra impresora; después, reintenta."),
            ["PRINTER_UNREACHABLE"] = ("Impresora sin conexión", "Comprueba que está encendida y en la red. " + SeReintenta),
            ["SPOOLER_DOWN"] = ("Fallo de la cola de Windows", "Revisa la impresora en el Windows del servidor (nombre de la cola) y SumatraPDF."),
            ["SPOOLER_EXCEPTION"] = ("Error inesperado al enviar", SeReintenta + " Si se repite, avisa a informática."),
            ["NO_PRINT_ROUTE"] = ("No se puede imprimir", "La impresora no tiene IP válida ni cola de Windows: revisa su ficha."),
            ["RETRIES_EXHAUSTED"] = ("Intentos agotados", "Revisa la impresora y reintenta."),
            ["NET_TIMEOUT"] = ("Envío cortado", CompruebaAntes),
            ["PRINTING_INTERRUPTED"] = ("Envío interrumpido", CompruebaAntes),
            ["SPOOL_ACCEPTED_TIMEOUT"] = ("Enviado sin confirmar", "Esta impresora no deja confirmarlo: comprueba que salió."),
            // De la impresora, por IPP.
            ["IPP_NO_RESPONSE"] = ("La impresora no responde por IP", "Comprueba que está encendida y en la red. " + SeReintenta),
            ["IPP_PRINTER_BUSY"] = ("Impresora ocupada", "No acepta trabajos ahora mismo. " + SeReintenta),
            ["IPP_NOT_AUTHORIZED"] = ("La impresora no deja imprimir por IP", "Pide usuario o tiene IPP bloqueado: quítalo en la impresora o dale una cola de Windows."),
            ["IPP_TOO_LARGE"] = ("Documento demasiado grande", "La impresora no admite un documento de ese tamaño por IP: dale una cola de Windows."),
            ["IPP_FORMAT_NOT_SUPPORTED"] = ("La impresora no admite el documento por IP", "No acepta PDF ni imagen por IP: dale una cola de Windows."),
            ["IPP_DOCUMENT_ERROR"] = ("La impresora no pudo leer el documento", "Reintenta: si la impresora admite imagen, esta vez se manda así. Si vuelve a fallar, genera de nuevo el documento en el ERP."),
            ["IPP_ERROR_WHILE_PRINTING"] = ("La impresora falló mientras imprimía", "Puede haber sacado parte: comprueba qué salió y, si falta, reimprime."),
            // Unos rechazos se reintentan y otros (no admite imprimir por IPP) no: sin prometer el reintento.
            ["IPP_REJECTED"] = ("La impresora rechazó el trabajo", "Si acaba en error, revisa la impresora o dale una cola de Windows; después, reintenta."),
            ["IPP_JOB_CANCELED"] = ("Cancelado en la impresora", "Alguien lo canceló en la propia impresora: reintenta si hace falta."),
            ["IPP_JOB_ABORTED"] = ("La impresora lo abortó", "Revisa el motivo y la impresora; después, reintenta."),
            ["IPP_JOB_STOPPED"] = ("Detenido en la impresora", RevisaImpresora),
            ["IPP_COMPLETED_WITH_ERRORS"] = ("Terminado con errores", "La impresora lo dio por terminado con errores: comprueba que salió bien."),
            ["IPP_PRINTER_STOPPED"] = ("Impresora detenida", RevisaImpresora),
            // 10 min: BlockedGiveUpMinutes del watchdog (Worker), que este proyecto no ve.
            ["IPP_PRINTER_STOPPED_TIMEOUT"] = ("Impresora parada más de 10 min", "Puede salir aún cuando se recupere la impresora: compruébalo antes de reimprimir."),
            ["IPP_PRINTER_PROCESSING_TIMEOUT"] = ("Impresora ocupada más de 30 min", "Puede salir aún: compruébalo antes de reimprimir."),
            ["IPP_CANCELED_BY_SERVICE"] = ("Cancelado por tardar demasiado", "Se canceló en la impresora y ya no saldrá: reintenta cuando esté lista. Si sacó alguna hoja antes de pararse, se repetirá."),
        };

    // Motivos de IPP (printer-state-reasons y job-state-reasons, RFC 8011 y PWG 5100), sin el sufijo
    // -error, -warning o -report.
    private static readonly Dictionary<string, string> MotivosIpp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["media-empty"] = "Sin papel en la bandeja",
        ["media-jam"] = "Atasco de papel",
        ["media-low"] = "Papel casi agotado",
        ["media-needed"] = "Se necesita cargar papel",
        ["media-path-cannot-duplex"] = "No se puede imprimir a doble cara",
        ["cover-open"] = "La tapa de la impresora está abierta",
        ["door-open"] = "La puerta de la impresora está abierta",
        ["interlock-open"] = "La cubierta de seguridad está abierta",
        ["toner-empty"] = "El tóner está agotado",
        ["toner-low"] = "El tóner está bajo",
        ["marker-supply-empty"] = "El cartucho de tinta o tóner está vacío",
        ["marker-supply-low"] = "El cartucho de tinta o tóner está bajo",
        ["marker-waste-full"] = "El depósito de residuos de tóner está lleno",
        ["marker-waste-almost-full"] = "El depósito de residuos de tóner está casi lleno",
        ["output-area-full"] = "La bandeja de salida está llena",
        ["output-area-almost-full"] = "La bandeja de salida está casi llena",
        ["input-tray-missing"] = "La bandeja de papel no está colocada",
        ["output-tray-missing"] = "La bandeja de salida no está colocada",
        ["offline"] = "La impresora está sin conexión (offline)",
        ["paused"] = "La impresora está en pausa",
        ["moving-to-paused"] = "La impresora se está pausando",
        ["shutdown"] = "La impresora está apagada",
        ["connecting-to-device"] = "No se puede establecer conexión con la impresora",
        ["timed-out"] = "Tiempo de espera agotado al conectar con la impresora",
        ["spool-area-full"] = "El área de spool está llena, no hay espacio para nuevos trabajos",
        ["fuser-over-temp"] = "El fusor está a temperatura excesiva",
        ["fuser-under-temp"] = "El fusor no alcanza la temperatura de trabajo",
        ["opc-life-over"] = "El cartucho fotoconductor ha llegado al final de su vida útil",
        ["developer-empty"] = "El revelador del cartucho está vacío",
        ["developer-low"] = "El revelador del cartucho está bajo",
        ["other"] = "La impresora ha reportado un error genérico",
        // Del trabajo.
        ["job-canceled-by-user"] = "Cancelado por un usuario",
        ["job-canceled-at-device"] = "Cancelado en la impresora",
        ["job-canceled-by-operator"] = "Cancelado por el administrador de la impresora",
        ["aborted-by-system"] = "Abortado por la impresora",
        ["document-format"] = "La impresora no entiende el formato del documento",
        ["document-unprintable"] = "La impresora no puede imprimir el documento",
        ["document-access"] = "La impresora no pudo leer el documento",
        ["compression"] = "Documento comprimido que la impresora no admite",
        ["unsupported-document-format"] = "Formato de documento no admitido",
        ["job-completed-with-errors"] = "Terminado con errores",
        ["job-completed-with-warnings"] = "Terminado con avisos",
        ["printer-stopped"] = "La impresora está detenida",
    };

    /// <summary>Texto corto del código, o null si no es conocido.</summary>
    public static string? Texto(string? codigo) =>
        codigo is null ? null
        : Codigos.TryGetValue(codigo, out var c) ? c.Texto
        : MotivoIpp(codigo);

    /// <summary>Qué hacer ante el código, o null si no es conocido.</summary>
    public static string? QueHacer(string? codigo) =>
        codigo is null ? null
        : Codigos.TryGetValue(codigo, out var c) ? c.QueHacer
        : MotivoIpp(codigo) is null ? null : RevisaImpresora;

    /// <summary>Motivo de IPP en castellano (con o sin sufijo -error/-warning/-report), o null.</summary>
    public static string? MotivoIpp(string motivo)
    {
        var limpio = motivo.Trim();
        foreach (var sufijo in new[] { "-error", "-warning", "-report" })
            if (limpio.EndsWith(sufijo, StringComparison.OrdinalIgnoreCase))
            {
                limpio = limpio[..^sufijo.Length];
                break;
            }
        return MotivosIpp.TryGetValue(limpio, out var texto) ? texto : null;
    }
}
