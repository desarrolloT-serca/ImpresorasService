@extends('layouts.app')

@section('title', 'Impresoras')

@section('content')
@php
    $printersByStore = is_array($printersByStore ?? null) ? $printersByStore : [];
    $selectedStoreGroup = is_array($selectedStoreGroup ?? null) ? $selectedStoreGroup : null;
    $selectedStoreId = $selectedStoreGroup['storeId'] ?? ($selectedStoreId ?? null);
    $selectedStoreKey = $selectedStoreId !== null ? (string) $selectedStoreId : 'none';
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
                        $storeUrlParams = ['storeId' => $storeId];
                        $storeUrlParams = array_filter($storeUrlParams, static fn ($value) => $value !== null && $value !== '');
                        $printersCount = (int) ($storeGroup['printersCount'] ?? 0);
                        $activePrintersCount = (int) ($storeGroup['activePrintersCount'] ?? 0);
                        $errorPrintersCount = (int) ($storeGroup['errorPrintersCount'] ?? $storeGroup['connectionErrorCount'] ?? 0);
                        $uncheckedPrintersCount = (int) ($storeGroup['uncheckedPrintersCount'] ?? 0);
                        $visualStatus = (string) ($storeGroup['visualStatus'] ?? 'empty');
                        $visualStatusLabel = (string) ($storeGroup['visualStatusLabel'] ?? 'WARNING');
                        $printerWord = $printersCount === 1 ? 'impresora' : 'impresoras';
                    @endphp
                    <a href="{{ route('impresoras.index', $storeUrlParams) }}"
                       class="dbx-routing-store-link {{ $isSelected ? 'is-active' : '' }} is-printer-status-{{ $visualStatus }}">
                        <span class="dbx-routing-store-line">
                            <span class="dbx-routing-store-name">{{ $storeGroup['formattedStoreName'] ?? 'Sin tienda' }}</span>
                            <span class="dbx-store-health-chip is-{{ $visualStatus }}">{{ $visualStatusLabel }}</span>
                        </span>
                        <span class="dbx-routing-store-meta">
                            @if($printersCount === 0)
                                0 impresoras &middot; Sin configurar
                            @elseif($errorPrintersCount === 0 && $uncheckedPrintersCount === 0 && $activePrintersCount > 0)
                                {{ $printersCount }} {{ $printerWord }} &middot; OK
                            @else
                                {{ $printersCount }} {{ $printerWord }} &middot; {{ $activePrintersCount }} activas
                            @endif
                        </span>
                        @if($errorPrintersCount > 0)
                            <span class="dbx-routing-store-meta is-danger">{{ $errorPrintersCount }} con error</span>
                        @elseif($uncheckedPrintersCount > 0)
                            <span class="dbx-routing-store-meta is-warning">{{ $uncheckedPrintersCount }} sin comprobar</span>
                        @endif
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
                @if($isAdmin ?? false)
                    <a href="{{ $createUrl }}" class="btn btn-primary dbx-routing-create-btn" title="Crear impresora para esta tienda" aria-label="Crear impresora para esta tienda">+ Nueva impresora</a>
                @endif
            </div>
        </div>

        @if(!$selectedStoreGroup)
            <div class="dbx-empty-state">No hay tienda seleccionada.</div>
        @elseif($selectedStorePrintersCount === 0)
            <div class="dbx-empty-state">Esta tienda no tiene impresoras configuradas.</div>
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
                            $lastConnectionOkRaw = $p['lastConnectionOk'] ?? $p['LastConnectionOk'] ?? null;
                            $lastConnectionOk = ($lastConnectionOkRaw === null || $lastConnectionOkRaw === '')
                                ? null
                                : (is_bool($lastConnectionOkRaw) ? $lastConnectionOkRaw : filter_var($lastConnectionOkRaw, FILTER_VALIDATE_BOOLEAN, FILTER_NULL_ON_FAILURE));
                            $lastConnectionError = trim((string) ($p['lastConnectionError'] ?? $p['LastConnectionError'] ?? ''));
                            $lastConnectionCheckAtUtc = trim((string) ($p['lastConnectionCheckAtUtc'] ?? $p['LastConnectionCheckAtUtc'] ?? ''));
                            $normalizedError = strtolower($lastConnectionError);
                            $isHostNotConfigured = str_contains($normalizedError, 'sin host')
                                || str_contains($normalizedError, 'host no configurado')
                                || str_contains($normalizedError, 'host not configured')
                                || str_contains($normalizedError, 'hostname no configurado');
                            if ($isHostNotConfigured) {
                                $connectionLabel = 'Sin host';
                                $connectionClass = 'badge-warning';
                            } elseif ($lastConnectionCheckAtUtc === '') {
                                $connectionLabel = 'No comprobada';
                                $connectionClass = 'badge-neutral';
                            } elseif ($lastConnectionOk === true) {
                                $connectionLabel = 'OK';
                                $connectionClass = 'badge-success';
                            } elseif ($lastConnectionOk === false || $lastConnectionError !== '') {
                                $connectionLabel = ($isAdmin ?? false) ? 'Error' : html_entity_decode('Sin conexi&oacute;n', ENT_QUOTES, 'UTF-8');
                                $connectionClass = 'badge-danger';
                            } else {
                                $connectionLabel = 'No comprobada';
                                $connectionClass = 'badge-neutral';
                            }
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
                                <span class="ping-status badge {{ $connectionClass }}" data-id="{{ $id ?? '' }}" title="{{ $lastConnectionError }}">{{ $connectionLabel }}</span>
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

    function classifyConnectionState(data) {
        if (!data || !data.reachable) return 'error';
        return 'ok';
    }

    function userFriendlyStatusText(data) {
        const state = classifyConnectionState(data);

        if (state === 'ok') {
            return 'OK';
        }

        if (isHostNotConfigured(data)) {
            return 'Sin host';
        }

        if (isAdmin) {
            return 'Error';
        }

        return 'Sin conexi\u00f3n';
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
