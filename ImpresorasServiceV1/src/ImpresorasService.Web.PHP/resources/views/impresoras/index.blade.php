@extends('layouts.app')

@section('title', 'Impresoras')

@section('content')
@php
    $printersByStore = is_array($printersByStore ?? null) ? $printersByStore : [];
    $selectedStoreGroup = is_array($selectedStoreGroup ?? null) ? $selectedStoreGroup : null;
    $selectedStoreId = $selectedStoreGroup['storeId'] ?? ($selectedStoreId ?? null);
    $selectedStoreKey = $selectedStoreId !== null ? (string) $selectedStoreId : 'none';
    $isActiveFilter = (string) ($isActiveFilter ?? request('isActive', ''));
    $selectedStorePrintersCount = is_array($selectedStoreGroup['printers'] ?? null) ? count($selectedStoreGroup['printers']) : 0;
    $createUrl = $selectedStoreId !== null
        ? route('impresoras.create', ['storeId' => $selectedStoreId])
        : route('impresoras.create');
@endphp

@if(session('success'))
    <div class="mb-4 alert alert-success">{{ session('success') }}</div>
@endif

<div class="dbx-wrap">
<section class="dbx-routing-layout">
    <x-ui.card class="dbx-routing-stores-card">
        <div class="dbx-title-row">
            <h2 class="dbx-title">Tiendas</h2>
            <span class="dbx-subtle">Selecciona una tienda</span>
        </div>

        @if(count($printersByStore) === 0)
            <p class="dbx-subtle">No hay tiendas disponibles.</p>
        @else
            <div class="dbx-routing-store-list">
                @foreach($printersByStore as $storeGroup)
                    @php
                        $storeId = $storeGroup['storeId'] ?? null;
                        $storeKey = $storeId !== null ? (string) $storeId : 'none';
                        $isSelected = $storeKey === $selectedStoreKey;
                        $storeUrlParams = ['storeId' => $storeId, 'isActive' => $isActiveFilter];
                        $storeUrlParams = array_filter($storeUrlParams, static fn ($value) => $value !== null && $value !== '');
                        $printersCount = (int) ($storeGroup['printersCount'] ?? 0);
                        $activePrintersCount = (int) ($storeGroup['activePrintersCount'] ?? 0);
                        $connectionErrorCount = (int) ($storeGroup['connectionErrorCount'] ?? 0);
                        $printerWord = $printersCount === 1 ? 'impresora' : 'impresoras';
                    @endphp
                    <a href="{{ route('impresoras.index', $storeUrlParams) }}"
                       class="dbx-routing-store-link {{ $isSelected ? 'is-active' : '' }} {{ $printersCount > 0 ? 'has-printers' : 'is-empty-store' }} {{ $connectionErrorCount > 0 ? 'has-printer-errors' : '' }}">
                        <span class="dbx-routing-store-name">{{ $storeGroup['formattedStoreName'] ?? 'Sin tienda' }}</span>
                        <span class="dbx-routing-store-meta">
                            {{ $printersCount }} {{ $printerWord }}
                            &middot;
                            {{ $activePrintersCount }} activas
                            @if($connectionErrorCount > 0)
                                &middot; {{ $connectionErrorCount }} con error
                            @endif
                        </span>
                    </a>
                @endforeach
            </div>
        @endif
    </x-ui.card>

    <x-ui.card class="dbx-routing-rules-card">
        <div class="dbx-title-row dbx-routing-title-row">
            <div>
                <h2 class="dbx-title">
                    @if($selectedStoreGroup)
                        Impresoras de {{ $selectedStoreGroup['formattedStoreName'] ?? 'la tienda seleccionada' }}
                    @else
                        Impresoras
                    @endif
                </h2>
                <span class="dbx-subtle">Estado, conectividad y configuraci&oacute;n</span>
            </div>
            <div class="dbx-printer-panel-tools">
                <form method="GET" class="dbx-printer-state-filter">
                    @if($selectedStoreId !== null)
                        <input type="hidden" name="storeId" value="{{ $selectedStoreId }}">
                    @endif
                    <label for="isActive" class="dbx-filter-label">Estado</label>
                    <select name="isActive" id="isActive" class="select" onchange="this.form.submit()">
                        <option value="" {{ $isActiveFilter === '' ? 'selected' : '' }}>Todas</option>
                        <option value="1" {{ $isActiveFilter === '1' ? 'selected' : '' }}>Activas</option>
                        <option value="0" {{ $isActiveFilter === '0' ? 'selected' : '' }}>Inactivas</option>
                    </select>
                </form>
                @if($isAdmin ?? false)
                    <a href="{{ $createUrl }}" class="btn btn-primary dbx-routing-create-btn" title="Crear impresora para esta tienda" aria-label="Crear impresora para esta tienda">+ Nueva impresora</a>
                @endif
            </div>
        </div>

        @if(!$selectedStoreGroup)
            <div class="dbx-empty-state">No hay tienda seleccionada.</div>
        @elseif($selectedStorePrintersCount === 0)
            <div class="dbx-empty-state">Esta tienda no tiene impresoras configuradas.</div>
        @elseif(count($printers) === 0)
            <div class="dbx-empty-state">No hay impresoras con el filtro actual.</div>
        @else
            <x-ui.table class="dbx-actions-table dbx-routing-rules-table dbx-printers-table">
                <thead>
                    <tr>
                        <th class="text-col">Nombre</th>
                        <th class="text-col">SpoolQueue</th>
                        <th class="text-col">Host</th>
                        <th class="status-col">Activa</th>
                        <th class="status-col">Conectividad</th>
                        <th class="status-col">Puerto</th>
                        <th class="actions-col">Acciones</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach($printers as $p)
                        @php
                            $id = $p['printerId'] ?? $p['PrinterId'] ?? null;
                            $name = $p['printerName'] ?? $p['PrinterName'] ?? '-';
                            $spoolQueue = $p['spoolQueue'] ?? $p['SpoolQueue'] ?? '-';
                            $host = trim((string) ($p['host'] ?? $p['Host'] ?? ''));
                            $isActive = (bool) ($p['isActive'] ?? $p['IsActive'] ?? false);
                            $editUrl = $id ? route('impresoras.edit', ['impresora' => $id, 'storeId' => $selectedStoreId]) : '#';
                        @endphp
                        <tr data-printer-id="{{ $id ?? '' }}">
                            <td class="text-col">{{ $name }}</td>
                            <td class="text-col dbx-printer-text-cell" title="{{ $spoolQueue }}">{{ $spoolQueue }}</td>
                            <td class="text-col dbx-printer-text-cell" title="{{ $host !== '' ? $host : 'Sin host configurado' }}">{{ $host !== '' ? $host : '-' }}</td>
                            <td class="status-col">
                                <span class="badge status-chip {{ $isActive ? 'badge-success' : 'badge-danger' }}" aria-label="{{ $isActive ? 'Impresora activa' : 'Impresora inactiva' }}">
                                    {{ $isActive ? 'Si' : 'No' }}
                                </span>
                            </td>
                            <td class="status-col">
                                <span class="ping-status badge badge-neutral" data-id="{{ $id ?? '' }}">-</span>
                            </td>
                            <td class="status-col">
                                <span class="ping-port badge badge-neutral" data-id="{{ $id ?? '' }}">-</span>
                            </td>
                            <td class="actions-col">
                                @if(($isAdmin ?? false) && $id)
                                    <x-ui.action-buttons>
                                        <a href="{{ $editUrl }}" class="btn btn-ghost">Editar</a>
                                        <form action="{{ route('impresoras.destroy', $id) }}" method="POST" onsubmit="return confirm('&iquest;Eliminar?')">
                                            @csrf
                                            @method('DELETE')
                                            @if($selectedStoreId !== null)
                                                <input type="hidden" name="storeId" value="{{ $selectedStoreId }}">
                                            @endif
                                            <button type="submit" class="btn btn-danger">Eliminar</button>
                                        </form>
                                    </x-ui.action-buttons>
                                @else
                                    <span class="text-slate-400 text-sm">-</span>
                                @endif
                            </td>
                        </tr>
                    @endforeach
                </tbody>
            </x-ui.table>
        @endif
    </x-ui.card>
</section>
</div>

<script>
(function() {
    const csrf = document.querySelector('meta[name="csrf-token"]')?.content || '';
    const pingInterval = {{ $pingIntervalSeconds ?? 30 }} * 1000;
    const isAdmin = {{ ($isAdmin ?? false) ? 'true' : 'false' }};
    const slowLatencyMs = 250;

    function classifyConnectionState(data) {
        if (!data || !data.reachable) return 'error';
        const latency = Number(data.latencyMs || 0);
        if (latency > slowLatencyMs) return 'slow';
        return 'ok';
    }

    function userFriendlyStatusText(data) {
        const state = classifyConnectionState(data);

        if (state === 'ok') {
            if (isAdmin) {
                if (data.latencyMs) return data.latencyMs + ' ms';
            }
            return 'Conectada';
        }

        if (state === 'slow') {
            if (isAdmin) {
                if (data.latencyMs) return 'Lenta: ' + data.latencyMs + ' ms';
            }
            return 'Conectada (lenta)';
        }

        if (isHostNotConfigured(data)) {
            return 'Sin host';
        }

        if (isAdmin) {
            return 'Error';
        }

        return 'Sin conexión';
    }

    function isHostNotConfigured(data) {
        const error = String(data?.error || data?.message || '').toLowerCase();
        return error.includes('sin host')
            || error.includes('host no configurado')
            || error.includes('host not configured')
            || error.includes('hostname no configurado');
    }

    function technicalStatusTitle(data) {
        if (!data) return '';
        if (data.error) return data.error;
        if (data.message) return data.message;
        if (data.transport) return data.transport;
        if (data.latencyMs) return 'Latencia: ' + data.latencyMs + ' ms';
        return '';
    }

    function applyConnectionBadgeClass(statusEl, data) {
        const state = classifyConnectionState(data);
        if (state === 'ok') {
            statusEl.className = 'ping-status badge badge-success';
            return;
        }
        if (state === 'slow') {
            statusEl.className = 'ping-status badge badge-warning';
            return;
        }
        statusEl.className = 'ping-status badge badge-danger';
    }

    function doNetConnection(id) {
        if (!id) return;
        const statusEl = document.querySelector('.ping-status[data-id="' + id + '"]');
        const portEl = document.querySelector('.ping-port[data-id="' + id + '"]');
        if (statusEl) statusEl.textContent = '...';
        if (portEl) portEl.textContent = '...';

        fetch('{{ url("/impresoras") }}/' + id + '/netconnection', {
            method: 'POST',
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json',
                'X-CSRF-TOKEN': csrf,
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: JSON.stringify({})
        })
        .then(r => r.json())
        .then(data => {
            if (statusEl) {
                statusEl.textContent = userFriendlyStatusText(data);
                statusEl.title = technicalStatusTitle(data);
                applyConnectionBadgeClass(statusEl, data);
            }
            if (portEl) {
                if (data && data.transport && typeof data.transport === 'string') {
                    const match = data.transport.match(/\/(\d+)$/);
                    portEl.textContent = match ? match[1] : data.transport;
                } else {
                    portEl.textContent = '-';
                }
            }
        })
        .catch(() => {
            if (statusEl) {
                statusEl.textContent = 'Error';
                statusEl.title = 'No se pudo comprobar la conectividad.';
                statusEl.className = 'ping-status badge badge-danger';
            }
            if (portEl) {
                portEl.textContent = '-';
            }
        });
    }

    function netConnectionAll() {
        document.querySelectorAll('[data-printer-id]').forEach(row => {
            const id = row.dataset.printerId;
            if (id) doNetConnection(id);
        });
    }

    if (pingInterval > 0) {
        netConnectionAll();
        setInterval(netConnectionAll, pingInterval);
    }
})();
</script>
@endsection
