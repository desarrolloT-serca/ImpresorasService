import { z } from "zod"

import { entero, fechaFiltro, lista } from "./comun.js"

// Contratos del módulo de impresión: lo que devuelve la API .NET, tal cual
// (camelCase de ASP.NET). Al integrar el módulo en el ERP pasan a
// packages/contratos y su pasarela los valida en los dos extremos.

// Claves de permiso, como PERMISOS_<MÓDULO> en los contratos del ERP. El
// módulo solo mira estas claves; quién las tiene lo decide el contenedor
// (aquí, por rol; en el ERP, su catálogo de permisos).
export const PERMISOS_IMPRESION = {
  leer: "impresion.leer",
  reimprimir: "impresion.reimprimir",
  administrar: "impresion.administrar",
  // Ver todas las tiendas va aparte del rol: el call center será empleado y
  // las verá todas (docs/PLAN.md, B7).
  todasLasTiendas: "impresion.tiendas.todas",
  // Con un perfil de tienda, que es de todos los operadores de esa tienda,
  // reimprimir pide el código y la contraseña de quien lo hace: si no, no se
  // sabría qué persona fue (decisión del usuario, 27/10/2026).
  identificarse: "impresion.reimprimir.identificarse",
} as const

// PrintJobStatus de la API.
export const ESTADOS_TRABAJO = {
  pendiente: 0,
  enrutado: 1,
  imprimiendo: 2,
  aceptado: 3,
  impreso: 4,
  sinConfirmar: 5,
  reintentoProgramado: 6,
  cancelado: 7,
  error: 8,
  bloqueado: 9,
} as const
export const EstadoTrabajo = z.number().int().min(0).max(9)

export const TrabajoCola = z.object({
  jobId: z.string(),
  externalJobId: z.string(),
  storeId: z.number().int(),
  printerId: z.number().int().nullable(),
  printerName: z.string().nullable(),
  documentType: z.string(),
  status: EstadoTrabajo,
  attemptCount: z.number().int(),
  lastErrorCode: z.string().nullable(),
  lastErrorMessage: z.string().nullable(),
  // El código, para la gente, y qué hacer (ErroresTrabajo en la API). Opcionales:
  // las API anteriores al 26/10/2026 no los mandan.
  lastErrorText: z.string().nullable().optional(),
  lastErrorHint: z.string().nullable().optional(),
  createdAtUtc: z.string(),
  updatedAtUtc: z.string(),
  // Reintentar o reimprimir reutiliza el trabajo: createdAtUtc es cuándo llegó
  // y sentAtUtc, cuándo salió después del último reintento (null si aún no).
  // retries: reintentos tras un error (no había salido); reprints:
  // reimpresiones de algo sin confirmar (puede ser un duplicado). Opcionales:
  // las API anteriores al 26/10/2026 no los mandan.
  sentAtUtc: z.string().nullish(),
  // Última vez que se volvió a mandar (reintento o reimpresión); la cola ordena
  // por ella o, sin ella, por createdAtUtc. Opcional: API anterior al 27/10/2026.
  lastRequeuedAtUtc: z.string().nullish(),
  retries: z.number().int().optional(),
  reprints: z.number().int().optional(),
})
export type TrabajoCola = z.infer<typeof TrabajoCola>

// POST /api/printjobs/{id}/reprint: reimprimir lo que ya salió (impreso o sin
// confirmar). Motivo obligatorio, las claves de RoutingService.ReprintReasons;
// con «Otro», la nota también.
export const MOTIVOS_REIMPRESION = {
  MAL_IMPRESO: "Mal impreso",
  PERDIDO: "Perdido",
  DANADO: "Dañado o mojado",
  ATASCO: "Atasco o sin papel",
  OTRO: "Otro",
} as const
export type MotivoReimpresion = keyof typeof MOTIVOS_REIMPRESION
export const MAX_NOTA_REIMPRESION = 500
// Con perfil de tienda (PERMISOS_IMPRESION.identificarse), quién reimprime: la
// pasarela del ERP lo comprueba y lo quita antes de pasarlo a la API .NET.
export type PeticionReimpresion = {
  reason: MotivoReimpresion
  note?: string
  operador?: string
  contrasena?: string
}

// GET /api/printjobs/reprints: el registro, lo más reciente primero. Cada uno
// ve su tienda; quien ve todas, todas (o la del filtro).
export const Reimpresion = z.object({
  eventId: z.number().int(),
  jobId: z.string(),
  externalJobId: z.string(),
  documentType: z.string(),
  storeId: z.number().int(),
  occurredAtUtc: z.string(),
  actor: z.string().nullable(),
  previousStatus: EstadoTrabajo.nullable(),
  reason: z.string().nullable(),
  reasonText: z.string().nullable(),
  note: z.string().nullable(),
  printerName: z.string().nullable(),
})
export type Reimpresion = z.infer<typeof Reimpresion>
// Con page: una página (25, 50 o 100) y cuántas hay de cada motivo en el
// periodo, antes de filtrar por motivo ("" = sin motivo). WEB-2, 27/10/2026.
export const PaginaReimpresiones = z.object({
  value: z.array(Reimpresion),
  count: z.number().int(),
  page: z.number().int(),
  pageSize: z.number().int(),
  byReason: z.record(z.string(), z.number().int()),
})
export type PaginaReimpresiones = z.infer<typeof PaginaReimpresiones>

// POST /api/printjobs/{id}/route, /reprint y /cancel. Reintentar sin regla
// aplicable devuelve 200 con status ErrorFinal y su código.
export const ResultadoAccion = z.object({
  status: z.string(),
  errorCode: z.string().nullish(),
  // Al cancelar: qué pasó en la cola de la impresora (CancelacionEnImpresora
  // o «SinNumero») y el texto para quien lo pidió. Sin ellos, no había
  // llegado a la impresora.
  printerCancel: z.string().nullish(),
  printerMessage: z.string().nullish(),
})

// GET /api/printjobs?includeTotal=true
export const PaginaCola = z.object({
  value: z.array(TrabajoCola),
  count: z.number().int(),
  page: z.number().int(),
  pageSize: z.number().int(),
})
export type PaginaCola = z.infer<typeof PaginaCola>

// Atajos de la cola, con los mismos conjuntos que los KPI del dashboard.
export const VistaCola = z.enum(["sinReenviar", "sinImpresora"])
export type VistaCola = z.infer<typeof VistaCola>

export type ConsultaCola = {
  pagina: number
  tamano: number
  q?: string
  tienda?: number
  estado?: number
  vista?: VistaCola
  // Búsqueda avanzada (27/10/2026): días «AAAA-MM-DD» de la zona de negocio,
  // incluidos los dos, sobre la hora por la que se ordena; impresoras y tipos,
  // varios a la vez. Vacío = sin filtrar.
  desde?: string
  hasta?: string
  impresoras?: number[]
  tipos?: string[]
}

// Los de la búsqueda avanzada (botón de arriba a la derecha): se usan poco y
// no van en la barra (decisión del usuario, 27/10/2026).
export const CriteriosAvanzadosCola = z
  .object({
    impresoras: lista(entero(1, 2_147_483_647), 200),
    tipos: lista(z.string().min(1).max(40), 50),
    desde: fechaFiltro.optional(),
    hasta: fechaFiltro.optional(),
  })
  .refine((c) => !c.desde || !c.hasta || c.desde <= c.hasta, {
    message: "no puede ser anterior a la de «desde»",
    path: ["hasta"],
  })
export type CriteriosAvanzadosCola = z.output<typeof CriteriosAvanzadosCola>

export const Tienda = z.object({
  storeId: z.number().int(),
  name: z.string(),
  isActive: z.boolean(),
})
export type Tienda = z.infer<typeof Tienda>

// GET /api/routingrules: la entidad, con su impresora. Sin tienda es global.
// Gana la más específica (tienda + tipo + canal… hasta global) y, entre
// iguales, la de menor prioridad (RoutingResolver).
export const ReglaEnrutado = z.object({
  ruleId: z.number().int(),
  priority: z.number().int(),
  storeId: z.number().int().nullable(),
  documentType: z.string().nullable(),
  channel: z.string().nullable(),
  printerId: z.number().int(),
  isActive: z.boolean(),
  validFromUtc: z.string(),
  validToUtc: z.string().nullable(),
  printer: z.object({ printerName: z.string() }).nullable(),
})
export type ReglaEnrutado = z.infer<typeof ReglaEnrutado>

// CreateRoutingRuleRequest y UpdateRoutingRuleRequest (límites del DDL).
// Tipo y canal vacíos = cualquiera.
export const DatosRegla = z.object({
  priority: z
    .number({ error: "Obligatoria" })
    .int()
    .min(0, "No puede ser negativa"),
  storeId: z.number().int().nullable(),
  documentType: z
    .string()
    .trim()
    .max(80, "Máximo 80 caracteres")
    .transform((v) => v || null),
  channel: z
    .string()
    .trim()
    .max(40, "Máximo 40 caracteres")
    .transform((v) => v || null),
  printerId: z.number({ error: "Elige una impresora" }).int(),
  isActive: z.boolean(),
})
export type DatosRegla = z.output<typeof DatosRegla>

// POST /api/routing/resolve (solo administración): la regla que ganaría para un
// trabajo, con la lógica real del RoutingResolver (simulador de la pantalla de
// reglas). 404 = ninguna: el trabajo quedaría en Error (ROUTE_NOT_FOUND).
export const ResolucionRegla = z.object({
  printerId: z.number().int(),
  ruleId: z.number().int(),
})
export type ResolucionRegla = z.infer<typeof ResolucionRegla>

// Umbrales de salud (GET/PUT /api/dashboard/threshold-rules): de 1 a 3 por
// métrica, crecientes con la severidad. Los usan el dashboard y las alertas
// de Telegram; la API los valida (ThresholdRuleEngine).
export const SeveridadUmbral = z.enum(["info", "warning", "critical"])
export type SeveridadUmbral = z.infer<typeof SeveridadUmbral>
export const Umbral = z.object({
  min: z.number().int(),
  severity: SeveridadUmbral,
})
export type Umbral = z.infer<typeof Umbral>
// Horario de tienda común (hora de negocio) en el que cuenta «sin recibir»:
// días ISO (1 = lunes … 7 = domingo) y "HH:mm".
export const HorarioTienda = z.object({
  dias: z.array(z.number().int().min(1).max(7)),
  desde: z.string(),
  hasta: z.string(),
})
export type HorarioTienda = z.infer<typeof HorarioTienda>
export const ReglasUmbral = z.object({
  queue: z.array(Umbral),
  failed: z.array(Umbral),
  missingHost: z.array(Umbral),
  conn: z.array(Umbral),
  // Opcionales (27/10/2026): vacías = desactivadas. Con una API anterior no
  // vienen y quedan así.
  silent: z.array(Umbral).default([]),
  reprints: z.array(Umbral).default([]),
  horario: HorarioTienda.default({
    dias: [1, 2, 3, 4, 5, 6],
    desde: "09:00",
    hasta: "21:00",
  }),
})
export type ReglasUmbral = z.infer<typeof ReglasUmbral>

// Telegram. enabled va en la configuración del servidor (Telegram:Enabled).
// El token se pone en PUT /telegram/token y nunca vuelve: solo de dónde sale
// (pantalla o Telegram:BotToken del servidor) y sus 4 últimos caracteres.
export const ConfigTelegram = z.object({
  enabled: z.boolean(),
  minSeverity: z.enum(["warning", "critical"]),
  notifyOnRecovery: z.boolean(),
  checkIntervalMinutes: z.number().int(),
  // Solo avisar dentro del horario de tienda (el de Umbrales, que viene para
  // enseñarlo). Opcionales: una API anterior no los manda (27/10/2026).
  soloEnHorario: z.boolean().optional(),
  horario: HorarioTienda.optional(),
  tokenOrigen: z.enum(["pantalla", "servidor"]).nullable().optional(),
  tokenFinal: z.string().nullable().optional(),
})
export type ConfigTelegram = z.infer<typeof ConfigTelegram>

export const ChatTelegram = z.object({
  chatId: z.number().int(),
  description: z.string().nullable(),
  isActive: z.boolean(),
  storeId: z.number().int().nullable(),
  createdAtUtc: z.string(),
})
export type ChatTelegram = z.infer<typeof ChatTelegram>

// AddChatRequest. Los grupos de Telegram tienen id negativo.
export const DatosChat = z.object({
  chatId: z
    .string()
    .trim()
    // Con letras suele ser el código de un enlace de invitación (t.me/+…),
    // que no sirve: Telegram pide el número del chat.
    .refine(
      (v) => !/[a-z]/i.test(v),
      "Eso parece un enlace de invitación, no el número del chat: añade el bot al grupo y saca su número (empieza por -100)"
    )
    .regex(
      /^-?[1-9]\d{0,15}$/,
      "Número de chat de Telegram (los grupos son negativos)"
    )
    .transform(Number),
  description: z
    .string()
    .trim()
    .max(120, "Máximo 120 caracteres")
    .transform((v) => v || null),
  storeId: z.number().int().nullable(),
})

// PUT /api/telegram/chats/{chatId}: descripción, tienda y si está activo
// (pausarlo sin borrarlo). El número del chat no cambia.
export const EdicionChat = z.object({
  description: DatosChat.shape.description,
  storeId: z.number().int().nullable(),
  isActive: z.boolean(),
})
export type EdicionChat = z.output<typeof EdicionChat>

// GET /api/telegram/preview: el aviso tal como lo manda el Worker (HTML de
// Telegram: b, i y code), con una tienda de ejemplo.
export const VistaPreviaTelegram = z.object({
  alerta: z.string(),
  recuperada: z.string(),
})

// GET /api/telegram/history: avisos mandados (o que no llegaron), lo más
// reciente primero.
export const AvisoTelegram = z.object({
  occurredAtUtc: z.string(),
  storeId: z.number().int().nullable(),
  storeName: z.string().nullable(),
  health: z.string(),
  reason: z.string().nullable(),
  delivered: z.boolean(),
})
export type AvisoTelegram = z.infer<typeof AvisoTelegram>

export const PruebaTelegram = z.object({
  results: z.array(z.object({ chatId: z.number(), ok: z.boolean() })),
})

// Dashboard (GET /api/dashboard/overview y /timeseries; contrato en
// docs/contrato-kpi-dashboard.md). La salud y las alertas las calcula la API
// (StoreHealthEvaluator), la misma fuente que las alertas de Telegram.
export const VentanaDashboard = z.enum(["today", "7d", "30d"])
export type VentanaDashboard = z.infer<typeof VentanaDashboard>

export const Salud = z.enum(["healthy", "info", "warning", "critical"])
export type Salud = z.infer<typeof Salud>

export const TiendaDashboard = z.object({
  storeId: z.number().int(),
  storeName: z.string(),
  connectedPrinters: z.number().int(),
  received: z.number().int(),
  printed: z.number().int(),
  failed: z.number().int(),
  queuedCurrent: z.number().int(),
  failedWithoutRetryCurrent: z.number().int(),
  health: Salud,
  healthReason: z.string(),
  unassignedQueueCurrent: z.number().int(),
  oldestQueuedAtUtc: z.string().nullable(),
  oldestFailedWithoutRetryAtUtc: z.string().nullable(),
  // Umbrales opcionales (27/10/2026; no vienen con una API anterior): minutos
  // sin recibir en horario de tienda (null fuera de él) y reimpresiones de hoy.
  silentMinutes: z.number().int().nullable().optional(),
  reprintsToday: z.number().int().optional(),
  // Impresoras activas de la tienda; sus datos (nombre, conexión) van en /printers.
  printers: z.array(
    z.object({
      printerId: z.number().int(),
      queueCurrent: z.number().int(),
      failedWindow: z.number().int(),
      receivedWindow: z.number().int(),
    })
  ),
})
export type TiendaDashboard = z.infer<typeof TiendaDashboard>

export const AlertaTienda = z.object({
  storeId: z.number().int(),
  storeName: z.string(),
  health: Salud,
  healthReason: z.string(),
  queuedCurrent: z.number().int(),
  failedWithoutRetryCurrent: z.number().int(),
})
export type AlertaTienda = z.infer<typeof AlertaTienda>

export const ResumenDashboard = z.object({
  generatedAtUtc: z.string(),
  // received, printed y failed son del periodo; el resto, de ahora mismo.
  kpis: z.object({
    received: z.number().int(),
    printed: z.number().int(),
    failed: z.number().int(),
    // Reimpresiones de lo que ya había salido, del periodo. Opcional: las API
    // anteriores al 26/10/2026 no lo mandan.
    reprints: z.number().int().optional(),
    queueCurrent: z.number().int(),
    failedWithoutRetryCurrent: z.number().int(),
    activePrinters: z.number().int(),
    activeStores: z.number().int(),
  }),
  alerts: z.array(AlertaTienda),
  stores: z.array(TiendaDashboard),
})
export type ResumenDashboard = z.infer<typeof ResumenDashboard>

// Tramos de la franja pedida (bucketMinutes; 0 = automática) o, sin pedirla,
// por hora hoy y por día en 7 y 30 días. queueDepth es la cola al final de cada
// tramo, reconstruida desde la de ahora; queuePeak, la mayor que hubo dentro
// (un trabajo espera segundos: al cerrar el tramo la cola casi siempre es 0).
export const SerieDashboard = z.object({
  bucket: z.enum(["hour", "day"]),
  // Opcional: las API anteriores al 26/10/2026 no lo mandan (solo bucket).
  bucketMinutes: z.number().int().optional(),
  points: z.array(
    z.object({
      startUtc: z.string(),
      received: z.number().int(),
      printed: z.number().int(),
      failed: z.number().int(),
      queueDepth: z.number().int(),
      // Opcional: las API anteriores al 27/10/2026 no lo mandan.
      queuePeak: z.number().int().optional(),
      // Lo que llegó en el tramo por su estado de ahora: cada trabajo una vez; un
      // fallo sin resolver sigue ahí hasta que se resuelve. Opcional: API anterior
      // al 27/10/2026.
      outcome: z
        .object({
          printed: z.number().int(),
          failed: z.number().int(),
          cancelled: z.number().int(),
          active: z.number().int(),
        })
        .optional(),
    })
  ),
})
export type SerieDashboard = z.infer<typeof SerieDashboard>

// Pruebas (POST /api/sourceprintjobs/test): un trabajo de prueba entra por la
// misma tabla que los del ERP y se imprime de verdad. pdfBlob en base64, o
// pdfUrl, que el Worker descarga como la del ERP; sin ninguno, la API pone un
// PDF mínimo.
export const ORIGEN_PRUEBA = "PRUEBA"
export type TrabajoPrueba = {
  sourceSystem: typeof ORIGEN_PRUEBA
  externalJobId: string
  storeId: number
  documentType: string
  channel: string
  pdfBlob?: string
  pdfUrl?: string
}
export const PruebaCreada = z.object({ externalJobId: z.string() })
// POST /api/sourceprintjobs/test/cancel-all[?runId=…]
export const PruebasCanceladas = z.object({ cancelled: z.number().int() })

export const SeveridadConexion = z.enum([
  "healthy",
  "warning",
  "critical",
  "neutral",
])
export type SeveridadConexion = z.infer<typeof SeveridadConexion>

export const Impresora = z.object({
  printerId: z.number().int(),
  printerName: z.string(),
  // Sin cola, solo se imprime por IP (host).
  spoolQueue: z.string().nullable(),
  host: z.string().nullable(),
  storeId: z.number().int(),
  isActive: z.boolean(),
  // No se edita en pantalla, pero editar sin enviarlo lo borraría.
  capabilitiesJson: z.string().nullable(),
  connectionFailuresStreak: z.number().int(),
  lastConnectionCheckAtUtc: z.string().nullable(),
  lastConnectionTransport: z.string().nullable(),
  lastConnectionError: z.string().nullable(),
  // Desde cuándo falla sin interrupción (sin conexión o sin host): el «Desde»
  // de las incidencias. Opcional: API anterior al 27/10/2026.
  connectionFailingSinceUtc: z.string().nullable().optional(),
  connectivityStatus: z.string(),
  connectivityLabel: z.string(),
  connectivitySeverity: SeveridadConexion,
  connectivityDetail: z.string().nullable(),
  ippSupported: z.boolean().nullable(),
})
export type Impresora = z.infer<typeof Impresora>

// Como la API (PrintersController.HostValido): se imprime por ipp://{host}:631.
// Lo que solo tiene cifras y puntos ha de ser una IPv4 entera.
const IPV4 = /^((25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(25[0-5]|2[0-4]\d|1?\d?\d)$/
const NOMBRE_HOST =
  /^[a-z\d]([a-z\d-]*[a-z\d])?(\.[a-z\d]([a-z\d-]*[a-z\d])?)*$/i
const hostValido = (h: string) =>
  (/^[\d.]+$/.test(h) ? IPV4 : NOMBRE_HOST).test(h)

// Alta y edición de impresoras: CreatePrinterRequest y UpdatePrinterRequest,
// con los mismos límites que la API. Se imprime primero por IP (host) y la
// cola de Windows es la alternativa: basta una de las dos.
export const DatosImpresora = z
  .object({
    printerName: z
      .string()
      .trim()
      .min(1, "Obligatorio")
      .max(120, "Máximo 120 caracteres"),
    spoolQueue: z
      .string()
      .trim()
      .max(200, "Máximo 200 caracteres")
      .transform((q) => q || null),
    host: z
      .string()
      .trim()
      .max(255, "Máximo 255 caracteres")
      .refine(
        (h) => !h || hostValido(h),
        "Solo la IP o el nombre, sin http://, puerto ni ruta"
      )
      .transform((h) => h || null),
    storeId: z.number({ error: "Elige una tienda" }).int(),
    isActive: z.boolean(),
    capabilitiesJson: z.string().nullable(),
  })
  .refine((d) => d.host || d.spoolQueue, {
    message: "Indica la IP o la cola de Windows",
    path: ["host"],
  })
export type DatosImpresora = z.output<typeof DatosImpresora>

// POST /api/printers/{id}/netconnection
export const Conexion = z.object({
  reachable: z.boolean(),
  latencyMs: z.number().nullable(),
  transport: z.string().nullable(),
  connectivityLabel: z.string(),
  connectivitySeverity: SeveridadConexion,
})
