# Spec: Pestaña Pruebas

**Fecha:** 2026-07-01  
**Rama:** IU  
**Audiencia:** equipo técnico únicamente (no visible a operativos)

---

## Objetivo

Añadir una pestaña "Pruebas" en la UI Laravel que permita configurar y lanzar pruebas de impresión con seguimiento en vivo, sin necesidad de scripts PowerShell ni SQL directo.

---

## Arquitectura

**Opción elegida:** file-based en Laravel (sin tocar HANA).

- Escenarios guardados como JSON en `storage/app/test-scenarios/`
- PDFs guardados en `storage/app/test-pdfs/` con índice `library.json`
- Nuevo controlador `PruebasController`
- Seguimiento en vivo via polling JS al API .NET existente

### Nuevos ficheros

```
app/Http/Controllers/PruebasController.php
resources/views/pruebas/index.blade.php
resources/views/pruebas/modal-pdfs.blade.php
routes/web.php  ← 7 rutas nuevas bajo /pruebas
storage/app/test-scenarios/   ← creado en runtime
storage/app/test-pdfs/        ← creado en runtime
```

No se modifican controladores existentes. No se tocan tablas HANA.

---

## Rutas

```
GET    /pruebas                    → vista principal (index)
POST   /pruebas/scenarios          → guardar escenario (crear o actualizar)
DELETE /pruebas/scenarios/{id}     → eliminar escenario
POST   /pruebas/pdfs               → subir PDF a biblioteca
DELETE /pruebas/pdfs/{id}          → eliminar PDF de biblioteca
POST   /pruebas/run                → inyectar trabajos vía API .NET
GET    /pruebas/jobs/status        → proxy polling de estados a .NET
```

Todas bajo middleware `auth` y rol `admin` (igual que el resto de rutas sensibles).

---

## Layout

Sigue el patrón `dbx-routing-layout` idéntico a Impresoras y Reglas:

```
dbx-routing-layout
├── dbx-routing-stores-card  (panel izquierdo, estrecho)
│   ├── "Escenarios"  [+ Nuevo]
│   ├── dbx-routing-store-link × N  (escenarios guardados)
│   └── [Gestionar PDFs (n)]        (abre modal)
│
└── dbx-routing-rules-card   (panel derecho, ancho)
    ├── ZONA A — Editor de escenario
    │   ├── Nombre del escenario [input text]
    │   ├── Tabla de lotes:
    │   │   columnas: Tienda | Tipo doc | Canal | Cantidad | PDF | [🗑]
    │   ├── [+ Añadir lote]
    │   └── [Guardar]  [▶ Lanzar]  [🗑 Eliminar escenario]
    │
    └── ZONA B — Resultados en vivo (oculta hasta lanzar)
        ├── tabla job × estado (badges de color)
        └── resumen: X OK / Y errores / impresoras usadas
```

La biblioteca de PDFs se gestiona en un **modal** (no en el sidebar) para no sobrecargar el panel izquierdo.

---

## Modelo de datos

### Escenario (`storage/app/test-scenarios/{uuid}.json`)

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "name": "Stress 3 tiendas",
  "createdAt": "2026-07-01T10:00:00Z",
  "batches": [
    {
      "storeId": 1,
      "documentType": "ALBARAN",
      "channel": "DEFAULT",
      "count": 5,
      "pdfId": "abc123"
    },
    {
      "storeId": 2,
      "documentType": "FACTURA",
      "channel": "DEFAULT",
      "count": 3,
      "pdfId": "def456"
    }
  ]
}
```

### Biblioteca de PDFs

**Índice** (`storage/app/test-pdfs/library.json`):
```json
[
  { "id": "abc123", "name": "albaran-real.pdf", "size": 48200, "uploadedAt": "2026-07-01T10:00:00Z" },
  { "id": "def456", "name": "factura-tipo-b.pdf", "size": 61000, "uploadedAt": "2026-07-01T11:00:00Z" }
]
```

**Archivos físicos:** `storage/app/test-pdfs/{id}.pdf`

---

## Tipos de documento

Valores predefinidos en los selectores del editor de lotes (lista fija + campo libre):

`ALBARAN` · `FACTURA` · `TRASPASO` · `CALLCENTER` · `AD360` · `DEVOLUCION`

El campo acepta texto libre para tipos que SAP pueda añadir en el futuro.

---

## Flujo de inyección y seguimiento en vivo

1. **Lanzar:** `POST /pruebas/run` con `{ scenarioId }`.
2. El controlador itera los lotes, por cada job llama a `POST /api/sourceprintjobs/test` con:
   ```json
   {
     "sourceSystem": "PRUEBA",
     "externalJobId": "PRUEBA-{SLUG}-{seq}-{timestamp}",
     "storeId": 1,
     "documentType": "ALBARAN",
     "channel": "DEFAULT",
     "pdfBlob": "<base64>"
   }
   ```
3. Devuelve `{ "injected": 8, "jobIds": ["PRUEBA-...", ...] }`.
4. JS arranca polling cada **2 s** a `GET /pruebas/jobs/status?ids[]=...&ids[]=...`.
5. El controlador proxea a `GET /api/printjobs` filtrando por `externalJobId` y devuelve `[{ externalJobId, status, printerId }]`.
6. JS actualiza la tabla fila a fila con badges (mismo sistema de colores que conectividad).
7. Polling para cuando todos los jobs están en estado terminal: `SpoolAccepted`, `ErrorFinal`, `PrintedConfirmed`, `PrintedUnknown`.
8. Se muestra resumen: jobs OK / errores / impresoras involucradas.

### Estados terminales vs. en curso

| Estado | Terminal | Badge |
|--------|----------|-------|
| Pending / Routed / Printing / RetryScheduled | No | warning (amarillo) |
| SpoolAccepted / PrintedConfirmed / PrintedUnknown | Sí | success (verde) |
| ErrorFinal | Sí | danger (rojo) |

---

## Gestión de PDFs (modal)

- Listado de PDFs con nombre y tamaño.
- Subida: `<input type="file" accept=".pdf">` → `POST /pruebas/pdfs` (multipart).
- Eliminación: `DELETE /pruebas/pdfs/{id}`. Antes de eliminar, verificar que ningún escenario guardado referencia ese `pdfId`; si lo hace, mostrar aviso y no eliminar.
- Sin límite de tamaño definido en spec; Laravel aplica el `upload_max_filesize` del servidor.

---

## Consideraciones de seguridad

- Rutas protegidas por `auth` + rol `admin`.
- PDFs almacenados fuera de `public/` (en `storage/app/`) → no accesibles por URL directa.
- El `externalJobId` generado incluye timestamp para evitar colisiones entre ejecuciones.
- 409 del API .NET (duplicado) se trata como advertencia, no como error.

---

## Fuera de alcance

- Historial de ejecuciones pasadas (no se persiste qué pasó en runs anteriores).
- Acceso a esta pestaña para roles no-admin.
- Exportar/importar escenarios entre servidores.
