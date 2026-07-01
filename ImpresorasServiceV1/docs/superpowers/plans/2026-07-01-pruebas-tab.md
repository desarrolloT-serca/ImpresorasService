# Pestaña Pruebas — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Añadir una pestaña "Pruebas" en la UI Laravel para configurar, guardar y lanzar tests de impresión con seguimiento en vivo, sin tocar HANA ni scripts externos.

**Architecture:** File-based storage en `storage/app/` (escenarios como JSON, PDFs en disco). `PruebasController` gestiona CRUD local y actúa de proxy hacia `POST /api/sourceprintjobs/test`. El seguimiento en vivo usa polling JS puro a una ruta Laravel que proxea `GET /api/printjobs`.

**Tech Stack:** Laravel 11, PHP 8.2, Blade, CSS dbx-* (glass-effect), JS vanilla, ApiClient (Guzzle), `Illuminate\Support\Facades\Storage`

## Global Constraints

- Sin frameworks JS externos (no Alpine, no Vue, no React). Solo JS vanilla.
- Seguir el patrón `dbx-routing-layout` / `dbx-routing-stores-card` / `dbx-routing-rules-card` exactamente como en `impresoras/index.blade.php` y `reglas/index.blade.php`.
- Usar `x-ui.card`, `x-ui.table`, `x-ui.action-buttons` donde aplique (componentes Blade existentes).
- Todas las rutas bajo middleware `admin.only` (mismo grupo que Reglas/Tiendas/Usuarios).
- PDFs guardados en `storage/app/test-pdfs/` (fuera de `public/`, no accesible por URL directa).
- Escenarios en `storage/app/test-scenarios/`. Cada escenario = un fichero `{uuid}.json`.
- `externalJobId` format: `PRUEBA-{SLUG}-{seq}-{timestamp}` (slug = nombre escenario, max 20 chars, solo alfanumérico+guion).
- Tipos de documento predefinidos (lista + campo libre): `ALBARAN`, `FACTURA`, `TRASPASO`, `CALLCENTER`, `AD360`, `DEVOLUCION`.
- Canal por defecto: `DEFAULT`.
- 409 del API .NET = duplicado → contar como OK, no como error.
- Estados terminales para detener polling: `SpoolAccepted`, `ErrorFinal`, `PrintedConfirmed`, `PrintedUnknown`.
- PHP 8.2: usar `readonly` properties, match expressions, named arguments donde aplique.

---

## File Map

| Acción | Fichero |
|--------|---------|
| **Crear** | `app/Http/Controllers/PruebasController.php` |
| **Crear** | `resources/views/pruebas/index.blade.php` |
| **Modificar** | `routes/web.php` — añadir grupo de rutas `/pruebas` |
| **Modificar** | `resources/views/layouts/app.blade.php` — añadir enlace nav "Pruebas" |

---

## Task 1: Rutas y controlador esqueleto

**Files:**
- Create: `src/ImpresorasService.Web.PHP/app/Http/Controllers/PruebasController.php`
- Modify: `src/ImpresorasService.Web.PHP/routes/web.php`

**Interfaces:**
- Produce: clase `PruebasController` con métodos `index`, `saveScenario`, `deleteScenario`, `uploadPdf`, `deletePdf`, `run`, `jobsStatus`
- Produce: rutas nombradas `pruebas.index`, `pruebas.scenarios.save`, `pruebas.scenarios.delete`, `pruebas.pdfs.upload`, `pruebas.pdfs.delete`, `pruebas.run`, `pruebas.jobs.status`

- [ ] **Step 1: Añadir rutas en `web.php`**

Abrir `src/ImpresorasService.Web.PHP/routes/web.php`. Al final del bloque `admin.only` existente (después de la última ruta de alertas), añadir:

```php
use App\Http\Controllers\PruebasController;
```

(junto a los otros `use` al principio del fichero)

Y dentro del grupo `admin.only`, añadir al final:

```php
Route::get('/pruebas', [PruebasController::class, 'index'])->name('pruebas.index');
Route::post('/pruebas/scenarios', [PruebasController::class, 'saveScenario'])->name('pruebas.scenarios.save');
Route::delete('/pruebas/scenarios/{id}', [PruebasController::class, 'deleteScenario'])->name('pruebas.scenarios.delete');
Route::post('/pruebas/pdfs', [PruebasController::class, 'uploadPdf'])->name('pruebas.pdfs.upload');
Route::delete('/pruebas/pdfs/{id}', [PruebasController::class, 'deletePdf'])->name('pruebas.pdfs.delete');
Route::post('/pruebas/run', [PruebasController::class, 'run'])->name('pruebas.run');
Route::get('/pruebas/jobs/status', [PruebasController::class, 'jobsStatus'])->name('pruebas.jobs.status');
```

- [ ] **Step 2: Crear `PruebasController.php`**

```php
<?php

namespace App\Http\Controllers;

use App\Services\ApiClient;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Storage;
use Illuminate\Support\Str;
use Illuminate\View\View;

class PruebasController extends Controller
{
    private const SCENARIOS_DISK = 'local';
    private const SCENARIOS_DIR  = 'test-scenarios';
    private const PDFS_DIR       = 'test-pdfs';
    private const PDF_INDEX      = 'test-pdfs/library.json';

    private const DOC_TYPES = [
        'ALBARAN', 'FACTURA', 'TRASPASO', 'CALLCENTER', 'AD360', 'DEVOLUCION',
    ];

    private const TERMINAL_STATUSES = [
        'SpoolAccepted', 'ErrorFinal', 'PrintedConfirmed', 'PrintedUnknown',
    ];

    public function __construct(private readonly ApiClient $api) {}

    // ── Helpers ───────────────────────────────────────────────────────────────

    private function loadScenarios(): array
    {
        $files = Storage::disk(self::SCENARIOS_DISK)->files(self::SCENARIOS_DIR);
        $scenarios = [];
        foreach ($files as $file) {
            if (!str_ends_with($file, '.json')) continue;
            $data = json_decode(Storage::disk(self::SCENARIOS_DISK)->get($file), true);
            if (is_array($data) && isset($data['id'])) {
                $scenarios[] = $data;
            }
        }
        usort($scenarios, fn($a, $b) => strcmp($a['createdAt'] ?? '', $b['createdAt'] ?? ''));
        return $scenarios;
    }

    private function loadPdfLibrary(): array
    {
        if (!Storage::disk(self::SCENARIOS_DISK)->exists(self::PDF_INDEX)) {
            return [];
        }
        return json_decode(Storage::disk(self::SCENARIOS_DISK)->get(self::PDF_INDEX), true) ?? [];
    }

    private function savePdfLibrary(array $library): void
    {
        Storage::disk(self::SCENARIOS_DISK)->put(self::PDF_INDEX, json_encode($library, JSON_PRETTY_PRINT));
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    public function index(): View
    {
        $scenarios = $this->loadScenarios();
        $pdfs = $this->loadPdfLibrary();
        $stores = $this->api->get('api/stores?isActive=true');

        return view('pruebas.index', [
            'scenarios' => $scenarios,
            'pdfs'      => $pdfs,
            'stores'    => is_array($stores) ? $stores : [],
            'docTypes'  => self::DOC_TYPES,
        ]);
    }

    public function saveScenario(Request $request): JsonResponse
    {
        $request->validate([
            'name'           => 'required|string|max:100',
            'batches'        => 'required|array|min:1',
            'batches.*.storeId'      => 'required|integer|min:1',
            'batches.*.documentType' => 'required|string|max:50',
            'batches.*.channel'      => 'nullable|string|max:50',
            'batches.*.count'        => 'required|integer|min:1|max:50',
            'batches.*.pdfId'        => 'nullable|string|max:36',
        ]);

        $id = $request->input('id') ?: (string) Str::uuid();
        $scenario = [
            'id'        => $id,
            'name'      => $request->input('name'),
            'createdAt' => $request->input('createdAt') ?: now()->toIso8601String(),
            'batches'   => array_map(fn($b) => [
                'storeId'      => (int) $b['storeId'],
                'documentType' => strtoupper(trim($b['documentType'])),
                'channel'      => strtoupper(trim($b['channel'] ?? 'DEFAULT')) ?: 'DEFAULT',
                'count'        => (int) $b['count'],
                'pdfId'        => $b['pdfId'] ?? null,
            ], $request->input('batches')),
        ];

        Storage::disk(self::SCENARIOS_DISK)->put(
            self::SCENARIOS_DIR . '/' . $id . '.json',
            json_encode($scenario, JSON_PRETTY_PRINT)
        );

        return response()->json($scenario);
    }

    public function deleteScenario(string $id): JsonResponse
    {
        $path = self::SCENARIOS_DIR . '/' . $id . '.json';
        if (Storage::disk(self::SCENARIOS_DISK)->exists($path)) {
            Storage::disk(self::SCENARIOS_DISK)->delete($path);
        }
        return response()->json(['ok' => true]);
    }

    public function uploadPdf(Request $request): JsonResponse
    {
        $request->validate([
            'pdf' => 'required|file|mimes:pdf|max:20480',
        ]);

        $file   = $request->file('pdf');
        $id     = (string) Str::uuid();
        $stored = $file->storeAs(self::PDFS_DIR, $id . '.pdf', self::SCENARIOS_DISK);

        if (!$stored) {
            return response()->json(['error' => 'No se pudo guardar el PDF.'], 500);
        }

        $library   = $this->loadPdfLibrary();
        $library[] = [
            'id'         => $id,
            'name'       => $file->getClientOriginalName(),
            'size'       => $file->getSize(),
            'uploadedAt' => now()->toIso8601String(),
        ];
        $this->savePdfLibrary($library);

        return response()->json(end($library));
    }

    public function deletePdf(string $id): JsonResponse
    {
        // Verificar que no lo usa ningún escenario
        foreach ($this->loadScenarios() as $scenario) {
            foreach ($scenario['batches'] ?? [] as $batch) {
                if (($batch['pdfId'] ?? null) === $id) {
                    return response()->json([
                        'error' => 'Este PDF está en uso por el escenario "' . ($scenario['name'] ?? $id) . '".',
                    ], 409);
                }
            }
        }

        $path = self::PDFS_DIR . '/' . $id . '.pdf';
        if (Storage::disk(self::SCENARIOS_DISK)->exists($path)) {
            Storage::disk(self::SCENARIOS_DISK)->delete($path);
        }

        $library = array_values(array_filter($this->loadPdfLibrary(), fn($p) => $p['id'] !== $id));
        $this->savePdfLibrary($library);

        return response()->json(['ok' => true]);
    }

    public function run(Request $request): JsonResponse
    {
        $request->validate(['scenarioId' => 'required|string']);

        $path = self::SCENARIOS_DIR . '/' . $request->input('scenarioId') . '.json';
        if (!Storage::disk(self::SCENARIOS_DISK)->exists($path)) {
            return response()->json(['error' => 'Escenario no encontrado.'], 404);
        }

        $scenario  = json_decode(Storage::disk(self::SCENARIOS_DISK)->get($path), true);
        $ts        = now()->format('YmdHis');
        $slug      = strtoupper(preg_replace('/[^A-Za-z0-9]/', '', $scenario['name'] ?? 'PRUEBA'));
        $slug      = substr($slug, 0, 20);
        $jobIds    = [];
        $injected  = 0;
        $errors    = [];

        foreach ($scenario['batches'] ?? [] as $batch) {
            $pdfBase64 = null;
            $pdfId     = $batch['pdfId'] ?? null;
            if ($pdfId) {
                $pdfPath = self::PDFS_DIR . '/' . $pdfId . '.pdf';
                if (Storage::disk(self::SCENARIOS_DISK)->exists($pdfPath)) {
                    $pdfBase64 = base64_encode(Storage::disk(self::SCENARIOS_DISK)->get($pdfPath));
                }
            }

            $count = max(1, (int) ($batch['count'] ?? 1));
            for ($i = 1; $i <= $count; $i++) {
                $externalJobId = sprintf('PRUEBA-%s-%03d-%s', $slug, $i, $ts);
                try {
                    $this->api->post('api/sourceprintjobs/test', [
                        'sourceSystem'  => 'PRUEBA',
                        'externalJobId' => $externalJobId,
                        'storeId'       => (int) $batch['storeId'],
                        'documentType'  => $batch['documentType'],
                        'channel'       => $batch['channel'] ?? 'DEFAULT',
                        'pdfBlob'       => $pdfBase64,
                    ]);
                    $jobIds[] = $externalJobId;
                    $injected++;
                } catch (\GuzzleHttp\Exception\RequestException $e) {
                    $status = $e->getResponse()?->getStatusCode();
                    if ($status === 409) {
                        // Duplicado: contar como OK
                        $jobIds[] = $externalJobId;
                        $injected++;
                    } else {
                        $errors[] = $externalJobId . ': HTTP ' . ($status ?? 'unknown');
                    }
                }
            }
        }

        return response()->json([
            'injected' => $injected,
            'jobIds'   => $jobIds,
            'errors'   => $errors,
        ]);
    }

    public function jobsStatus(Request $request): JsonResponse
    {
        $ids = $request->input('ids', []);
        if (empty($ids) || !is_array($ids)) {
            return response()->json([]);
        }

        // Llamar a la API con filtro por externalJobId (query string multi-value)
        $query  = implode('&', array_map(fn($id) => 'externalJobId[]=' . urlencode($id), $ids));
        $result = $this->api->get('api/printjobs?' . $query);

        // Normalizar a [{externalJobId, status, printerId}]
        $mapped = array_map(fn($j) => [
            'externalJobId' => $j['externalJobId'] ?? $j['ExternalJobId'] ?? '',
            'status'        => $j['status'] ?? $j['Status'] ?? 'Unknown',
            'printerId'     => $j['printerId'] ?? $j['PrinterId'] ?? null,
        ], is_array($result) ? $result : []);

        return response()->json($mapped);
    }
}
```

- [ ] **Step 3: Verificar que las rutas cargan (test manual)**

Arrancar Laravel: `php artisan serve`  
Navegar a `http://localhost:8000/pruebas` con sesión de admin.  
Esperado: vista en blanco (aún no existe) o error 404 de vista — NO error de ruta.

- [ ] **Step 4: Commit**

```bash
git add src/ImpresorasService.Web.PHP/app/Http/Controllers/PruebasController.php \
        src/ImpresorasService.Web.PHP/routes/web.php
git commit -m "feat(pruebas): controlador y rutas base"
```

---

## Task 2: Vista principal — panel izquierdo (lista de escenarios)

**Files:**
- Create: `src/ImpresorasService.Web.PHP/resources/views/pruebas/index.blade.php`

**Interfaces:**
- Consume: variables de vista `$scenarios` (array), `$pdfs` (array), `$stores` (array), `$docTypes` (array de strings)
- Produce: vista `/pruebas` con `dbx-routing-layout`, panel izquierdo con lista de escenarios y botón "Gestionar PDFs"

- [ ] **Step 1: Crear la vista con el shell completo y el panel izquierdo**

Crear `src/ImpresorasService.Web.PHP/resources/views/pruebas/index.blade.php`:

```blade
@extends('layouts.app')

@section('title', 'Pruebas')

@section('content')
@php
    $scenarios  = is_array($scenarios ?? null) ? $scenarios : [];
    $pdfs       = is_array($pdfs ?? null) ? $pdfs : [];
    $stores     = is_array($stores ?? null) ? $stores : [];
    $docTypes   = is_array($docTypes ?? null) ? $docTypes : [];
    $activeId   = request('scenario');
    $active     = collect($scenarios)->firstWhere('id', $activeId);
@endphp

<div class="dbx-wrap">
<section class="dbx-routing-layout">

    {{-- ── Panel izquierdo: lista de escenarios ── --}}
    <x-ui.card class="dbx-routing-stores-card">
        <div class="dbx-title-row">
            <h2 class="dbx-title">Escenarios</h2>
            <button type="button" class="btn btn-primary btn-sm" id="btn-nuevo-escenario">+ Nuevo</button>
        </div>

        @if(count($scenarios) === 0)
            <p class="dbx-subtle">Sin escenarios. Crea uno con el botón.</p>
        @else
            <div class="dbx-routing-store-list">
                @foreach($scenarios as $sc)
                    <a href="{{ route('pruebas.index', ['scenario' => $sc['id']]) }}"
                       class="dbx-routing-store-link {{ ($activeId === $sc['id']) ? 'is-active' : '' }}"
                       data-scenario-id="{{ $sc['id'] }}">
                        <span class="dbx-routing-store-name">{{ $sc['name'] }}</span>
                        <span class="dbx-routing-store-meta">{{ count($sc['batches'] ?? []) }} lote(s)</span>
                    </a>
                @endforeach
            </div>
        @endif

        <div style="margin-top: auto; padding-top: 1rem; border-top: 1px solid var(--ui-border, #e2e8f0);">
            <button type="button" class="btn btn-ghost btn-sm" id="btn-gestionar-pdfs">
                Gestionar PDFs ({{ count($pdfs) }})
            </button>
        </div>
    </x-ui.card>

    {{-- ── Panel derecho ── --}}
    <x-ui.card class="dbx-routing-rules-card" id="pruebas-panel-derecho">

        {{-- ZONA A: Editor de escenario --}}
        <div id="pruebas-editor">
            @if($active)
                @include('pruebas._editor', ['scenario' => $active, 'stores' => $stores, 'docTypes' => $docTypes, 'pdfs' => $pdfs])
            @else
                <div class="dbx-empty-state">
                    Selecciona un escenario o crea uno nuevo.
                </div>
            @endif
        </div>

        {{-- ZONA B: Resultados en vivo (oculta hasta lanzar) --}}
        <div id="pruebas-resultados" style="display:none; margin-top: 1.5rem;">
            <div class="dbx-title-row" style="margin-bottom: 0.75rem;">
                <h3 class="dbx-title" style="font-size: 1rem;">Resultados en vivo</h3>
                <span id="pruebas-resultado-resumen" class="dbx-subtle"></span>
            </div>
            <x-ui.table>
                <thead>
                    <tr>
                        <th>Job ID</th>
                        <th>Estado</th>
                        <th>Impresora</th>
                    </tr>
                </thead>
                <tbody id="pruebas-jobs-tbody"></tbody>
            </x-ui.table>
        </div>

    </x-ui.card>

</section>
</div>

{{-- ── Modal Gestionar PDFs ── --}}
@include('pruebas._modal-pdfs', ['pdfs' => $pdfs])

@endsection

@section('page_scripts')
<script>
@include('pruebas._script')
</script>
@endsection
```

- [ ] **Step 2: Verificar que la vista carga sin errores**

`php artisan serve` → navegar a `/pruebas`.  
Esperado: la vista carga (parciales aún no existen → error de include, está bien, lo veremos en Task 3).

- [ ] **Step 3: Commit**

```bash
git add src/ImpresorasService.Web.PHP/resources/views/pruebas/index.blade.php
git commit -m "feat(pruebas): vista principal shell"
```

---

## Task 3: Parciales Blade — editor, modal PDFs, script JS

**Files:**
- Create: `src/ImpresorasService.Web.PHP/resources/views/pruebas/_editor.blade.php`
- Create: `src/ImpresorasService.Web.PHP/resources/views/pruebas/_modal-pdfs.blade.php`
- Create: `src/ImpresorasService.Web.PHP/resources/views/pruebas/_script.blade.php`

**Interfaces:**
- Consume (`_editor`): `$scenario` (array con `id`, `name`, `batches`), `$stores`, `$docTypes`, `$pdfs`
- Consume (`_modal-pdfs`): `$pdfs` (array)
- Consume (`_script`): variables de Blade embebidas via `@json`
- Produce: formulario de edición de lotes, modal de PDFs, lógica JS de guardado/lanzado/polling

- [ ] **Step 1: Crear `_editor.blade.php`**

```blade
@php
    $scId      = $scenario['id'] ?? '';
    $scName    = $scenario['name'] ?? '';
    $scBatches = $scenario['batches'] ?? [];
    $pdfsById  = collect($pdfs ?? [])->keyBy('id')->toArray();
@endphp

<div class="dbx-title-row dbx-routing-title-row">
    <div>
        <h2 class="dbx-title">{{ $scName ?: 'Nuevo escenario' }}</h2>
        <span class="dbx-subtle">Edita los lotes y lanza la prueba</span>
    </div>
    <div class="dbx-printer-panel-tools" style="gap: 0.5rem;">
        <button type="button" class="btn btn-ghost" id="btn-guardar-escenario">Guardar</button>
        <button type="button" class="btn btn-primary" id="btn-lanzar-escenario">&#9654; Lanzar</button>
        @if($scId)
        <button type="button" class="btn btn-danger" id="btn-eliminar-escenario" data-id="{{ $scId }}">Eliminar</button>
        @endif
    </div>
</div>

<form id="form-escenario" data-id="{{ $scId }}">
    <div style="margin-bottom: 1rem;">
        <label class="dbx-label" for="sc-nombre">Nombre del escenario</label>
        <input type="text" id="sc-nombre" class="input" name="name" value="{{ $scName }}"
               placeholder="Ej. Stress 3 tiendas" required maxlength="100" style="max-width: 400px;">
    </div>

    <div style="margin-bottom: 0.75rem;">
        <strong style="font-size: 0.875rem;">Lotes</strong>
    </div>

    <x-ui.table id="tabla-lotes">
        <thead>
            <tr>
                <th>Tienda</th>
                <th>Tipo doc.</th>
                <th>Canal</th>
                <th>Cantidad</th>
                <th>PDF</th>
                <th></th>
            </tr>
        </thead>
        <tbody id="lotes-tbody">
            @foreach($scBatches as $i => $batch)
                @php $pdfName = $pdfsById[$batch['pdfId'] ?? '']['name'] ?? '(sin PDF)'; @endphp
                <tr data-lote-idx="{{ $i }}">
                    <td>
                        <select name="batches[{{ $i }}][storeId]" class="select select-sm" required>
                            <option value="">Tienda…</option>
                            @foreach($stores as $store)
                                <option value="{{ $store['storeId'] ?? $store['StoreId'] }}"
                                    {{ (string)($batch['storeId'] ?? '') === (string)($store['storeId'] ?? $store['StoreId'] ?? '') ? 'selected' : '' }}>
                                    {{ \App\Helpers\StoreFormat::label($store['storeId'] ?? $store['StoreId'], $store['name'] ?? $store['Name'] ?? '') }}
                                </option>
                            @endforeach
                        </select>
                    </td>
                    <td>
                        <input type="text" name="batches[{{ $i }}][documentType]" class="input input-sm"
                               value="{{ $batch['documentType'] ?? '' }}" list="doc-types-list" placeholder="ALBARAN" required maxlength="50">
                    </td>
                    <td>
                        <input type="text" name="batches[{{ $i }}][channel]" class="input input-sm"
                               value="{{ $batch['channel'] ?? 'DEFAULT' }}" maxlength="50" style="width: 90px;">
                    </td>
                    <td>
                        <input type="number" name="batches[{{ $i }}][count]" class="input input-sm"
                               value="{{ $batch['count'] ?? 1 }}" min="1" max="50" required style="width: 70px;">
                    </td>
                    <td>
                        <button type="button" class="btn btn-ghost btn-sm btn-elegir-pdf"
                                data-pdf-id="{{ $batch['pdfId'] ?? '' }}">
                            {{ Str::limit($pdfName, 22) }}
                        </button>
                        <input type="hidden" name="batches[{{ $i }}][pdfId]" value="{{ $batch['pdfId'] ?? '' }}">
                    </td>
                    <td>
                        <button type="button" class="btn btn-danger btn-sm btn-eliminar-lote">&#10005;</button>
                    </td>
                </tr>
            @endforeach
        </tbody>
    </x-ui.table>

    <button type="button" class="btn btn-ghost btn-sm" id="btn-add-lote" style="margin-top: 0.5rem;">
        + Añadir lote
    </button>
</form>

<datalist id="doc-types-list">
    @foreach($docTypes as $dt)
        <option value="{{ $dt }}">
    @endforeach
</datalist>
```

- [ ] **Step 2: Crear `_modal-pdfs.blade.php`**

```blade
<div id="modal-pdfs" style="display:none; position:fixed; inset:0; z-index:1000;
     background:rgba(0,0,0,.45); align-items:center; justify-content:center;">
    <div class="dbx-card" style="width: min(560px, 94vw); max-height: 80vh;
         overflow-y:auto; padding: 1.5rem; position:relative;">
        <div class="dbx-title-row" style="margin-bottom: 1rem;">
            <h2 class="dbx-title" style="font-size: 1.05rem;">Biblioteca de PDFs</h2>
            <button type="button" id="btn-cerrar-modal-pdfs" class="btn btn-ghost btn-sm">&#10005;</button>
        </div>

        <div style="margin-bottom: 1rem;">
            <label class="btn btn-primary btn-sm" for="input-subir-pdf" style="cursor:pointer;">
                Subir PDF
            </label>
            <input type="file" id="input-subir-pdf" accept=".pdf" style="display:none;">
            <span id="pdf-upload-status" class="dbx-subtle" style="margin-left:.5rem;"></span>
        </div>

        <x-ui.table id="tabla-pdfs">
            <thead>
                <tr>
                    <th>Nombre</th>
                    <th>Tamaño</th>
                    <th></th>
                </tr>
            </thead>
            <tbody id="pdfs-tbody">
                @foreach($pdfs as $pdf)
                    <tr data-pdf-id="{{ $pdf['id'] }}">
                        <td>{{ $pdf['name'] }}</td>
                        <td>{{ number_format(($pdf['size'] ?? 0) / 1024, 1) }} KB</td>
                        <td>
                            <button type="button" class="btn btn-ghost btn-sm btn-seleccionar-pdf"
                                    data-pdf-id="{{ $pdf['id'] }}" data-pdf-name="{{ $pdf['name'] }}">
                                Seleccionar
                            </button>
                            <button type="button" class="btn btn-danger btn-sm btn-eliminar-pdf"
                                    data-pdf-id="{{ $pdf['id'] }}">
                                Eliminar
                            </button>
                        </td>
                    </tr>
                @endforeach
            </tbody>
        </x-ui.table>

        @if(count($pdfs) === 0)
            <p class="dbx-subtle" id="pdfs-empty-msg">No hay PDFs subidos.</p>
        @endif
    </div>
</div>
```

- [ ] **Step 3: Crear `_script.blade.php`**

```blade
(function () {
    const csrf       = document.querySelector('meta[name="csrf-token"]')?.content || '';
    const TERMINAL   = new Set(['SpoolAccepted', 'ErrorFinal', 'PrintedConfirmed', 'PrintedUnknown']);
    const STATUS_CSS = {
        SpoolAccepted: 'badge-success', PrintedConfirmed: 'badge-success',
        PrintedUnknown: 'badge-success', ErrorFinal: 'badge-danger',
        Pending: 'badge-warning', Routed: 'badge-warning',
        Printing: 'badge-warning', RetryScheduled: 'badge-warning',
        default: 'badge-neutral',
    };

    let activePdfTargetBtn = null;  // botón de lote que disparó abrir modal
    let pollTimer = null;

    // ── Helpers fetch ────────────────────────────────────────────────────────

    async function apiFetch(url, opts = {}) {
        const res = await fetch(url, {
            headers: { 'X-CSRF-TOKEN': csrf, 'Accept': 'application/json', ...(opts.headers || {}) },
            ...opts,
        });
        return { ok: res.ok, status: res.status, data: await res.json().catch(() => ({})) };
    }

    // ── Nuevo escenario ──────────────────────────────────────────────────────

    document.getElementById('btn-nuevo-escenario')?.addEventListener('click', () => {
        window.location.href = '{{ route('pruebas.index') }}';
    });

    // ── Añadir lote ──────────────────────────────────────────────────────────

    document.getElementById('btn-add-lote')?.addEventListener('click', addLote);

    function addLote() {
        const tbody = document.getElementById('lotes-tbody');
        if (!tbody) return;
        const idx = tbody.rows.length;
        const storeOptions = @json(collect($stores)->map(fn($s) => [
            'id'   => $s['storeId'] ?? $s['StoreId'] ?? 0,
            'name' => \App\Helpers\StoreFormat::label($s['storeId'] ?? $s['StoreId'] ?? 0, $s['name'] ?? $s['Name'] ?? ''),
        ])->values()->toArray());

        const storeSelect = '<select name="batches['+idx+'][storeId]" class="select select-sm" required>'
            + '<option value="">Tienda…</option>'
            + storeOptions.map(s => '<option value="'+s.id+'">'+s.name+'</option>').join('')
            + '</select>';

        const row = document.createElement('tr');
        row.dataset.loteIdx = idx;
        row.innerHTML = `
            <td>${storeSelect}</td>
            <td><input type="text" name="batches[${idx}][documentType]" class="input input-sm"
                       list="doc-types-list" placeholder="ALBARAN" required maxlength="50"></td>
            <td><input type="text" name="batches[${idx}][channel]" class="input input-sm"
                       value="DEFAULT" maxlength="50" style="width:90px;"></td>
            <td><input type="number" name="batches[${idx}][count]" class="input input-sm"
                       value="1" min="1" max="50" required style="width:70px;"></td>
            <td>
                <button type="button" class="btn btn-ghost btn-sm btn-elegir-pdf" data-pdf-id="">
                    (sin PDF)
                </button>
                <input type="hidden" name="batches[${idx}][pdfId]" value="">
            </td>
            <td><button type="button" class="btn btn-danger btn-sm btn-eliminar-lote">&#10005;</button></td>
        `;
        tbody.appendChild(row);
    }

    // ── Eliminar lote ────────────────────────────────────────────────────────

    document.getElementById('lotes-tbody')?.addEventListener('click', e => {
        if (e.target.closest('.btn-eliminar-lote')) {
            e.target.closest('tr').remove();
            reindexLotes();
        }
    });

    function reindexLotes() {
        document.querySelectorAll('#lotes-tbody tr').forEach((row, i) => {
            row.dataset.loteIdx = i;
            row.querySelectorAll('[name]').forEach(el => {
                el.name = el.name.replace(/batches\[\d+\]/, `batches[${i}]`);
            });
        });
    }

    // ── Guardar escenario ────────────────────────────────────────────────────

    document.getElementById('btn-guardar-escenario')?.addEventListener('click', async () => {
        const form   = document.getElementById('form-escenario');
        if (!form) return;
        const id     = form.dataset.id || '';
        const name   = document.getElementById('sc-nombre')?.value?.trim();
        if (!name) { window.showToast?.('Introduce un nombre para el escenario.', 'error'); return; }

        const batches = buildBatches();
        if (batches.length === 0) { window.showToast?.('Añade al menos un lote.', 'error'); return; }

        const payload = { id: id || undefined, name, batches };
        const { ok, data } = await apiFetch('{{ route('pruebas.scenarios.save') }}', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
        });

        if (ok) {
            window.showToast?.('Escenario guardado.', 'success');
            setTimeout(() => window.location.href = '{{ route('pruebas.index') }}?scenario=' + data.id, 600);
        } else {
            window.showToast?.(data?.message || 'Error al guardar.', 'error');
        }
    });

    function buildBatches() {
        const rows = document.querySelectorAll('#lotes-tbody tr');
        return Array.from(rows).map(row => {
            const get = name => row.querySelector(`[name$="[${name}]"]`)?.value?.trim() || '';
            return {
                storeId:      parseInt(get('storeId'), 10),
                documentType: get('documentType').toUpperCase(),
                channel:      get('channel').toUpperCase() || 'DEFAULT',
                count:        parseInt(get('count'), 10) || 1,
                pdfId:        get('pdfId') || null,
            };
        }).filter(b => b.storeId > 0 && b.documentType);
    }

    // ── Eliminar escenario ───────────────────────────────────────────────────

    document.getElementById('btn-eliminar-escenario')?.addEventListener('click', async e => {
        const id = e.currentTarget.dataset.id;
        if (!confirm('¿Eliminar este escenario?')) return;
        const url = '{{ route('pruebas.scenarios.delete', '__ID__') }}'.replace('__ID__', id);
        const { ok } = await apiFetch(url, { method: 'DELETE' });
        if (ok) window.location.href = '{{ route('pruebas.index') }}';
        else window.showToast?.('Error al eliminar.', 'error');
    });

    // ── Lanzar escenario ─────────────────────────────────────────────────────

    document.getElementById('btn-lanzar-escenario')?.addEventListener('click', async () => {
        const scenarioId = document.getElementById('form-escenario')?.dataset.id;
        if (!scenarioId) { window.showToast?.('Guarda el escenario antes de lanzar.', 'error'); return; }

        document.getElementById('pruebas-resultados').style.display = 'block';
        document.getElementById('pruebas-jobs-tbody').innerHTML =
            '<tr><td colspan="3" class="dbx-subtle">Inyectando trabajos…</td></tr>';
        document.getElementById('pruebas-resultado-resumen').textContent = '';

        const { ok, data } = await apiFetch('{{ route('pruebas.run') }}', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ scenarioId }),
        });

        if (!ok) {
            window.showToast?.(data?.error || 'Error al lanzar.', 'error');
            return;
        }

        const jobIds = data.jobIds || [];
        if (jobIds.length === 0) {
            document.getElementById('pruebas-jobs-tbody').innerHTML =
                '<tr><td colspan="3" class="dbx-subtle">Sin trabajos inyectados.</td></tr>';
            return;
        }

        // Inicializar tabla
        const tbody = document.getElementById('pruebas-jobs-tbody');
        tbody.innerHTML = '';
        jobIds.forEach(id => {
            const tr = document.createElement('tr');
            tr.dataset.jobId = id;
            tr.innerHTML = `<td class="text-col">${id}</td>
                <td><span class="badge badge-neutral">Pendiente</span></td>
                <td>-</td>`;
            tbody.appendChild(tr);
        });

        if (data.errors?.length) {
            window.showToast?.(`${data.errors.length} errores al inyectar.`, 'warning');
        }

        startPolling(jobIds);
    });

    // ── Polling de estado ────────────────────────────────────────────────────

    function startPolling(jobIds) {
        if (pollTimer) clearInterval(pollTimer);
        let resolved = 0;

        async function tick() {
            const qs = jobIds.map(id => 'ids[]=' + encodeURIComponent(id)).join('&');
            const { ok, data } = await apiFetch('{{ route('pruebas.jobs.status') }}?' + qs);
            if (!ok) return;

            const byId = Object.fromEntries(data.map(j => [j.externalJobId, j]));
            resolved = 0;

            jobIds.forEach(id => {
                const row = document.querySelector(`#pruebas-jobs-tbody tr[data-job-id="${id}"]`);
                if (!row) return;
                const job    = byId[id];
                const status = job?.status || 'Unknown';
                const css    = STATUS_CSS[status] || STATUS_CSS.default;
                row.cells[1].innerHTML = `<span class="badge ${css}">${status}</span>`;
                row.cells[2].textContent = job?.printerId ? `#${job.printerId}` : '-';
                if (TERMINAL.has(status)) resolved++;
            });

            if (resolved === jobIds.length) {
                clearInterval(pollTimer);
                pollTimer = null;
                const ok2 = jobIds.filter(id => {
                    const j = byId[id]; const s = j?.status || '';
                    return s === 'SpoolAccepted' || s === 'PrintedConfirmed' || s === 'PrintedUnknown';
                }).length;
                const err = jobIds.length - ok2;
                document.getElementById('pruebas-resultado-resumen').textContent =
                    `✓ ${ok2} OK  ✗ ${err} errores`;
            }
        }

        tick();
        pollTimer = setInterval(tick, 2000);
    }

    // ── Modal PDFs ───────────────────────────────────────────────────────────

    document.getElementById('btn-gestionar-pdfs')?.addEventListener('click', () => {
        document.getElementById('modal-pdfs').style.display = 'flex';
    });

    document.getElementById('btn-cerrar-modal-pdfs')?.addEventListener('click', () => {
        document.getElementById('modal-pdfs').style.display = 'none';
        activePdfTargetBtn = null;
    });

    // Clic en fondo del modal
    document.getElementById('modal-pdfs')?.addEventListener('click', e => {
        if (e.target === document.getElementById('modal-pdfs')) {
            document.getElementById('modal-pdfs').style.display = 'none';
            activePdfTargetBtn = null;
        }
    });

    // Abrir modal desde botón de lote (elegir PDF)
    document.getElementById('lotes-tbody')?.addEventListener('click', e => {
        const btn = e.target.closest('.btn-elegir-pdf');
        if (!btn) return;
        activePdfTargetBtn = btn;
        document.getElementById('modal-pdfs').style.display = 'flex';
    });

    // Seleccionar PDF desde modal
    document.getElementById('pdfs-tbody')?.addEventListener('click', e => {
        const btn = e.target.closest('.btn-seleccionar-pdf');
        if (!btn || !activePdfTargetBtn) return;
        const id   = btn.dataset.pdfId;
        const name = btn.dataset.pdfName;
        activePdfTargetBtn.textContent = name.length > 22 ? name.slice(0, 22) + '…' : name;
        activePdfTargetBtn.dataset.pdfId = id;
        const hidden = activePdfTargetBtn.closest('td')?.querySelector('input[type="hidden"]');
        if (hidden) hidden.value = id;
        document.getElementById('modal-pdfs').style.display = 'none';
        activePdfTargetBtn = null;
    });

    // Subir PDF
    document.getElementById('input-subir-pdf')?.addEventListener('change', async e => {
        const file = e.target.files?.[0];
        if (!file) return;
        const status = document.getElementById('pdf-upload-status');
        status.textContent = 'Subiendo…';
        const fd = new FormData();
        fd.append('pdf', file);
        fd.append('_token', csrf);
        const { ok, data } = await apiFetch('{{ route('pruebas.pdfs.upload') }}', {
            method: 'POST',
            headers: {},  // sin Content-Type para que el browser ponga multipart
            body: fd,
        });
        if (ok) {
            status.textContent = '';
            addPdfRow(data);
            document.getElementById('pdfs-empty-msg') && (document.getElementById('pdfs-empty-msg').style.display = 'none');
            updatePdfCount(1);
            window.showToast?.('PDF subido.', 'success');
        } else {
            status.textContent = 'Error: ' + (data?.message || 'Fallo al subir');
            window.showToast?.('Error al subir el PDF.', 'error');
        }
        e.target.value = '';
    });

    function addPdfRow(pdf) {
        const tbody = document.getElementById('pdfs-tbody');
        if (!tbody) return;
        const tr = document.createElement('tr');
        tr.dataset.pdfId = pdf.id;
        const sizeKb = ((pdf.size || 0) / 1024).toFixed(1);
        const shortName = pdf.name.length > 22 ? pdf.name.slice(0, 22) + '…' : pdf.name;
        tr.innerHTML = `
            <td>${pdf.name}</td>
            <td>${sizeKb} KB</td>
            <td>
                <button type="button" class="btn btn-ghost btn-sm btn-seleccionar-pdf"
                        data-pdf-id="${pdf.id}" data-pdf-name="${pdf.name}">Seleccionar</button>
                <button type="button" class="btn btn-danger btn-sm btn-eliminar-pdf"
                        data-pdf-id="${pdf.id}">Eliminar</button>
            </td>`;
        tbody.appendChild(tr);
    }

    function updatePdfCount(delta) {
        const btn = document.getElementById('btn-gestionar-pdfs');
        if (!btn) return;
        const m = btn.textContent.match(/\((\d+)\)/);
        const n = m ? parseInt(m[1], 10) + delta : delta;
        btn.textContent = `Gestionar PDFs (${n})`;
    }

    // Eliminar PDF desde modal
    document.getElementById('pdfs-tbody')?.addEventListener('click', async e => {
        const btn = e.target.closest('.btn-eliminar-pdf');
        if (!btn) return;
        const id  = btn.dataset.pdfId;
        const url = '{{ route('pruebas.pdfs.delete', '__ID__') }}'.replace('__ID__', id);
        const { ok, data } = await apiFetch(url, { method: 'DELETE' });
        if (ok) {
            btn.closest('tr').remove();
            updatePdfCount(-1);
            window.showToast?.('PDF eliminado.', 'success');
        } else {
            window.showToast?.(data?.error || 'No se pudo eliminar.', 'error');
        }
    });

})();
```

- [ ] **Step 4: Verificar que la vista completa carga sin errores JS**

`php artisan serve` → `/pruebas`.  
Abrir consola del navegador: sin errores. Verificar que el modal de PDFs se abre y cierra.

- [ ] **Step 5: Commit**

```bash
git add src/ImpresorasService.Web.PHP/resources/views/pruebas/
git commit -m "feat(pruebas): editor de escenario, modal PDFs, polling JS"
```

---

## Task 4: Enlace de navegación en sidebar

**Files:**
- Modify: `src/ImpresorasService.Web.PHP/resources/views/layouts/app.blade.php`

**Interfaces:**
- Consume: nada nuevo
- Produce: enlace "Pruebas" en el sidebar, visible solo para admin, con ícono de beaker

- [ ] **Step 1: Añadir enlace en `layouts/app.blade.php`**

Dentro del bloque `@if($isAdmin ?? false)` del sidebar (después del enlace de "Telegram"), añadir:

```blade
<a href="{{ route('pruebas.index') }}" class="app-nav-link {{ request()->routeIs('pruebas.*') ? 'app-nav-link-active' : '' }}" @if(request()->routeIs('pruebas.*')) aria-current="page" @endif data-label="Pruebas">
    <svg class="nav-icon" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9 3H5a2 2 0 0 0-2 2v4m6-6h10a2 2 0 0 1 2 2v4M9 3v18m0 0h10a2 2 0 0 0 2-2V9M9 21H5a2 2 0 0 1-2-2V9m0 0h18"/></svg>
    <span class="nav-label">Pruebas</span>
    <span class="nav-tooltip" aria-hidden="true">Pruebas</span>
</a>
```

- [ ] **Step 2: Verificar visualmente**

Navegar con admin → sidebar muestra "Pruebas". Navegar con otro rol → no aparece.

- [ ] **Step 3: Commit**

```bash
git add src/ImpresorasService.Web.PHP/resources/views/layouts/app.blade.php
git commit -m "feat(pruebas): enlace en sidebar (solo admin)"
```

---

## Task 5: Smoke test manual end-to-end

No hay tests de integración para esta feature (es una herramienta de dev, no lógica de negocio crítica).

- [ ] **Step 1: Verificar flujo completo**

1. Login como admin → sidebar muestra "Pruebas".
2. Abrir `/pruebas` → estado vacío, panel izquierdo muestra "Sin escenarios".
3. Clic "+ Nuevo" → editor en blanco aparece en panel derecho.
4. Escribir nombre "Test smoke".
5. Clic "+ Añadir lote" → aparece fila en tabla.
6. Seleccionar tienda, poner `ALBARAN`, canal `DEFAULT`, cantidad `2`.
7. Clic "Gestionar PDFs" → modal se abre.
8. Subir un PDF real → aparece en lista, contador actualiza.
9. Clic "Seleccionar" en el PDF → botón del lote muestra el nombre del PDF.
10. Cerrar modal.
11. Clic "Guardar" → toast "Escenario guardado", redirige a `/pruebas?scenario={id}`.
12. El escenario aparece en el panel izquierdo activo.
13. Clic "▶ Lanzar" → tabla de resultados aparece, filas se crean, badges cambian de color cada 2s.
14. Cuando todos terminan → resumen "X OK Y errores" aparece.
15. Clic "Eliminar" en escenario → confirm → redirige a `/pruebas` sin el escenario.

- [ ] **Step 2: Verificar que el PDF en uso no se puede eliminar**

1. Tener un escenario guardado con un PDF asignado.
2. Abrir "Gestionar PDFs" → clic "Eliminar" en ese PDF.
3. Esperado: toast de error "Este PDF está en uso por el escenario X".

- [ ] **Step 3: Commit final**

```bash
git add -A
git commit -m "feat(pruebas): pestaña completa con escenarios, PDFs y polling"
```
