# Gate I-1 — ¿se puede confirmar la impresión por trabajo?

**Ejecutado el 19/08/2026** contra el parque real, con `scripts/probar-ipp-operaciones.ps1` y
`scripts/probar-ipp-jobs.ps1` (ambos de solo lectura: preguntan, no encolan nada).

Este gate bloqueaba la Fase 3.4 de `roadmapimpresoras.md` y el hallazgo H-01 de
`auditoria/revision-garantias-2026-08-12.md`.

---

## Resultado: **positivo en la pregunta que se hacía, insuficiente para el diseño que la motivaba**

### 1. ¿Soportan las impresoras `Get-Job-Attributes`?

| # | Modelo | `Get-Job-Attributes` (0x09) |
|---|---|---|
| 2 | HP LaserJet Pro MFP M26nw | **SÍ** (también `Get-Jobs`) |
| 13 | Brother MFC-L2730DW | **SÍ** (también `Get-Jobs`) |
| 15 | — | responde IPP, pero **NO** |
| 3, 6, 11, 12, 14 | — | no responden IPP |

**2 de 8 impresoras activas (25 %).** Que sean de dos fabricantes distintos indica que el soporte
es estándar donde hay IPP, no una peculiaridad de un modelo. El campo `ipp_supported` de la base de
datos coincide con la realidad medida.

### 2. El obstáculo que no estaba en el mapa: **no imprimimos por IPP**

`Get-Job-Attributes` necesita un `job-id`, y ese identificador **lo asigna la impresora cuando
recibe el trabajo**. Nosotros no se lo mandamos por IPP: `WindowsPrintSpooler` invoca SumatraPDF,
que encola en el spooler de Windows, que a su vez habla con la impresora. El `job-id` existe dentro
de la impresora, pero **nunca llega a nuestro proceso**.

Sin ese id, la operación soportada no nos sirve: no hay nada que preguntar.

### 3. `Get-Jobs` tampoco cierra el hueco

Sería la vía para correlacionar sin `job-id` (por nombre de documento u hora). Pero en la Brother,
`Get-Jobs` responde correctamente (`status 0x0000`) y **devuelve cero trabajos**, tanto con
`which-jobs=completed` como con `not-completed`. La impresora no conserva historial de trabajos
terminados.

> **Matiz honesto:** la prueba se hizo con la cola vacía y sin imprimir nada (el último trabajo del
> sistema es del 11/08). Que `completed` salga vacío es *compatible* con "no guarda historial", pero
> no lo demuestra. Confirmarlo exige imprimir un documento real y consultar acto seguido.

---

### 4. Y la puntilla: **ninguna acepta PDF**

| Impresora | `document-format-supported` |
|---|---|
| Brother MFC-L2730DW | `application/octet-stream`, `image/urf`, `image/pwg-raster` |
| HP LaserJet Pro M26nw | `image/urf`, `application/PCLm`, `application/octet-stream`, `image/jpeg` |

`image/urf`, `image/pwg-raster` y `application/PCLm` son formatos **raster** (AirPrint / IPP
Everywhere): la impresora espera recibir el documento ya convertido a píxeles. Ninguna declara
`application/pdf`.

**Esto reencuadra el problema.** SumatraPDF no está en el sistema por el transporte, sino por el
**renderizado**: convierte el PDF a lo que el driver de Windows sabe mandarle a cada impresora.
Cambiar el transporte a IPP no elimina esa necesidad — la deja al descubierto.

Si se enviara por IPP habría que mandar en un formato aceptado:

- **Raster** (`urf`/`pwg-raster`/`PCLm`): habría que rasterizar nosotros. Eso no quita la
  dependencia de SumatraPDF, la sustituye por una pieza propia menos madura que hace lo mismo.
- **`application/octet-stream`**: es la puerta trasera — mandar el PDF crudo y confiar en que la
  impresora lo autodetecte. Funciona en muchos modelos actuales, pero **no está declarado**, así
  que es una apuesta por impresora, no una garantía. **Sin verificar**: comprobarlo exige
  imprimir un documento real.

## Qué significa para la Fase 3.4

**La confirmación por trabajo, tal como estaba planteada, no es viable con la arquitectura de envío
actual.** No es que las impresoras no puedan: es que enviamos por un camino que nos deja sin el
identificador.

Quedan dos salidas, y son excluyentes:

### A. Dejar de afirmar lo que no se comprueba (Fase 3.5 / B3.2)

Renombrar `PrintedConfirmed` y sus etiquetas para que digan lo que de verdad sostienen: que la
impresora estaba libre cuando se miró, no que este documento salió. Barato, honesto, y **cubre el
100 % del parque**. No mejora la garantía; deja de exagerarla.

### B. Imprimir por IPP en vez de por SumatraPDF

Ambas impresoras con IPP soportan `Print-Job` (0x02), que **devuelve el `job-id` en la respuesta**.
Con él, `Get-Job-Attributes` da confirmación real por documento. Pero:

- **No quita SumatraPDF** (ver §4): ninguna acepta PDF, así que seguiría haciendo falta
  rasterizar. Solo funcionaría apostando por `application/octet-stream`, sin garantía declarada.
- Solo aplica al **25 %** del parque. El resto seguiría igual, así que habría que hacer (A) de
  todos modos.
- Añade un **segundo camino crítico** de impresión: dos formas de enviar, dos formas de fallar,
  sobre lo único del sistema que hoy funciona de punta a punta.

**Recomendación: hacer (A) ya. (B) solo con un requisito de negocio que exija prueba por**
**documento, y aun así acotado a una impresora y midiendo.** Hacer (B) sin (A) deja al 75 % del
parque afirmando lo mismo que hoy, y añade complejidad al camino que sostiene el producto.

---

## Repetir el gate

```powershell
# ¿Qué operaciones soporta esta impresora?
.\scripts\probar-ipp-operaciones.ps1 -Ip 192.42.172.201

# ¿Qué devuelve Get-Jobs?
.\scripts\probar-ipp-jobs.ps1 -Ip 192.42.172.201 -WhichJobs completed
```

Conviene repasarlo cuando entre material nuevo en el parque: si algún día la mayoría de las
impresoras soportan IPP, la balanza entre (A) y (B) cambia.
