import { useId, useState } from "react"
import { TriangleAlertIcon } from "lucide-react"
import { toast } from "sonner"
import {
  ESTADOS_TRABAJO as E,
  MAX_NOTA_REIMPRESION,
  MOTIVOS_REIMPRESION,
  PERMISOS_IMPRESION as P,
  type MotivoReimpresion,
  type Tienda,
  type TrabajoCola,
} from "@serca/contratos"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { ErrorApi } from "@/core/api"
import { usePermiso } from "@/core/sesion"
import { useReimprimir } from "./api"
import { Campo } from "./formulario"
import {
  ESTADOS,
  documentoErp,
  faltaParaReimprimir,
  fechaHora,
  nombreTienda,
} from "./formato"

const MOTIVOS = Object.entries(MOTIVOS_REIMPRESION).map(([value, label]) => ({
  value,
  label,
}))

// Reimprimir lo que ya salió (decisión del usuario, 26/10/2026): se puede,
// porque un documento se imprime mal, se pierde o se moja, pero cada copia
// puede ser un duplicado. Por eso la ventana avisa, pide el motivo y que se
// acepte el riesgo, y la API lo registra con quién (registro de reimpresiones).
export function DialogoReimprimir({
  trabajo: t,
  tiendas,
  onCerrar,
}: {
  trabajo: TrabajoCola
  tiendas: Tienda[]
  onCerrar: () => void
}) {
  const reimprimir = useReimprimir()
  // Con el usuario de la tienda, que comparten todos sus operadores, quien
  // reimprime se identifica (decisión del usuario, 27/10/2026).
  const identificarse = usePermiso(P.identificarse)
  const [operador, setOperador] = useState("")
  const [contrasena, setContrasena] = useState("")
  const [motivo, setMotivo] = useState<MotivoReimpresion | "">("")
  const [nota, setNota] = useState("")
  const [entiendo, setEntiendo] = useState(false)
  const [intentado, setIntentado] = useState(false)
  const base = useId()
  const falta =
    faltaParaReimprimir({ motivo, nota, entiendo }) ??
    (identificarse && (!/^\d{1,5}$/.test(operador.trim()) || !contrasena)
      ? "Indica tu código de operador y tu contraseña"
      : null)
  const doc = documentoErp(t.documentType, t.externalJobId)

  async function enviar() {
    setIntentado(true)
    if (falta || !motivo) return
    try {
      const r = await reimprimir.mutateAsync({
        id: t.jobId,
        peticion: {
          reason: motivo,
          note: nota.trim() || undefined,
          ...(identificarse && { operador: operador.trim(), contrasena }),
        },
      })
      if (r.status === "ErrorFinal")
        toast.error(
          `Sin impresora para el trabajo (${r.errorCode ?? "sin regla"})`
        )
      else toast.success("Reimpresión enviada y registrada")
      // La copia anterior, si seguía en la cola de la impresora (decisión del
      // usuario, 27/10/2026): cancelada, o aviso fijo si puede salir también.
      if (r.printerMessage)
        if (r.printerCancel === "Cancelado" || r.printerCancel === "YaSalio")
          toast.info(r.printerMessage)
        else
          toast.warning(r.printerMessage, {
            duration: Infinity,
            closeButton: true,
          })
      onCerrar()
    } catch (e) {
      setContrasena("")
      toast.error(
        e instanceof ErrorApi ? e.problema.title : "No se pudo reimprimir"
      )
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && !reimprimir.isPending && onCerrar()}
    >
      <DialogContent className="sm:max-w-lg">
        <DialogTitle>Reimprimir un documento que ya salió</DialogTitle>
        <DialogDescription>
          {doc ? `${doc.nombre} ${t.externalJobId}` : t.externalJobId} ·{" "}
          {nombreTienda(t.storeId, tiendas)} ·{" "}
          {t.printerName ?? "sin impresora"}
        </DialogDescription>

        <Alert variant="destructive">
          <TriangleAlertIcon />
          <AlertTitle>Puede salir duplicado</AlertTitle>
          <AlertDescription>
            {t.status === E.impreso
              ? "La impresora confirmó que este documento se imprimió."
              : "No se pudo confirmar si este documento se imprimió: puede que sí."}{" "}
            Reimprímelo solo si la copia anterior no vale o no está. Queda
            registrado quién lo reimprime, cuándo y por qué.
            {!!t.reprints && (
              <>
                {" "}
                <strong>
                  Ya se ha reimpreso{" "}
                  {t.reprints === 1 ? "1 vez" : `${t.reprints} veces`}
                  {t.sentAtUtc
                    ? `; la última salió el ${fechaHora(t.sentAtUtc)}`
                    : ""}
                  .
                </strong>
              </>
            )}
          </AlertDescription>
        </Alert>

        <div className="grid gap-3">
          <p className="text-xs text-muted-foreground">
            Estado: {ESTADOS[t.status].etiqueta} · llegó el{" "}
            {fechaHora(t.createdAtUtc)}
          </p>
          <Campo
            id={`${base}-motivo`}
            etiqueta="Motivo"
            error={intentado && !motivo ? "Elige el motivo" : undefined}
          >
            <Select
              items={MOTIVOS}
              value={motivo || null}
              disabled={reimprimir.isPending}
              onValueChange={(v) => setMotivo((v as MotivoReimpresion) ?? "")}
            >
              <SelectTrigger id={`${base}-motivo`} className="w-full">
                <SelectValue placeholder="¿Por qué se reimprime?" />
              </SelectTrigger>
              <SelectContent>
                {MOTIVOS.map((m) => (
                  <SelectItem key={m.value} value={m.value}>
                    {m.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Campo>
          <Campo
            id={`${base}-nota`}
            etiqueta={motivo === "OTRO" ? "Explicación" : "Nota (opcional)"}
            error={
              intentado && motivo === "OTRO" && !nota.trim()
                ? "Explica el motivo"
                : undefined
            }
          >
            <Textarea
              id={`${base}-nota`}
              value={nota}
              maxLength={MAX_NOTA_REIMPRESION}
              rows={2}
              disabled={reimprimir.isPending}
              onChange={(e) => setNota(e.target.value)}
            />
          </Campo>
          {identificarse && (
            <div className="grid gap-2 rounded-md border bg-muted/40 p-3">
              <p className="text-xs text-muted-foreground">
                Has entrado con el usuario de la tienda: identifícate para que
                conste quién reimprime.
              </p>
              <div className="grid grid-cols-2 gap-3">
                <Campo id={`${base}-operador`} etiqueta="Tu código de operador">
                  <Input
                    id={`${base}-operador`}
                    inputMode="numeric"
                    autoComplete="off"
                    value={operador}
                    disabled={reimprimir.isPending}
                    onChange={(e) => setOperador(e.target.value)}
                  />
                </Campo>
                <Campo id={`${base}-contrasena`} etiqueta="Tu contraseña">
                  <Input
                    id={`${base}-contrasena`}
                    type="password"
                    autoComplete="off"
                    value={contrasena}
                    disabled={reimprimir.isPending}
                    onChange={(e) => setContrasena(e.target.value)}
                  />
                </Campo>
              </div>
            </div>
          )}
          <label className="flex items-start gap-2 text-sm">
            <Checkbox
              checked={entiendo}
              disabled={reimprimir.isPending}
              onCheckedChange={(v) => setEntiendo(!!v)}
              className="mt-0.5"
            />
            Entiendo que puede salir una copia duplicada de este documento.
          </label>
          {intentado && falta && (
            <p aria-live="polite" className="text-xs text-destructive">
              {falta}
            </p>
          )}
        </div>

        <DialogFooter>
          <Button
            variant="outline"
            disabled={reimprimir.isPending}
            onClick={onCerrar}
          >
            No reimprimir
          </Button>
          <Button
            variant="destructive"
            disabled={reimprimir.isPending || (intentado && !!falta)}
            onClick={enviar}
          >
            {reimprimir.isPending ? "Enviando…" : "Reimprimir"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
