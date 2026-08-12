@extends('layouts.app')
@php
    use App\Helpers\DateTimeFormat;
    $storeNameById = collect($storeOptions ?? [])->mapWithKeys(fn ($store) => [(string) ($store['storeId'] ?? '') => $store['name'] ?? null]);
@endphp

@section('title', 'Cola de impresión')

@section('content')

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
                        {{-- Sin este filtro no habia forma de listar los trabajos que esperan
                             decision del operador, que son los unicos que requieren su atencion. --}}
                        <option value="5" {{ ($status ?? '') == '5' ? 'selected' : '' }}>Sin confirmar</option>
                        <option value="6" {{ ($status ?? '') == '6' ? 'selected' : '' }}>Reintento programado</option>
                        <option value="7" {{ ($status ?? '') == '7' ? 'selected' : '' }}>Cancelado</option>
                        <option value="8" {{ ($status ?? '') == '8' ? 'selected' : '' }}>Error</option>
                        <option value="9" {{ ($status ?? '') == '9' ? 'selected' : '' }}>Bloqueado</option>
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
                @php
                    $errorFilterUrl = url('/cola?' . http_build_query(array_merge(request()->except(['status', 'page']), ['status' => 8])));
                @endphp
                <a href="{{ $errorFilterUrl }}" class="btn {{ ($status ?? '') == '8' ? 'btn-danger' : 'btn-ghost' }}">Solo errores</a>
                <a href="{{ url('/cola') }}" class="btn btn-ghost">Limpiar</a>
            </div>
        </form>
    </div>
</x-ui.card>

<x-ui.card class="dbx-operational-card">
<div class="dbx-cola-section-header">
    <div class="dbx-cola-section-header-top">
        <h2 class="dbx-title">Trabajos de cola</h2>
        <div class="dbx-column-toggles" style="display:flex;gap:12px;align-items:center;">
            <label class="dbx-subtle"><input type="checkbox" id="col-toggle-error" checked> Error</label>
            <label class="dbx-subtle"><input type="checkbox" id="col-toggle-intentos" checked> Intentos</label>
        </div>
    </div>
    <div class="dbx-cola-section-header-bottom">
        <span class="dbx-subtle">
            {{ $total ?? count($jobs ?? []) }} resultado(s) con los filtros actuales
            @if(($total ?? count($jobs ?? [])) > 0)
                - mostrando {{ $from ?? 1 }}-{{ $to ?? count($jobs ?? []) }}
            @endif
        </span>
        <span class="dbx-subtle">Selecciona filas para acciones masivas</span>
    </div>
</div>

@if(($isAdmin ?? false) || ($isStoreManager ?? false))
    <div id="cola-bulk-bar" class="dbx-bulk-bar" hidden>
        <span id="bulk-selected-count" class="dbx-bulk-bar-count" aria-live="polite">0 seleccionados</span>
        <span class="dbx-bulk-bar-hint">Se aplicara la accion a los trabajos seleccionados.</span>
        <div class="dbx-bulk-bar-actions">
            <form id="bulk-reintentar-form" method="POST" action="{{ route('cola.reintentar_masivo') }}" class="inline"
                data-cola-action="reintentar-masivo"
                data-confirm="¿Reintentar masivamente los trabajos seleccionados?">
                @csrf
                <div id="bulk-reintentar-jobIds"></div>
                <button type="submit" id="bulk-reintentar-submit" class="btn btn-ghost bulk-action-control" disabled>
                    Reintentar
                </button>
            </form>
            <form id="bulk-cancelar-form" method="POST" action="{{ route('cola.cancelar_masivo') }}" class="inline"
                data-cola-action="cancelar-masivo"
                data-confirm="¿Cancelar masivamente los trabajos seleccionados?">
                @csrf
                <div id="bulk-cancelar-jobIds"></div>
                <button type="submit" id="bulk-cancelar-submit" class="btn btn-danger bulk-action-control" disabled>
                    Cancelar
                </button>
            </form>
        </div>
    </div>
@endif
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
                <th class="status-col" data-col="error">Error</th>
                <th>Impresora</th>
                <th class="number-col" data-col="intentos">Intentos</th>
                <th class="date-col">Creado</th>
                <th class="actions-col">Acciones</th>
            </tr>
        </thead>
        <tbody>
            @forelse($jobs as $job)
            @php
                $rowJobId = $job['jobId'] ?? $job['JobId'] ?? null;
                $rowStatusRaw = $job['_status'] ?? $job['status'] ?? $job['Status'] ?? null;
                $rowStatusInt = \App\Helpers\StatusLabels::normalizeToInt($rowStatusRaw);
                $rowCanBulk = (($isAdmin ?? false) || ($isStoreManager ?? false)) && $rowJobId;
            @endphp
            <tr class="dbx-selectable-row cola-job-row" data-job-id="{{ $rowJobId ?? '' }}" data-status="{{ $rowStatusInt ?? '' }}">
                <td>
                    <input type="checkbox" class="bulk-row" value="{{ $rowJobId ?? '' }}" aria-label="Seleccionar trabajo {{ $rowJobId ?? '' }}" {{ $rowCanBulk ? '' : 'disabled' }} />
                </td>
                <td class="long-text-col"><code class="external-job-id" title="{{ $job['externalJobId'] ?? $job['ExternalJobId'] ?? '-' }}">{{ $job['externalJobId'] ?? $job['ExternalJobId'] ?? '-' }}</code></td>
                @php $rowStoreId = $job['storeId'] ?? $job['StoreId'] ?? null; @endphp
                <td class="number-col">{{ \App\Helpers\StoreFormat::label($rowStoreId, $storeNameById[(string) $rowStoreId] ?? null) }}</td>
                <td>{{ $job['documentType'] ?? $job['DocumentType'] ?? '-' }}</td>
                @php
                    $stRaw = $job['_status'] ?? $job['status'] ?? $job['Status'] ?? null;
                    $stInt = \App\Helpers\StatusLabels::normalizeToInt($stRaw);
                    $statusClass = match ($stInt) {
                        8 => 'critical',
                        0, 2, 6, 9 => 'warning',
                        1 => 'info',
                        3, 4, 5 => 'healthy',
                        7 => 'neutral',
                        default => 'neutral',
                    };
                    $pillTitle = in_array($stInt, [8, 9], true) ? ($job['lastErrorMessage'] ?? $job['LastErrorMessage'] ?? null) : null;
                    $lastErrorCode = $job['lastErrorCode'] ?? $job['LastErrorCode'] ?? null;
                @endphp
                <td class="status-col"><span class="dbx-pill {{ $statusClass }}"{!! $pillTitle ? ' title="'.e($pillTitle).'"' : '' !!}>{{ \App\Helpers\StatusLabels::get($stRaw) }}</span></td>
                <td class="status-col" data-col="error">
                    @if($stInt === 8 && filled($lastErrorCode))
                        <span class="badge badge-danger"{!! $pillTitle ? ' title="'.e($pillTitle).'"' : '' !!}>{{ $lastErrorCode }}</span>
                    @else
                        -
                    @endif
                </td>
                @php
                    $printerName = $job['printerName'] ?? $job['PrinterName'] ?? null;
                    $printerId = $job['printerId'] ?? $job['PrinterId'] ?? null;
                @endphp
                <td>{{ filled($printerName) ? $printerName : ($printerId ? "Impresora {$printerId}" : 'Sin asignar') }}</td>
                <td class="number-col" data-col="intentos">{{ $job['attemptCount'] ?? $job['AttemptCount'] ?? 0 }}</td>
                <td class="date-col">{{ DateTimeFormat::localDateTime($job['createdAtUtc'] ?? $job['CreatedAtUtc'] ?? $job['created_at_utc'] ?? null) }}</td>
                <td class="actions-col">
                    @php
                        $jobId = $job['jobId'] ?? $job['JobId'] ?? null;
                        $st = \App\Helpers\StatusLabels::normalizeToInt($job['_status'] ?? $job['status'] ?? $job['Status'] ?? null);
                    @endphp
                    @php
                        // 0 Pendiente y 8 Error: el trabajo no ha salido, reintentar es seguro.
                        // 5 Sin confirmar y 9 Bloqueado: el sistema no sabe si el documento salio,
                        // asi que la decision es del operador y reimprimir puede duplicarlo.
                        $canRetry = in_array($st, [0, 8], true);
                        $isUncertain = in_array($st, [5, 9], true);
                        $canManage = ($isAdmin ?? false) || ($isStoreManager ?? false);
                    @endphp
                    @if($canManage && $jobId && ($canRetry || $isUncertain))
                    <x-ui.action-buttons>
                    @if($canRetry)
                    <form action="{{ url("/cola/{$jobId}/reintentar") }}" method="POST" data-cola-action="reintentar">
                        @csrf
                        <button type="submit" class="btn btn-ghost">Reintentar</button>
                    </form>
                    @endif
                    @if($isUncertain)
                    <form action="{{ url("/cola/{$jobId}/confirmar") }}" method="POST" class="inline"
                        data-cola-action="confirmar" data-confirm="¿Confirmas que este documento sí se imprimió?">
                        @csrf
                        <button type="submit" class="btn btn-ghost">Sí se imprimió</button>
                    </form>
                    @endif
                    {{-- Solo desde "Sin confirmar": con la impresora bloqueada la API no admite reimprimir. --}}
                    @if($st === 5)
                    <form action="{{ url("/cola/{$jobId}/reintentar") }}" method="POST" class="inline"
                        data-cola-action="reintentar" data-confirm-danger="1"
                        data-confirm="Este trabajo puede haberse impreso ya. Si lo reimprimes y sí había salido, el documento saldrá por duplicado. ¿Continuar?">
                        @csrf
                        <button type="submit" class="btn btn-warning">Reimprimir</button>
                    </form>
                    @endif
                    <form action="{{ url("/cola/{$jobId}/cancelar") }}" method="POST" class="inline"
                        data-cola-action="cancelar" data-confirm="¿Cancelar este trabajo?">
                        @csrf
                        <button type="submit" class="btn btn-danger">Cancelar</button>
                    </form>
                    </x-ui.action-buttons>
                    @endif
                </td>
            </tr>
            @empty
            <x-ui.empty-row colspan="10" message="No hay trabajos en la cola." />
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
    @if($prevUrl)
        <a class="btn btn-ghost" href="{{ $prevUrl }}">Anterior</a>
    @else
        <span class="btn btn-ghost is-disabled" aria-disabled="true" tabindex="-1">Anterior</span>
    @endif
    <span class="dbx-pagination-status">Pagina {{ $page ?? 1 }} de {{ $lastPage ?? 1 }}</span>
    @if($nextUrl)
        <a class="btn btn-ghost" href="{{ $nextUrl }}">Siguiente</a>
    @else
        <span class="btn btn-ghost is-disabled" aria-disabled="true" tabindex="-1">Siguiente</span>
    @endif
</nav>
</x-ui.card>
</div>

<script>
(function() {
    const STATUS_LABELS = @json(\App\Helpers\StatusLabels::all());
    const ACTIONABLE_STATUSES = [0, 8];
    // Estados en los que el sistema no sabe si el documento salio: la decision es del operador.
    const UNCERTAIN_STATUSES = [5, 9];
    const canManageQueue = @json(($isAdmin ?? false) || ($isStoreManager ?? false));
    const csrfToken = document.querySelector('meta[name="csrf-token"]')?.content || '';
    const colaReintentarBase = @json(url('/cola'));
    const statusFilterEl = document.getElementById('cola-status');

    const headerCb = document.getElementById('bulk-select-all');
    const retrySubmit = document.getElementById('bulk-reintentar-submit');
    const cancelSubmit = document.getElementById('bulk-cancelar-submit');
    const selectedCount = document.getElementById('bulk-selected-count');
    const bulkBar = document.getElementById('cola-bulk-bar');

    function getRowCheckboxes() {
        return Array.from(document.querySelectorAll('.bulk-row'));
    }

    function getSelectedIds() {
        return getRowCheckboxes().filter(cb => !cb.disabled && cb.checked).map(cb => cb.value).filter(v => v !== '');
    }

    function statusPillClass(statusInt) {
        if (statusInt === 8) return 'critical';
        if ([0, 2, 6, 9].includes(statusInt)) return 'warning';
        if (statusInt === 1) return 'info';
        if ([3, 4, 5].includes(statusInt)) return 'healthy';
        return 'neutral';
    }

    function getActiveStatusFilter() {
        const value = statusFilterEl?.value ?? '';
        return value === '' ? null : parseInt(value, 10);
    }

    function findJobRow(jobId) {
        return document.querySelector('.cola-job-row[data-job-id="' + CSS.escape(jobId) + '"]');
    }

    function isActionableStatus(statusInt) {
        return ACTIONABLE_STATUSES.includes(statusInt);
    }

    function updateSubmitEnabled() {
        const rowCbs = getRowCheckboxes();
        const count = getSelectedIds().length;
        if (retrySubmit) retrySubmit.disabled = count === 0;
        if (cancelSubmit) cancelSubmit.disabled = count === 0;
        if (selectedCount) {
            selectedCount.textContent = count + (count === 1 ? ' seleccionado' : ' seleccionados');
        }
        if (bulkBar) {
            bulkBar.hidden = count === 0;
        }
        if (headerCb) {
            const selectableCount = rowCbs.filter(cb => !cb.disabled).length;
            headerCb.checked = selectableCount > 0 && count === selectableCount;
            headerCb.indeterminate = count > 0 && count < selectableCount;
        }
    }

    function setRowSelectedState(cb) {
        const row = cb.closest('tr');
        if (row) row.classList.toggle('is-selected', cb.checked);
    }

    function clearRowSelection(row) {
        const cb = row.querySelector('.bulk-row');
        if (!cb) return;
        cb.checked = false;
        row.classList.remove('is-selected');
    }

    function renderActionsCell(row, jobId, statusInt) {
        const td = row.querySelector('.actions-col');
        if (!td || !canManageQueue) return;

        const canRetry = isActionableStatus(statusInt);
        const isUncertain = UNCERTAIN_STATUSES.includes(statusInt);

        if (!canRetry && !isUncertain) {
            td.innerHTML = '';
            return;
        }

        const base = colaReintentarBase + '/' + encodeURIComponent(jobId);
        const token = '<input type="hidden" name="_token" value="' + csrfToken + '">';
        let html = '<div class="action-buttons">';

        if (canRetry) {
            html +=
                '<form action="' + base + '/reintentar" method="POST" data-cola-action="reintentar">' +
                    token +
                    '<button type="submit" class="btn btn-ghost">Reintentar</button>' +
                '</form>';
        }

        if (isUncertain) {
            html +=
                '<form action="' + base + '/confirmar" method="POST" class="inline" data-cola-action="confirmar" data-confirm="¿Confirmas que este documento sí se imprimió?">' +
                    token +
                    '<button type="submit" class="btn btn-ghost">Sí se imprimió</button>' +
                '</form>';
        }

        // Solo desde "Sin confirmar": con la impresora bloqueada la API no admite reimprimir.
        if (statusInt === 5) {
            html +=
                '<form action="' + base + '/reintentar" method="POST" class="inline" data-cola-action="reintentar" data-confirm-danger="1" data-confirm="Este trabajo puede haberse impreso ya. Si lo reimprimes y sí había salido, el documento saldrá por duplicado. ¿Continuar?">' +
                    token +
                    '<button type="submit" class="btn btn-warning">Reimprimir</button>' +
                '</form>';
        }

        html +=
            '<form action="' + base + '/cancelar" method="POST" class="inline" data-cola-action="cancelar" data-confirm="¿Cancelar este trabajo?">' +
                token +
                '<button type="submit" class="btn btn-danger">Cancelar</button>' +
            '</form>' +
        '</div>';

        td.innerHTML = html;
    }

    function updateStatusPill(row, statusInt) {
        const pill = row.querySelector('.status-col .dbx-pill');
        if (!pill) return;

        pill.className = 'dbx-pill ' + statusPillClass(statusInt);
        pill.textContent = STATUS_LABELS[statusInt] ?? '-';

        const errorCell = row.querySelectorAll('.status-col')[1];
        if (errorCell && statusInt !== 8) {
            errorCell.textContent = '-';
        }
    }

    function hideRowIfFiltered(row, statusInt) {
        const filter = getActiveStatusFilter();
        if (filter === null || filter === statusInt) {
            row.hidden = false;
            return;
        }

        row.hidden = true;
        clearRowSelection(row);
    }

    function applyJobResult(result) {
        if (!result?.jobId) return;

        const row = findJobRow(result.jobId);
        if (!row) return;

        if (!result.ok || result.newStatus === undefined) return;

        const newStatus = parseInt(result.newStatus, 10);
        row.dataset.status = String(newStatus);
        updateStatusPill(row, newStatus);
        renderActionsCell(row, result.jobId, newStatus);
        hideRowIfFiltered(row, newStatus);
    }

    function setFormBusy(form, busy) {
        form.classList.toggle('is-submitting', busy);
        form.querySelectorAll('button[type="submit"]').forEach(btn => {
            btn.disabled = busy;
        });
    }

    function showColaToast(message, type) {
        if (typeof window.showToast === 'function') {
            window.showToast(message, type);
            return;
        }
        window.alert(message);
    }

    async function submitColaForm(form) {
        if (!window.fetch) {
            form.submit();
            return;
        }

        const confirmMessage = form.dataset.confirm;
        if (confirmMessage) {
            const ok = window.confirmDialog
                ? await window.confirmDialog(confirmMessage, { title: 'Confirmar accion', danger: form.dataset.confirmDanger === '1' || form.dataset.colaAction === 'cancelar-masivo' || form.dataset.colaAction === 'cancelar' })
                : window.confirm(confirmMessage);
            if (!ok) return;
        }

        const action = form.dataset.colaAction;
        if (action === 'reintentar-masivo') {
            fillJobIdsIntoForm(form, 'bulk-reintentar-jobIds');
        } else if (action === 'cancelar-masivo') {
            fillJobIdsIntoForm(form, 'bulk-cancelar-jobIds');
        }

        if (action === 'reintentar-masivo' || action === 'cancelar-masivo') {
            const selected = getSelectedIds();
            if (selected.length === 0) {
                showColaToast('Selecciona al menos un trabajo.', 'error');
                return;
            }
        }

        setFormBusy(form, true);

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                headers: {
                    'Accept': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest',
                    'X-CSRF-TOKEN': csrfToken
                },
                body: new FormData(form)
            });

            let payload = {};
            try {
                payload = await response.json();
            } catch (error) {
                throw new Error('Respuesta no valida del servidor.');
            }

            const toastType = (payload.ok || (payload.processed > 0)) ? 'success' : 'error';
            if (payload.message) {
                showColaToast(payload.message, toastType);
            }

            (payload.results || []).forEach(applyJobResult);
            updateSubmitEnabled();
        } catch (error) {
            showColaToast('No se pudo completar la operacion. Intentalo de nuevo.', 'error');
        } finally {
            setFormBusy(form, false);
            if (action === 'reintentar-masivo' || action === 'cancelar-masivo') {
                updateSubmitEnabled();
            }
        }
    }

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

    function isInteractiveClick(target) {
        return Boolean(target.closest('a, button, input, select, textarea, label, form, [role="button"]'));
    }

    if (headerCb) {
        headerCb.addEventListener('change', function() {
            getRowCheckboxes().forEach(cb => {
                if (cb.disabled) return;
                cb.checked = headerCb.checked;
                setRowSelectedState(cb);
            });
            updateSubmitEnabled();
        });
    }

    document.addEventListener('change', function(event) {
        const cb = event.target.closest('.bulk-row');
        if (!cb) return;
        setRowSelectedState(cb);
        updateSubmitEnabled();
    });

    document.querySelectorAll('.dbx-selectable-row').forEach(row => {
        row.addEventListener('click', function(event) {
            if (isInteractiveClick(event.target)) return;
            const cb = row.querySelector('.bulk-row');
            if (!cb || cb.disabled) return;
            cb.checked = !cb.checked;
            cb.dispatchEvent(new Event('change', { bubbles: true }));
        });
    });

    document.addEventListener('submit', function(event) {
        const form = event.target.closest('form[data-cola-action]');
        if (!form) return;
        event.preventDefault();
        submitColaForm(form);
    });

    const COLUMN_STORAGE_KEY = 'cola-visible-columns';
    const columnToggles = {
        error: document.getElementById('col-toggle-error'),
        intentos: document.getElementById('col-toggle-intentos'),
    };
    let columnPrefs = {};
    try {
        columnPrefs = JSON.parse(localStorage.getItem(COLUMN_STORAGE_KEY) || '{}');
    } catch (error) {
        columnPrefs = {};
    }

    function applyColumnVisibility(col, visible) {
        document.querySelectorAll('[data-col="' + col + '"]').forEach(el => {
            el.style.display = visible ? '' : 'none';
        });
    }

    Object.entries(columnToggles).forEach(([col, checkbox]) => {
        if (!checkbox) return;
        const visible = columnPrefs[col] !== false;
        checkbox.checked = visible;
        applyColumnVisibility(col, visible);
        checkbox.addEventListener('change', function() {
            applyColumnVisibility(col, checkbox.checked);
            columnPrefs[col] = checkbox.checked;
            localStorage.setItem(COLUMN_STORAGE_KEY, JSON.stringify(columnPrefs));
        });
    });

    updateSubmitEnabled();
})();
</script>
@endsection
