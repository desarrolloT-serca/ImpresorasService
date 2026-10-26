import { z } from "zod"

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
  createdAtUtc: z.string(),
  updatedAtUtc: z.string(),
})
export type TrabajoCola = z.infer<typeof TrabajoCola>

// POST /api/printjobs/{id}/route y /cancel. Reintentar sin regla aplicable
// devuelve 200 con status ErrorFinal y su código.
export const ResultadoAccion = z.object({
  status: z.string(),
  errorCode: z.string().nullish(),
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
}

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
export const ReglasUmbral = z.object({
  queue: z.array(Umbral),
  failed: z.array(Umbral),
  missingHost: z.array(Umbral),
  conn: z.array(Umbral),
})
export type ReglasUmbral = z.infer<typeof ReglasUmbral>

// Telegram. enabled y el token van en la configuración del servidor
// (Telegram:Enabled, Telegram:BotToken), no aquí.
export const ConfigTelegram = z.object({
  enabled: z.boolean(),
  minSeverity: z.enum(["warning", "critical"]),
  notifyOnRecovery: z.boolean(),
  checkIntervalMinutes: z.number().int(),
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
// tramo, reconstruida desde la de ahora.
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
  connectivityStatus: z.string(),
  connectivityLabel: z.string(),
  connectivitySeverity: SeveridadConexion,
  connectivityDetail: z.string().nullable(),
  ippSupported: z.boolean().nullable(),
})
export type Impresora = z.infer<typeof Impresora>

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
