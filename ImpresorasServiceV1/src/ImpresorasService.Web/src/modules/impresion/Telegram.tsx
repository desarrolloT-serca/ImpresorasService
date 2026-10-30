import { Fragment, useState, type ReactNode } from "react"
import type { ColumnDef } from "@tanstack/react-table"
import { PencilIcon, PlusIcon, SendIcon, Trash2Icon } from "lucide-react"
import { toast } from "sonner"
import {
  DatosChat,
  type ChatTelegram,
  type ConfigTelegram,
  type HorarioTienda,
  type Salud,
  type Tienda,
} from "@serca/contratos"

import { DataTable } from "@/components/data-table"
import { Panel } from "@/components/ficha"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Spinner } from "@/components/ui/spinner"
import { ErrorApi } from "@/core/api"
import { cn } from "@/lib/utils"
import {
  useAnadirChat,
  useBorrarChat,
  useChatsTelegram,
  useConfigTelegram,
  useEditarChat,
  useGuardarConfigTelegram,
  useGuardarTokenTelegram,
  usePruebaTelegram,
  useTiendas,
  useHistorialTelegram,
  useVistaPreviaTelegram,
} from "./api"
import { CLASE_TONO, fechaHora, nombreTienda, plural, SALUD } from "./formato"
import { Campo, DialogoFormulario } from "./formulario"

const error = (e: unknown, defecto: string) =>
  toast.error(e instanceof ErrorApi ? e.problema.title : defecto)

const SEVERIDAD_ITEMS = [
  { value: "warning", label: "Aviso y crítica" },
  { value: "critical", label: "Solo crítica" },
]

// Alertas por Telegram (alertas/configuracion.blade.php del PHP): cuándo
// avisar y a qué chats. Activarlo va en la configuración del servidor
// (Telegram:Enabled); el token del bot, aquí o en el servidor. Junto a la
// configuración, el aviso tal como llega; cada chat se puede pausar, editar y
// probar solo (decisión del usuario, 27/10/2026).
export default function Telegram() {
  const config = useConfigTelegram()
  const chats = useChatsTelegram()
  const tiendas = useTiendas().data ?? []
  const borrar = useBorrarChat()
  const editar = useEditarChat()
  const prueba = usePruebaTelegram()
  const [editando, setEditando] = useState<ChatTelegram | "nuevo">()

  const probar = (chatId?: number) =>
    prueba.mutate(chatId, {
      onSuccess: ({ results }) => {
        const fallos = results.filter((r) => !r.ok)
        if (fallos.length)
          toast.error(`No llegó a ${fallos.map((r) => r.chatId).join(", ")}`)
        else
          toast.success(
            chatId === undefined
              ? `Prueba enviada a ${plural(results.length, "chat", "chats")}`
              : `Prueba enviada al chat ${chatId}`
          )
      },
      onError: (e) => error(e, "No se pudo enviar la prueba"),
    })

  const columnas: ColumnDef<ChatTelegram, unknown>[] = [
    {
      id: "chat",
      header: "Chat",
      size: 160,
      accessorKey: "chatId",
      cell: ({ row: { original: c } }) => (
        <span className="font-mono">{c.chatId}</span>
      ),
    },
    {
      id: "descripcion",
      header: "Descripción",
      size: 0,
      accessorFn: (c) => c.description ?? "",
      cell: ({ getValue }) => (
        <span className="block truncate">{getValue<string>()}</span>
      ),
    },
    {
      id: "tienda",
      header: "Tienda",
      size: 170,
      accessorFn: (c) =>
        c.storeId === null ? "Todas" : nombreTienda(c.storeId, tiendas),
    },
    {
      // Pausarlo sin perderlo: deja de recibir alertas hasta reactivarlo.
      id: "estado",
      header: "Recibe",
      size: 120,
      cell: ({ row: { original: c } }) => (
        <label className="flex items-center gap-2 text-sm">
          <Checkbox
            checked={c.isActive}
            disabled={editar.isPending}
            onCheckedChange={(v) =>
              editar.mutate(
                {
                  chatId: c.chatId,
                  description: c.description,
                  storeId: c.storeId,
                  isActive: !!v,
                },
                {
                  onSuccess: () =>
                    toast.success(
                      v ? "Chat activado" : "Chat en pausa: no recibe alertas"
                    ),
                  onError: (e) => error(e, "No se pudo cambiar"),
                }
              )
            }
          />
          <Badge
            variant="outline"
            className={cn(
              "border-transparent",
              CLASE_TONO[c.isActive ? "ok" : "neutro"]
            )}
          >
            {c.isActive ? "Activo" : "En pausa"}
          </Badge>
        </label>
      ),
    },
    {
      id: "alta",
      header: "Alta",
      size: 140,
      cell: ({ row: { original: c } }) => fechaHora(c.createdAtUtc),
    },
    {
      id: "acciones",
      header: "",
      size: 104,
      enableSorting: false,
      cell: ({ row: { original: c } }) => (
        <div className="flex justify-end gap-1">
          <Button
            size="icon-xs"
            variant="ghost"
            aria-label="Probar este chat"
            title="Mandar una prueba solo a este chat"
            disabled={prueba.isPending}
            onClick={() =>
              window.confirm(
                `Se mandará un mensaje de prueba al chat ${c.chatId}.`
              ) && probar(c.chatId)
            }
          >
            <SendIcon />
          </Button>
          <Button
            size="icon-xs"
            variant="ghost"
            aria-label="Editar chat"
            title="Editar"
            onClick={() => setEditando(c)}
          >
            <PencilIcon />
          </Button>
          <Button
            size="icon-xs"
            variant="ghost"
            className="text-destructive hover:text-destructive"
            aria-label="Quitar chat"
            title="Quitar"
            onClick={() =>
              window.confirm(
                `Se dejarán de mandar alertas al chat ${c.chatId}.`
              ) &&
              borrar.mutate(c.chatId, {
                onSuccess: () => toast.success("Chat quitado"),
                onError: (e) => error(e, "No se pudo quitar"),
              })
            }
          >
            <Trash2Icon />
          </Button>
        </div>
      ),
    },
  ]

  return (
    <Panel className="@container [&_table]:min-w-[48rem] [&_table]:table-fixed">
      <div className="grid gap-4 @5xl:grid-cols-[minmax(0,1fr)_minmax(0,24rem)]">
        {config.data ? (
          <Configuracion actual={config.data} />
        ) : (
          <section className="flex items-center justify-center rounded-lg border bg-card p-4">
            <Spinner />
          </section>
        )}
        <VistaPrevia />
      </div>
      <section className="flex min-h-60 flex-1 flex-col gap-3 rounded-lg border bg-card p-4">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="font-semibold">Chats que reciben las alertas</h2>
          <span className="text-xs text-muted-foreground">
            {plural(
              (chats.data ?? []).filter((c) => c.isActive).length,
              "activo",
              "activos"
            )}{" "}
            de {(chats.data ?? []).length}
          </span>
          <Button
            size="sm"
            variant="outline"
            className="ml-auto"
            disabled={prueba.isPending}
            onClick={() =>
              window.confirm(
                "Se mandará un mensaje de prueba a todos los chats activos."
              ) && probar()
            }
          >
            <SendIcon data-icon="inline-start" />
            {prueba.isPending ? "Enviando…" : "Probar todos"}
          </Button>
          <Button size="sm" onClick={() => setEditando("nuevo")}>
            <PlusIcon data-icon="inline-start" />
            Añadir chat
          </Button>
        </div>
        <DataTable
          columns={columnas}
          data={chats.data ?? []}
          getRowId={(c) => String(c.chatId)}
          paginar={false}
          emptyContent={
            chats.isPending
              ? undefined
              : "No hay chats: no se manda ninguna alerta."
          }
        />
      </section>
      <Historial />
      {editando && (
        <FormularioChat
          chat={editando === "nuevo" ? undefined : editando}
          tiendas={tiendas}
          onCerrar={() => setEditando(undefined)}
        />
      )}
    </Panel>
  )
}

// El aviso tal como llega al chat: el mismo texto que manda el Worker
// (GET /telegram/preview), con una tienda de ejemplo.
function VistaPrevia() {
  const vista = useVistaPreviaTelegram()
  return (
    <section className="space-y-3 rounded-lg border bg-card p-4">
      <div>
        <h2 className="font-semibold">Así llega</h2>
        <p className="text-xs text-muted-foreground">
          Ejemplo con una tienda en crítica y cuando se recupera.
        </p>
      </div>
      {vista.data ? (
        <div className="space-y-2 rounded-md bg-muted/50 p-3">
          <MensajeTelegram html={vista.data.alerta} />
          <MensajeTelegram html={vista.data.recuperada} />
        </div>
      ) : vista.isError ? (
        <p className="text-sm text-muted-foreground">
          No disponible con esta versión de la API.
        </p>
      ) : (
        <Spinner />
      )}
    </section>
  )
}

// Pinta el HTML de Telegram (b, i, code) sin innerHTML: lo demás va como
// texto, así un nombre raro no se convierte en marcado.
export function MensajeTelegram({ html }: { html: string }) {
  const abiertas: string[] = []
  const partes: ReactNode[] = []
  html.split(/(<\/?(?:b|i|code)>)/).forEach((p, k) => {
    const etiqueta = p.match(/^<(\/?)(b|i|code)>$/)
    if (etiqueta) {
      if (etiqueta[1]) abiertas.pop()
      else abiertas.push(etiqueta[2])
      return
    }
    if (!p) return
    let nodo: ReactNode = sinEntidades(p)
    for (const t of [...abiertas].reverse())
      nodo =
        t === "b" ? (
          <b className="font-semibold">{nodo}</b>
        ) : t === "i" ? (
          <i>{nodo}</i>
        ) : (
          <code className="rounded bg-muted px-1 font-mono text-[0.85em]">
            {nodo}
          </code>
        )
    partes.push(<Fragment key={k}>{nodo}</Fragment>)
  })
  return (
    <div className="w-fit max-w-full overflow-hidden rounded-lg rounded-tl-none border bg-card px-3 py-2 text-sm break-words whitespace-pre-wrap shadow-sm">
      {partes}
    </div>
  )
}

const sinEntidades = (t: string) =>
  t
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&amp;/g, "&")

function Configuracion({ actual }: { actual: ConfigTelegram }) {
  const guardar = useGuardarConfigTelegram()
  const [valores, setValores] = useState({
    minSeverity: actual.minSeverity,
    notifyOnRecovery: actual.notifyOnRecovery,
    soloEnHorario: actual.soloEnHorario ?? false,
    intervalo: String(actual.checkIntervalMinutes),
  })
  const [mensaje, setMensaje] = useState("")
  const cambiado =
    valores.minSeverity !== actual.minSeverity ||
    valores.notifyOnRecovery !== actual.notifyOnRecovery ||
    valores.soloEnHorario !== (actual.soloEnHorario ?? false) ||
    valores.intervalo !== String(actual.checkIntervalMinutes)
  return (
    <section className="space-y-3 rounded-lg border bg-card p-4">
      <div className="flex items-center gap-2">
        <h2 className="font-semibold">Cuándo avisar</h2>
        <Badge
          variant="outline"
          className={cn(
            "border-transparent",
            CLASE_TONO[actual.enabled ? "ok" : "aviso"]
          )}
          title="Se cambia en la configuración del servidor (Telegram:Enabled)"
        >
          {actual.enabled
            ? "Activado en el servidor"
            : "Desactivado en el servidor"}
        </Badge>
      </div>
      <div className="flex flex-wrap items-end gap-4">
        <Campo id="telegram-severidad" etiqueta="Avisar de">
          <Select
            items={SEVERIDAD_ITEMS}
            value={valores.minSeverity}
            disabled={guardar.isPending}
            onValueChange={(v) =>
              setValores((x) => ({
                ...x,
                minSeverity: v as ConfigTelegram["minSeverity"],
              }))
            }
          >
            <SelectTrigger id="telegram-severidad" className="w-44">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {SEVERIDAD_ITEMS.map((i) => (
                <SelectItem key={i.value} value={i.value}>
                  {i.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </Campo>
        <Campo id="telegram-intervalo" etiqueta="Revisar cada (minutos)">
          <Input
            id="telegram-intervalo"
            inputMode="numeric"
            className="w-24"
            value={valores.intervalo}
            disabled={guardar.isPending}
            onChange={(e) =>
              setValores((x) => ({ ...x, intervalo: e.target.value }))
            }
          />
        </Campo>
        <label className="flex h-8 items-center gap-2 text-sm">
          <Checkbox
            checked={valores.notifyOnRecovery}
            disabled={guardar.isPending}
            onCheckedChange={(v) =>
              setValores((x) => ({ ...x, notifyOnRecovery: !!v }))
            }
          />
          Avisar también cuando se recupere
        </label>
        {/* Sin dato con una API anterior: no se ofrece. */}
        {actual.soloEnHorario !== undefined && (
          <label
            className="flex h-8 items-center gap-2 text-sm"
            title="Lo que pase fuera se avisa al abrir, si sigue pasando. El horario se cambia en Umbrales."
          >
            <Checkbox
              checked={valores.soloEnHorario}
              disabled={guardar.isPending}
              onCheckedChange={(v) =>
                setValores((x) => ({ ...x, soloEnHorario: !!v }))
              }
            />
            Solo en horario de tienda
            {actual.horario && (
              <span className="text-xs text-muted-foreground">
                ({textoHorario(actual.horario)})
              </span>
            )}
          </label>
        )}
        <Button
          size="sm"
          className="ml-auto"
          disabled={!cambiado || guardar.isPending}
          onClick={() => {
            if (
              !/^\d{1,4}$/.test(valores.intervalo.trim()) ||
              Number(valores.intervalo) < 1
            ) {
              setMensaje(
                "El intervalo es un número de minutos, de 1 en adelante."
              )
              return
            }
            setMensaje("")
            guardar.mutate(
              {
                minSeverity: valores.minSeverity,
                notifyOnRecovery: valores.notifyOnRecovery,
                checkIntervalMinutes: Number(valores.intervalo),
                ...(actual.soloEnHorario !== undefined && {
                  soloEnHorario: valores.soloEnHorario,
                }),
              },
              {
                onSuccess: () => toast.success("Configuración guardada"),
                onError: (e) =>
                  setMensaje(
                    e instanceof ErrorApi
                      ? e.problema.title
                      : "No se pudo guardar"
                  ),
              }
            )
          }}
        >
          {guardar.isPending ? "Guardando…" : "Guardar"}
        </Button>
      </div>
      {mensaje && (
        <p role="alert" className="text-sm text-destructive">
          {mensaje}
        </p>
      )}
      {/* Sin dato con una API anterior: no se ofrece. */}
      {actual.tokenOrigen !== undefined && <TokenBot actual={actual} />}
    </section>
  )
}

// Token del bot (lo da @BotFather). Se escribe y no se vuelve a ver: solo de
// dónde sale y sus 4 últimos caracteres (decisión del usuario, 27/10/2026).
function TokenBot({ actual }: { actual: ConfigTelegram }) {
  const guardar = useGuardarTokenTelegram()
  const [token, setToken] = useState("")
  const [mensaje, setMensaje] = useState("")
  const enviar = (valor: string) => {
    setMensaje("")
    guardar.mutate(valor, {
      onSuccess: ({ bot }) => {
        setToken("")
        toast.success(
          bot ? `Token guardado: @${bot}` : "Vuelve el token del servidor"
        )
      },
      onError: (e) =>
        setMensaje(
          e instanceof ErrorApi ? e.problema.title : "No se pudo guardar"
        ),
    })
  }
  return (
    <div className="space-y-2 border-t pt-3">
      <div className="flex flex-wrap items-end gap-4">
        <Campo id="telegram-token" etiqueta="Token del bot">
          <Input
            id="telegram-token"
            type="password"
            autoComplete="off"
            className="w-80"
            placeholder={
              actual.tokenFinal ? `···${actual.tokenFinal}` : "Sin token"
            }
            value={token}
            disabled={guardar.isPending}
            onChange={(e) => setToken(e.target.value)}
          />
        </Campo>
        <span className="flex h-8 items-center text-xs text-muted-foreground">
          {actual.tokenOrigen === "pantalla"
            ? "Puesto aquí"
            : actual.tokenOrigen === "servidor"
              ? "El del servidor (Telegram__BotToken)"
              : "Sin token: no se manda nada"}
        </span>
        {actual.tokenOrigen === "pantalla" && (
          <Button
            size="sm"
            variant="ghost"
            disabled={guardar.isPending}
            onClick={() =>
              window.confirm(
                "Se quitará el token puesto aquí y volverá el del servidor."
              ) && enviar("")
            }
          >
            Quitar
          </Button>
        )}
        <Button
          size="sm"
          className="ml-auto"
          disabled={!token.trim() || guardar.isPending}
          onClick={() => enviar(token.trim())}
        >
          {guardar.isPending ? "Comprobando…" : "Guardar token"}
        </Button>
      </div>
      {mensaje && (
        <p role="alert" className="text-sm text-destructive">
          {mensaje}
        </p>
      )}
    </div>
  )
}

const TODAS = "todas"

// Alta o edición de un chat. Al editar, el número no cambia (para otro chat,
// se añade uno nuevo) y se conserva si está activo o en pausa.
function FormularioChat({
  chat,
  tiendas,
  onCerrar,
}: {
  chat?: ChatTelegram
  tiendas: readonly Tienda[]
  onCerrar: () => void
}) {
  const anadir = useAnadirChat()
  const editar = useEditarChat()
  const items = [
    { value: TODAS, label: "Todas" },
    ...tiendas
      .filter((t) => t.isActive || t.storeId === chat?.storeId)
      .map((t) => ({
        value: String(t.storeId),
        label: nombreTienda(t.storeId, tiendas),
      })),
  ]
  return (
    <DialogoFormulario
      titulo={chat ? `Editar chat ${chat.chatId}` : "Añadir chat"}
      aviso={chat ? "Chat guardado" : "Chat añadido"}
      esquema={DatosChat}
      inicial={{
        chatId: chat ? String(chat.chatId) : "",
        description: chat?.description ?? "",
        storeId: chat?.storeId ?? (null as number | null),
      }}
      guardar={(datos) =>
        chat
          ? editar.mutateAsync({
              chatId: chat.chatId,
              description: datos.description,
              storeId: datos.storeId,
              isActive: chat.isActive,
            })
          : anadir.mutateAsync(datos)
      }
      onCerrar={onCerrar}
    >
      {({ valores, errores, enviando, cambiar, id }) => (
        <>
          <Campo
            id={id("chatId")}
            etiqueta="Chat"
            error={errores.chatId}
            ayuda={
              chat
                ? "El número no cambia: para otro chat, añade uno nuevo."
                : "El número del chat (los grupos empiezan por -100). No vale el enlace de invitación: añade el bot al grupo y reenvía un mensaje del grupo a @RawDataBot para ver su número."
            }
          >
            <Input
              id={id("chatId")}
              autoFocus={!chat}
              className="font-mono"
              value={valores.chatId}
              disabled={enviando || !!chat}
              aria-invalid={!!errores.chatId}
              onChange={(e) => cambiar("chatId", e.target.value)}
            />
          </Campo>
          <Campo
            id={id("description")}
            etiqueta="Descripción"
            error={errores.description}
          >
            <Input
              id={id("description")}
              autoFocus={!!chat}
              value={valores.description}
              disabled={enviando}
              aria-invalid={!!errores.description}
              onChange={(e) => cambiar("description", e.target.value)}
            />
          </Campo>
          <Campo
            id={id("storeId")}
            etiqueta="Tienda"
            ayuda="«Todas»: recibe las alertas de cualquier tienda."
          >
            <Select
              items={items}
              value={valores.storeId === null ? TODAS : String(valores.storeId)}
              disabled={enviando}
              onValueChange={(v) =>
                cambiar("storeId", v === TODAS ? null : Number(v))
              }
            >
              <SelectTrigger id={id("storeId")} className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {items.map((i) => (
                  <SelectItem key={i.value} value={i.value}>
                    {i.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Campo>
        </>
      )}
    </DialogoFormulario>
  )
}

const DIAS = ["L", "M", "X", "J", "V", "S", "D"]

// «L-S 09:00-21:00»: días seguidos como tramo, sueltos separados por comas.
function textoHorario(h: HorarioTienda) {
  const dias = [...h.dias].sort((a, b) => a - b)
  const seguidos = dias.every((d, i) => i === 0 || d === dias[i - 1] + 1)
  const texto =
    seguidos && dias.length > 2
      ? `${DIAS[dias[0] - 1]}-${DIAS[dias[dias.length - 1] - 1]}`
      : dias.map((d) => DIAS[d - 1]).join(",")
  return `${texto} ${h.desde}-${h.hasta}`
}

// Últimos avisos mandados: cuándo, a qué tienda, de qué y si llegó. Quedan
// como eventos en la BD aunque no lleguen (decisión del usuario, 27/10/2026).
function Historial() {
  const historial = useHistorialTelegram()
  return (
    <section className="space-y-3 rounded-lg border bg-card p-4">
      <div className="flex items-baseline gap-2">
        <h2 className="font-semibold">Últimos avisos</h2>
        <span className="text-xs text-muted-foreground">
          los 50 más recientes, lleguen o no
        </span>
      </div>
      {historial.isError ? (
        <p className="text-sm text-muted-foreground">
          No disponible con esta versión de la API.
        </p>
      ) : !historial.data ? (
        <Spinner />
      ) : !historial.data.length ? (
        <p className="text-sm text-muted-foreground">
          Todavía no se ha mandado ningún aviso.
        </p>
      ) : (
        <ul className="max-h-72 divide-y overflow-auto rounded-md border">
          {historial.data.map((a, i) => {
            const salud = SALUD[a.health as Salud] ?? SALUD.warning
            return (
              <li
                key={i}
                className="grid grid-cols-[7.5rem_9rem_6rem_minmax(0,1fr)_auto] items-center gap-3 px-3 py-2 text-sm"
              >
                <span className="text-muted-foreground tabular-nums">
                  {fechaHora(a.occurredAtUtc)}
                </span>
                <span className="truncate">
                  {a.storeId === null
                    ? "—"
                    : `${a.storeId} · ${a.storeName ?? ""}`}
                </span>
                <Badge
                  variant="outline"
                  className={cn(
                    "w-fit border-transparent",
                    CLASE_TONO[salud.tono]
                  )}
                >
                  {a.health === "healthy" ? "Recuperada" : salud.etiqueta}
                </Badge>
                <span className="truncate" title={a.reason ?? undefined}>
                  {a.reason}
                </span>
                {a.delivered ? (
                  <span className="text-xs text-muted-foreground">
                    entregado
                  </span>
                ) : (
                  <span className="text-xs font-medium text-destructive">
                    no llegó
                  </span>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
