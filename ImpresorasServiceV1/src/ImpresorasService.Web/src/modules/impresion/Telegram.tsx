import { useState } from "react"
import type { ColumnDef } from "@tanstack/react-table"
import { PlusIcon, SendIcon } from "lucide-react"
import { toast } from "sonner"
import {
  DatosChat,
  type ChatTelegram,
  type ConfigTelegram,
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
import { ErrorApi } from "@/core/api"
import { cn } from "@/lib/utils"
import {
  useAnadirChat,
  useBorrarChat,
  useChatsTelegram,
  useConfigTelegram,
  useGuardarConfigTelegram,
  usePruebaTelegram,
  useTiendas,
} from "./api"
import { CLASE_TONO, fechaHora, nombreTienda } from "./formato"
import { Campo, DialogoFormulario } from "./formulario"

const error = (e: unknown, defecto: string) =>
  toast.error(e instanceof ErrorApi ? e.problema.title : defecto)

const SEVERIDAD_ITEMS = [
  { value: "warning", label: "Aviso y crítica" },
  { value: "critical", label: "Solo crítica" },
]

// Alertas por Telegram (alertas/configuracion.blade.php del PHP): cuándo
// avisar y a qué chats. Activarlo y el token del bot van en la configuración
// del servidor (Telegram:Enabled, Telegram:BotToken), nunca aquí.
export default function Telegram() {
  const config = useConfigTelegram()
  const chats = useChatsTelegram()
  const tiendas = useTiendas().data ?? []
  const borrar = useBorrarChat()
  const prueba = usePruebaTelegram()
  const [anadiendo, setAnadiendo] = useState(false)

  const columnas: ColumnDef<ChatTelegram, unknown>[] = [
    {
      id: "chat",
      header: "Chat",
      size: 170,
      accessorKey: "chatId",
      cell: ({ row: { original: c } }) => <span className="font-mono">{c.chatId}</span>,
    },
    {
      id: "descripcion",
      header: "Descripción",
      size: 0,
      accessorFn: (c) => c.description ?? "",
      cell: ({ getValue }) => <span className="block truncate">{getValue<string>()}</span>,
    },
    {
      id: "tienda",
      header: "Tienda",
      size: 180,
      accessorFn: (c) => (c.storeId === null ? "Todas" : nombreTienda(c.storeId, tiendas)),
    },
    {
      id: "estado",
      header: "Estado",
      size: 90,
      cell: ({ row: { original: c } }) => (
        <Badge
          variant="outline"
          className={cn("border-transparent", CLASE_TONO[c.isActive ? "ok" : "neutro"])}
        >
          {c.isActive ? "Activo" : "Inactivo"}
        </Badge>
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
      size: 100,
      enableSorting: false,
      cell: ({ row: { original: c } }) => (
        <div className="flex justify-end">
          <Button
            size="xs"
            variant="destructive"
            onClick={() =>
              window.confirm(`Se dejarán de mandar alertas al chat ${c.chatId}.`) &&
              borrar.mutate(c.chatId, {
                onSuccess: () => toast.success("Chat quitado"),
                onError: (e) => error(e, "No se pudo quitar"),
              })
            }
          >
            Quitar
          </Button>
        </div>
      ),
    },
  ]

  return (
    <Panel className="[&_table]:min-w-[48rem] [&_table]:table-fixed">
      {config.data && <Configuracion actual={config.data} />}
      <DataTable
        columns={columnas}
        data={chats.data ?? []}
        paginar={false}
        emptyContent={chats.isPending ? undefined : "No hay chats: no se manda ninguna alerta."}
        toolbar={
          <>
            <h2 className="self-center font-semibold">Chats que reciben las alertas</h2>
            <Button
              size="sm"
              variant="outline"
              className="ml-auto"
              disabled={prueba.isPending}
              onClick={() =>
                window.confirm("Se mandará un mensaje de prueba a todos los chats activos.") &&
                prueba.mutate(undefined, {
                  onSuccess: ({ results }) => {
                    const fallos = results.filter((r) => !r.ok)
                    if (fallos.length)
                      toast.error(`No llegó a ${fallos.map((r) => r.chatId).join(", ")}`)
                    else toast.success(`Prueba enviada a ${results.length} chats`)
                  },
                  onError: (e) => error(e, "No se pudo enviar la prueba"),
                })
              }
            >
              <SendIcon data-icon="inline-start" />
              {prueba.isPending ? "Enviando…" : "Enviar prueba"}
            </Button>
            <Button size="sm" onClick={() => setAnadiendo(true)}>
              <PlusIcon data-icon="inline-start" />
              Añadir chat
            </Button>
          </>
        }
      />
      {anadiendo && <FormularioChat tiendas={tiendas} onCerrar={() => setAnadiendo(false)} />}
    </Panel>
  )
}

function Configuracion({ actual }: { actual: ConfigTelegram }) {
  const guardar = useGuardarConfigTelegram()
  const [valores, setValores] = useState({
    minSeverity: actual.minSeverity,
    notifyOnRecovery: actual.notifyOnRecovery,
    intervalo: String(actual.checkIntervalMinutes),
  })
  const [mensaje, setMensaje] = useState("")
  const cambiado =
    valores.minSeverity !== actual.minSeverity ||
    valores.notifyOnRecovery !== actual.notifyOnRecovery ||
    valores.intervalo !== String(actual.checkIntervalMinutes)
  return (
    <section className="space-y-3 rounded-lg border bg-card p-4">
      <div className="flex items-center gap-2">
        <h2 className="font-semibold">Cuándo avisar</h2>
        <Badge
          variant="outline"
          className={cn("border-transparent", CLASE_TONO[actual.enabled ? "ok" : "aviso"])}
          title="Se cambia en la configuración del servidor (Telegram:Enabled)"
        >
          {actual.enabled ? "Activado en el servidor" : "Desactivado en el servidor"}
        </Badge>
      </div>
      <div className="flex flex-wrap items-end gap-4">
        <Campo id="telegram-severidad" etiqueta="Avisar de">
          <Select
            items={SEVERIDAD_ITEMS}
            value={valores.minSeverity}
            disabled={guardar.isPending}
            onValueChange={(v) =>
              setValores((x) => ({ ...x, minSeverity: v as ConfigTelegram["minSeverity"] }))
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
            onChange={(e) => setValores((x) => ({ ...x, intervalo: e.target.value }))}
          />
        </Campo>
        <label className="flex h-8 items-center gap-2 text-sm">
          <Checkbox
            checked={valores.notifyOnRecovery}
            disabled={guardar.isPending}
            onCheckedChange={(v) => setValores((x) => ({ ...x, notifyOnRecovery: !!v }))}
          />
          Avisar también cuando se recupere
        </label>
        <Button
          size="sm"
          className="ml-auto"
          disabled={!cambiado || guardar.isPending}
          onClick={() => {
            if (!/^\d{1,4}$/.test(valores.intervalo.trim()) || Number(valores.intervalo) < 1) {
              setMensaje("El intervalo es un número de minutos, de 1 en adelante.")
              return
            }
            setMensaje("")
            guardar.mutate(
              {
                minSeverity: valores.minSeverity,
                notifyOnRecovery: valores.notifyOnRecovery,
                checkIntervalMinutes: Number(valores.intervalo),
              },
              {
                onSuccess: () => toast.success("Configuración guardada"),
                onError: (e) =>
                  setMensaje(e instanceof ErrorApi ? e.problema.title : "No se pudo guardar"),
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
    </section>
  )
}

const TODAS = "todas"

function FormularioChat({
  tiendas,
  onCerrar,
}: {
  tiendas: readonly Tienda[]
  onCerrar: () => void
}) {
  const anadir = useAnadirChat()
  const items = [
    { value: TODAS, label: "Todas" },
    ...tiendas
      .filter((t) => t.isActive)
      .map((t) => ({ value: String(t.storeId), label: nombreTienda(t.storeId, tiendas) })),
  ]
  return (
    <DialogoFormulario
      titulo="Añadir chat"
      aviso="Chat añadido"
      esquema={DatosChat}
      inicial={{ chatId: "", description: "", storeId: null as number | null }}
      guardar={(datos) => anadir.mutateAsync(datos)}
      onCerrar={onCerrar}
    >
      {({ valores, errores, enviando, cambiar, id }) => (
        <>
          <Campo
            id={id("chatId")}
            etiqueta="Chat"
            error={errores.chatId}
            ayuda="El id del chat o grupo de Telegram; los grupos empiezan por -."
          >
            <Input
              id={id("chatId")}
              autoFocus
              className="font-mono"
              value={valores.chatId}
              disabled={enviando}
              aria-invalid={!!errores.chatId}
              onChange={(e) => cambiar("chatId", e.target.value)}
            />
          </Campo>
          <Campo id={id("description")} etiqueta="Descripción" error={errores.description}>
            <Input
              id={id("description")}
              value={valores.description}
              disabled={enviando}
              aria-invalid={!!errores.description}
              onChange={(e) => cambiar("description", e.target.value)}
            />
          </Campo>
          <Campo id={id("storeId")} etiqueta="Tienda" ayuda="«Todas»: recibe las alertas de cualquier tienda.">
            <Select
              items={items}
              value={valores.storeId === null ? TODAS : String(valores.storeId)}
              disabled={enviando}
              onValueChange={(v) => cambiar("storeId", v === TODAS ? null : Number(v))}
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
