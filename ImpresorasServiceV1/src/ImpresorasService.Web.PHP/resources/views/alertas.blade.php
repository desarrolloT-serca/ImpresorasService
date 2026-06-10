@extends('layouts.app')
@php
    use App\Helpers\DateTimeFormat;
    $storeNameById = collect($storeOptions ?? [])->mapWithKeys(fn ($store) => [(string) ($store['storeId'] ?? '') => $store['name'] ?? null]);
@endphp

@section('title', 'Alertas')

@section('content')
<div class="dbx-wrap">
<x-ui.card>
        @if(isset($apiError) && $apiError)
            <div class="mt-3 alert alert-error" role="alert">{{ $apiError }}</div>
        @endif
        <div class="dbx-queue-topbar">
            <form method="GET" action="{{ url('/alertas') }}" class="dbx-cola-filters-form">
                <div class="dbx-filters">
                    <div class="min-w-44">
                        <label for="alertas-external-job-id" class="dbx-filter-label">ExternalJobId</label>
                        <input id="alertas-external-job-id" type="text" name="externalJobId" class="input"
                            value="{{ $externalJobId ?? request('externalJobId') }}"
                            placeholder="Buscar ExternalJobId">
                    </div>
                </div>
                @if(!empty($storeId))
                    <input type="hidden" name="storeId" value="{{ $storeId }}">
                @endif
                <div class="dbx-form-actions">
                    <a href="{{ url('/alertas') }}" class="btn btn-ghost">Limpiar</a>
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
<div class="dbx-table-meta">
    <div>
        <h2 class="dbx-title">Alertas activas</h2>
        <span class="dbx-subtle">{{ count($jobs ?? []) }} incidencia(s) con los filtros actuales</span>
    </div>
    <span class="dbx-subtle">Prioriza errores y reintentos desde esta vista</span>
</div>
<x-ui.table class="dbx-actions-table">
        <caption class="sr-only">Listado operativo de trabajos con alerta</caption>
        <thead>
            <tr>
                <th class="dbx-checkbox-col">
                    <input type="checkbox" id="bulk-select-all" aria-label="Seleccionar todas las alertas visibles" />
                </th>
                <th class="long-text-col">ExternalJobId</th>
                <th class="number-col">Tienda</th>
                <th>Tipo</th>
                <th class="status-col">Error</th>
                <th class="date-col">Creado</th>
                <th class="actions-col">Acciones</th>
            </tr>
        </thead>
        <tbody>
            @forelse($jobs as $job)
            <tr class="dbx-selectable-row">
                @php
                    $rowJobId = $job['jobId'] ?? $job['JobId'] ?? null;
                    $rowCanBulk = (($isAdmin ?? false) || ($isStoreManager ?? false)) && $rowJobId;
                @endphp
                <td>
                    <input type="checkbox" class="bulk-row" value="{{ $rowJobId ?? '' }}" aria-label="Seleccionar alerta {{ $rowJobId ?? '' }}" {{ $rowCanBulk ? '' : 'disabled' }} />
                </td>
                <td class="long-text-col"><code class="external-job-id" title="{{ $job['externalJobId'] ?? $job['ExternalJobId'] ?? '-' }}">{{ $job['externalJobId'] ?? $job['ExternalJobId'] ?? '-' }}</code></td>
                @php $rowStoreId = $job['storeId'] ?? $job['StoreId'] ?? null; @endphp
                <td class="number-col">{{ \App\Helpers\StoreFormat::label($rowStoreId, $storeNameById[(string) $rowStoreId] ?? null) }}</td>
                <td>{{ $job['documentType'] ?? $job['DocumentType'] ?? '-' }}</td>
                <td class="status-col"><span class="badge badge-danger">{{ $job['lastErrorCode'] ?? $job['LastErrorCode'] ?? '-' }}</span></td>
                <td class="date-col">{{ DateTimeFormat::localDateTime($job['createdAtUtc'] ?? $job['CreatedAtUtc'] ?? $job['created_at_utc'] ?? null) }}</td>
                <td class="actions-col">
                    @php $jobId = $job['jobId'] ?? $job['JobId'] ?? null; @endphp
                    @if(($isAdmin ?? false || $isStoreManager ?? false) && $jobId)
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
            <x-ui.empty-row colspan="7" message="No hay alertas." />
            @endforelse
        </tbody>
</x-ui.table>
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

    function fillJobIdsIntoForm(containerId) {
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
            fillJobIdsIntoForm('bulk-reintentar-jobIds');
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
            fillJobIdsIntoForm('bulk-cancelar-jobIds');
        });
    }
})();
</script>
@endsection
