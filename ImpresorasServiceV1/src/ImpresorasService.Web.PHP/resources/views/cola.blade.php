@extends('layouts.app')
@php
    use App\Helpers\DateTimeFormat;
    $storeNameById = collect($storeOptions ?? [])->mapWithKeys(fn ($store) => [(string) ($store['storeId'] ?? '') => $store['name'] ?? null]);
@endphp

@section('title', 'Cola de impresión')

@section('content')
@if(session('success'))
    <div class="mb-4 alert alert-success">{{ session('success') }}</div>
@endif

<div class="dbx-wrap">
<x-ui.card>
    <div class="dbx-queue-topbar">
        <form method="GET" action="{{ url('/cola') }}" class="dbx-cola-filters-form">
            <div class="dbx-filters">
                <div class="min-w-24">
                    <label for="cola-store" class="dbx-filter-label">Tienda</label>
                    @if($isAdmin ?? false)
                    <select id="cola-store" name="storeId" class="select">
                        <option value="">Todas</option>
                        @foreach($storeOptions ?? [] as $store)
                            <option value="{{ $store['storeId'] }}" {{ (string)old('storeId', $storeId ?? '') === (string)$store['storeId'] ? 'selected' : '' }}>
                                {{ \App\Helpers\StoreFormat::label($store['storeId'], $store['name']) }}
                            </option>
                        @endforeach
                    </select>
                    @else
                    <input id="cola-store" type="text" value="{{ \App\Helpers\StoreFormat::label($effectiveStoreId ?? null, $storeNameById[(string) ($effectiveStoreId ?? '')] ?? null) }}" class="input" disabled>
                    @endif
                </div>
                <div class="min-w-44">
                    <label for="cola-status" class="dbx-filter-label">Estado</label>
                    <select id="cola-status" name="status" class="select">
                        <option value="">Todos</option>
                        <option value="0" {{ ($status ?? '') == '0' ? 'selected' : '' }}>Pendiente</option>
                        <option value="1" {{ ($status ?? '') == '1' ? 'selected' : '' }}>Enrutado</option>
                        <option value="2" {{ ($status ?? '') == '2' ? 'selected' : '' }}>Imprimiendo</option>
                        <option value="6" {{ ($status ?? '') == '6' ? 'selected' : '' }}>Reintento programado</option>
                        <option value="7" {{ ($status ?? '') == '7' ? 'selected' : '' }}>Cancelado</option>
                        <option value="8" {{ ($status ?? '') == '8' ? 'selected' : '' }}>Error final</option>
                    </select>
                </div>
                <div class="min-w-44">
                    <label for="cola-external-job-id" class="dbx-filter-label">ExternalJobId</label>
                    <input id="cola-external-job-id" type="text" name="externalJobId" class="input"
                        value="{{ $externalJobId ?? request('externalJobId') }}"
                        placeholder="Buscar ExternalJobId">
                </div>
                <div class="min-w-24">
                    <label for="cola-limit" class="dbx-filter-label">Resultados</label>
                    <select id="cola-limit" name="limit" class="select">
                        @foreach([50, 100, 250, 500] as $option)
                            <option value="{{ $option }}" {{ (int)($limit ?? 100) === $option ? 'selected' : '' }}>{{ $option }}</option>
                        @endforeach
                    </select>
                </div>
            </div>
            <div class="dbx-form-actions">
                <a href="{{ url('/cola') }}" class="btn btn-ghost">Limpiar</a>
            </div>
        </form>

        @if(($isAdmin ?? false) || ($isStoreManager ?? false))
            <div class="dbx-bulk-toolbar">
                <span id="bulk-selected-count" class="dbx-selection-count bulk-action-control bulk-selected-counter" aria-live="polite">0 seleccionados</span>
                <form id="bulk-reintentar-form" method="POST" action="{{ route('cola.reintentar_masivo') }}" class="inline">
                    @csrf
                    <div id="bulk-reintentar-jobIds"></div>
                    <button type="submit" id="bulk-reintentar-submit" class="btn btn-ghost bulk-action-control" disabled
                        onclick="return confirm('¿Reintentar masivamente los trabajos seleccionados?')">
                        Reintentar masivo
                    </button>
                </form>
                <form id="bulk-cancelar-form" method="POST" action="{{ route('cola.cancelar_masivo') }}" class="inline">
                    @csrf
                    <div id="bulk-cancelar-jobIds"></div>
                    <button type="submit" id="bulk-cancelar-submit" class="btn btn-danger bulk-action-control" disabled
                        onclick="return confirm('¿Cancelar masivamente los trabajos seleccionados?')">
                        Cancelar masivo
                    </button>
                </form>
            </div>
        @endif
    </div>
</x-ui.card>

<x-ui.card class="dbx-operational-card">
<div class="dbx-table-meta">
    <div>
        <h2 class="dbx-title">Trabajos de cola</h2>
        <span class="dbx-subtle">
            {{ $total ?? count($jobs ?? []) }} resultado(s) con los filtros actuales
            @if(($total ?? count($jobs ?? [])) > 0)
                - mostrando {{ $from ?? 1 }}-{{ $to ?? count($jobs ?? []) }}
            @endif
        </span>
    </div>
    <span class="dbx-subtle">Selecciona filas para acciones masivas</span>
</div>
<x-ui.table class="dbx-actions-table">
        <caption class="sr-only">Listado operativo de trabajos de impresion en cola</caption>
        <thead>
            <tr>
                <th class="dbx-checkbox-col">
                    <input type="checkbox" id="bulk-select-all" aria-label="Seleccionar todos los trabajos visibles" />
                </th>
                <th class="long-text-col">ExternalJobId</th>
                <th class="number-col">Tienda</th>
                <th>Tipo</th>
                <th class="status-col">Estado</th>
                <th>Impresora</th>
                <th class="number-col">Intentos</th>
                <th class="date-col">Creado</th>
                <th class="actions-col">Acciones</th>
            </tr>
        </thead>
        <tbody>
            @forelse($jobs as $job)
            <tr class="dbx-selectable-row">
                @php
                    $rowJobId = $job['jobId'] ?? $job['JobId'] ?? null;
                    $rowStatusRaw = $job['_status'] ?? $job['status'] ?? $job['Status'] ?? null;
                    $rowStatusInt = is_numeric($rowStatusRaw) ? (int) $rowStatusRaw : null;
                    $rowCanBulk = (($isAdmin ?? false) || ($isStoreManager ?? false)) && $rowJobId;
                @endphp
                <td>
                    <input type="checkbox" class="bulk-row" value="{{ $rowJobId ?? '' }}" aria-label="Seleccionar trabajo {{ $rowJobId ?? '' }}" {{ $rowCanBulk ? '' : 'disabled' }} />
                </td>
                <td class="long-text-col"><code class="external-job-id" title="{{ $job['externalJobId'] ?? $job['ExternalJobId'] ?? '-' }}">{{ $job['externalJobId'] ?? $job['ExternalJobId'] ?? '-' }}</code></td>
                @php $rowStoreId = $job['storeId'] ?? $job['StoreId'] ?? null; @endphp
                <td class="number-col">{{ \App\Helpers\StoreFormat::label($rowStoreId, $storeNameById[(string) $rowStoreId] ?? null) }}</td>
                <td>{{ $job['documentType'] ?? $job['DocumentType'] ?? '-' }}</td>
                @php
                    $stRaw = $job['_status'] ?? $job['status'] ?? $job['Status'] ?? null;
                    $stInt = is_numeric($stRaw) ? (int) $stRaw : null;
                    $statusClass = match ($stInt) {
                        8 => 'critical', // Error final
                        0, 2, 6 => 'warning', // Pendiente / Imprimiendo / Reintento
                        1 => 'info', // Enrutado
                        3, 4, 5 => 'healthy', // Aceptado / Impreso
                        7 => 'neutral', // Cancelado
                        default => 'neutral',
                    };
                @endphp
                <td class="status-col"><span class="dbx-pill {{ $statusClass }}">{{ \App\Helpers\StatusLabels::get($stRaw) }}</span></td>
                @php
                    $printerName = $job['printerName'] ?? $job['PrinterName'] ?? null;
                    $printerId = $job['printerId'] ?? $job['PrinterId'] ?? null;
                @endphp
                <td>{{ filled($printerName) ? $printerName : ($printerId ? "Impresora {$printerId}" : 'Sin asignar') }}</td>
                <td class="number-col">{{ $job['attemptCount'] ?? $job['AttemptCount'] ?? 0 }}</td>
                <td class="date-col">{{ DateTimeFormat::localDateTime($job['createdAtUtc'] ?? $job['CreatedAtUtc'] ?? $job['created_at_utc'] ?? null) }}</td>
                <td class="actions-col">
                    @php $jobId = $job['jobId'] ?? $job['JobId'] ?? null; $st = $job['_status'] ?? $job['status'] ?? $job['Status'] ?? null; @endphp
                    @if(($isAdmin ?? false || $isStoreManager ?? false) && $jobId && in_array($st, [0, 8]))
                    <x-ui.action-buttons>
                    <form action="{{ url("/cola/{$jobId}/reintentar") }}" method="POST">
                        @csrf
                        <button type="submit" class="btn btn-ghost">Reintentar</button>
                    </form>
                    <form action="{{ url("/cola/{$jobId}/cancelar") }}" method="POST" class="inline" onsubmit="return confirm('¿Cancelar este trabajo?')">
                        @csrf
                        <button type="submit" class="btn btn-danger">Cancelar</button>
                    </form>
                    </x-ui.action-buttons>
                    @endif
                </td>
            </tr>
            @empty
            <x-ui.empty-row colspan="9" message="No hay trabajos en la cola." />
            @endforelse
        </tbody>
</x-ui.table>
@php
    $queryBase = request()->except('page');
    $prevUrl = ($page ?? 1) > 1
        ? url('/cola?' . http_build_query(array_merge($queryBase, ['page' => ($page ?? 1) - 1])))
        : null;
    $nextUrl = ($page ?? 1) < ($lastPage ?? 1)
        ? url('/cola?' . http_build_query(array_merge($queryBase, ['page' => ($page ?? 1) + 1])))
        : null;
@endphp
<nav class="dbx-pagination" aria-label="Paginacion de trabajos de cola">
    <a class="btn btn-ghost {{ $prevUrl ? '' : 'is-disabled' }}" href="{{ $prevUrl ?? '#' }}" aria-disabled="{{ $prevUrl ? 'false' : 'true' }}">Anterior</a>
    <span class="dbx-pagination-status">Pagina {{ $page ?? 1 }} de {{ $lastPage ?? 1 }}</span>
    <a class="btn btn-ghost {{ $nextUrl ? '' : 'is-disabled' }}" href="{{ $nextUrl ?? '#' }}" aria-disabled="{{ $nextUrl ? 'false' : 'true' }}">Siguiente</a>
</nav>
</x-ui.card>
</div>

<script>
(function() {
    const headerCb = document.getElementById('bulk-select-all');
    const rowCbs = Array.from(document.querySelectorAll('.bulk-row'));
    const retrySubmit = document.getElementById('bulk-reintentar-submit');
    const cancelSubmit = document.getElementById('bulk-cancelar-submit');
    const selectedCount = document.getElementById('bulk-selected-count');

    function getSelectedIds() {
        return rowCbs.filter(cb => !cb.disabled && cb.checked).map(cb => cb.value).filter(v => v !== '');
    }

    function updateSubmitEnabled() {
        const count = getSelectedIds().length;
        if (retrySubmit) retrySubmit.disabled = count === 0;
        if (cancelSubmit) cancelSubmit.disabled = count === 0;
        if (selectedCount) selectedCount.textContent = count + (count === 1 ? ' seleccionado' : ' seleccionados');
        if (headerCb) {
            const selectableCount = rowCbs.filter(cb => !cb.disabled).length;
            headerCb.checked = selectableCount > 0 && count === selectableCount;
            headerCb.indeterminate = count > 0 && count < selectableCount;
        }
    }

    if (headerCb) {
        headerCb.addEventListener('change', function() {
            rowCbs.forEach(cb => {
                if (cb.disabled) return;
                cb.checked = headerCb.checked;
                setRowSelectedState(cb);
            });
            updateSubmitEnabled();
        });
    }

    function setRowSelectedState(cb) {
        const row = cb.closest('tr');
        if (row) row.classList.toggle('is-selected', cb.checked);
    }

    function isInteractiveClick(target) {
        return Boolean(target.closest('a, button, input, select, textarea, label, form, [role="button"]'));
    }

    rowCbs.forEach(cb => cb.addEventListener('change', function() {
        setRowSelectedState(cb);
        updateSubmitEnabled();
    }));

    document.querySelectorAll('.dbx-selectable-row').forEach(row => {
        row.addEventListener('click', function(event) {
            if (isInteractiveClick(event.target)) return;
            const cb = row.querySelector('.bulk-row');
            if (!cb || cb.disabled) return;
            cb.checked = !cb.checked;
            cb.dispatchEvent(new Event('change', { bubbles: true }));
        });
    });
    updateSubmitEnabled();

    function fillJobIdsIntoForm(formEl, containerId) {
        const selected = getSelectedIds();
        const container = document.getElementById(containerId);
        if (!container) return;

        container.innerHTML = '';
        selected.forEach(jobId => {
            const inp = document.createElement('input');
            inp.type = 'hidden';
            inp.name = 'jobIds[]';
            inp.value = jobId;
            container.appendChild(inp);
        });
    }

    const retryForm = document.getElementById('bulk-reintentar-form');
    if (retryForm) {
        retryForm.addEventListener('submit', function(e) {
            const selected = getSelectedIds();
            if (selected.length === 0) {
                e.preventDefault();
                return;
            }
            fillJobIdsIntoForm(retryForm, 'bulk-reintentar-jobIds');
        });
    }

    const cancelForm = document.getElementById('bulk-cancelar-form');
    if (cancelForm) {
        cancelForm.addEventListener('submit', function(e) {
            const selected = getSelectedIds();
            if (selected.length === 0) {
                e.preventDefault();
                return;
            }
            fillJobIdsIntoForm(cancelForm, 'bulk-cancelar-jobIds');
        });
    }
})();
</script>
@endsection
