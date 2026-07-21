@extends('layouts.app')

@section('title', 'Impresoras')

@section('content')
@php
    $printersByStore = is_array($printersByStore ?? null) ? $printersByStore : [];
    $selectedStoreGroup = is_array($selectedStoreGroup ?? null) ? $selectedStoreGroup : null;
    $pageStoreId = $selectedStoreGroup['storeId'] ?? ($selectedStoreId ?? null);
    $selectedStoreKey = $pageStoreId !== null ? (string) $pageStoreId : 'none';
    $selectedStorePrintersCount = is_array($selectedStoreGroup['printers'] ?? null) ? count($selectedStoreGroup['printers']) : 0;
    $createUrl = $pageStoreId !== null
        ? route('impresoras.create', ['storeId' => $pageStoreId])
        : route('impresoras.create');
@endphp

<div class="dbx-wrap">
@fragment('routing-layout')
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
                        $warningPrintersCount = (int) ($storeGroup['warningPrintersCount'] ?? $storeGroup['uncheckedPrintersCount'] ?? 0);
                        $visualStatus = (string) ($storeGroup['visualStatus'] ?? 'empty');
                        $visualStatusLabel = (string) ($storeGroup['visualStatusLabel'] ?? 'WARNING');
                        $printerWord = $printersCount === 1 ? 'impresora' : 'impresoras';
                    @endphp
                    <a href="{{ route('impresoras.index', $storeUrlParams) }}"
                       data-dynamic-store-link
                       data-dynamic-target="impresoras"
                       class="dbx-routing-store-link {{ $isSelected ? 'is-active' : '' }} is-printer-status-{{ $visualStatus }}">
                        <span class="dbx-routing-store-line">
                            <span class="dbx-routing-store-name">{{ $storeGroup['formattedStoreName'] ?? 'Sin tienda' }}</span>
                            <span class="dbx-store-health-chip is-{{ $visualStatus }}">{{ $visualStatusLabel }}</span>
                        </span>
                        <span class="dbx-routing-store-meta">
                            @if($printersCount === 0)
                                0 impresoras &middot; Sin configurar
                            @elseif($errorPrintersCount === 0 && $warningPrintersCount === 0 && $activePrintersCount > 0)
                                {{ $printersCount }} {{ $printerWord }} &middot; OK
                            @else
                                {{ $printersCount }} {{ $printerWord }} &middot; {{ $activePrintersCount }} activas
                            @endif
                        </span>
                        @if($errorPrintersCount > 0)
                            <span class="dbx-routing-store-meta is-danger">{{ $errorPrintersCount }} con error</span>
                        @elseif($warningPrintersCount > 0)
                            <span class="dbx-routing-store-meta is-warning">{{ $warningPrintersCount }} pendientes</span>
                        @endif
                    </a>
                @endforeach
            </div>
        @endif
    </x-ui.card>

    <x-ui.card class="dbx-routing-rules-card" data-dynamic-panel="impresoras" aria-live="polite">
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
                    <a href="{{ $createUrl }}" class="btn btn-primary btn-icon btn-action-icon dbx-routing-create-btn" title="Crear impresora para esta tienda" aria-label="Crear impresora para esta tienda">
                        <x-ui.action-icon name="plus" label="Crear impresora para esta tienda" />
                    </a>
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
                        <th class="status-col">IPP</th>
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
                            $editUrl = $id ? route('impresoras.edit', ['impresora' => $id, 'storeId' => $pageStoreId]) : '#';
                            $connectivityStatus = strtolower(trim((string) ($p['connectivityStatus'] ?? $p['ConnectivityStatus'] ?? '')));
                            $connectivitySeverity = strtolower(trim((string) ($p['connectivitySeverity'] ?? $p['ConnectivitySeverity'] ?? '')));
                            $connectionLabel = trim((string) ($p['connectivityLabel'] ?? $p['ConnectivityLabel'] ?? ''));
                            $connectionTitle = trim((string) ($p['connectivityDetail'] ?? $p['ConnectivityDetail'] ?? ''));
                            $lastConnectionError = trim((string) ($p['lastConnectionError'] ?? $p['LastConnectionError'] ?? ''));
                            $lastConnectionOkRaw = $p['lastConnectionOk'] ?? $p['LastConnectionOk'] ?? null;
                            $lastConnectionOk = ($lastConnectionOkRaw === null || $lastConnectionOkRaw === '')
                                ? null
                                : (is_bool($lastConnectionOkRaw) ? $lastConnectionOkRaw : filter_var($lastConnectionOkRaw, FILTER_VALIDATE_BOOLEAN, FILTER_NULL_ON_FAILURE));
                            $lastConnectionCheckAtUtc = trim((string) ($p['lastConnectionCheckAtUtc'] ?? $p['LastConnectionCheckAtUtc'] ?? ''));
                            $lastConnectionTransport = trim((string) ($p['lastConnectionTransport'] ?? $p['LastConnectionTransport'] ?? ''));
                            $normalizedError = strtolower($connectionTitle !== '' ? $connectionTitle : $lastConnectionError);
                            $isHostNotConfigured = str_contains($normalizedError, 'sin host')
                                || str_contains($normalizedError, 'host no configurado')
                                || str_contains($normalizedError, 'host not configured')
                                || str_contains($normalizedError, 'hostname no configurado')
                                || trim($normalizedError) === 'host_not_configured';
                            if ($connectivityStatus === 'error' && !($isAdmin ?? false)) {
                                $connectionLabel = html_entity_decode('Sin conexi&oacute;n', ENT_QUOTES, 'UTF-8');
                            }
                            if ($connectionLabel === '') {
                                $connectionLabel = match ($connectivityStatus) {
                                    'ok' => 'OK',
                                    'no_host' => 'Sin host',
                                    'inactive' => 'Inactiva',
                                    'error' => ($isAdmin ?? false) ? 'Error' : html_entity_decode('Sin conexi&oacute;n', ENT_QUOTES, 'UTF-8'),
                                    default => 'No comprobada',
                                };
                            }
                            if ($connectivitySeverity === '') {
                                if ($isHostNotConfigured) {
                                    $connectivitySeverity = 'warning';
                                } elseif ($lastConnectionCheckAtUtc === '') {
                                    $connectivitySeverity = 'warning';
                                } elseif ($lastConnectionOk === true) {
                                    $connectivitySeverity = 'healthy';
                                } elseif ($lastConnectionOk === false || $lastConnectionError !== '') {
                                    $connectivitySeverity = 'critical';
                                } else {
                                    $connectivitySeverity = 'warning';
                                }
                            }
                            if ($connectivityStatus === '' && $isHostNotConfigured) {
                                $connectivityStatus = 'no_host';
                            }
                            if ($connectionTitle === '') {
                                $connectionTitle = $lastConnectionError;
                            }
                            if ($connectionTitle === '' && $lastConnectionCheckAtUtc !== '') {
                                $connectionTitle = 'Ultimo chequeo: ' . $lastConnectionCheckAtUtc;
                            }
                            $connectionClass = match ($connectivitySeverity) {
                                'healthy' => 'badge-success',
                                'critical' => 'badge-danger',
                                'neutral' => 'badge-neutral',
                                default => 'badge-warning',
                            };
                            if ($isHostNotConfigured) {
                                $connectionLabel = 'Sin host';
                            }
                            $initialPort = '-';
                            if ($lastConnectionTransport !== '' && preg_match('/\/(\d+)$/', $lastConnectionTransport, $matches)) {
                                $initialPort = $matches[1];
                            } elseif ($lastConnectionTransport !== '') {
                                $initialPort = $lastConnectionTransport;
                            }
                            $ippRaw = $p['ippSupported'] ?? $p['IppSupported'] ?? null;
                            $ippSupported = ($ippRaw === null || $ippRaw === '') ? null : filter_var($ippRaw, FILTER_VALIDATE_BOOLEAN, FILTER_NULL_ON_FAILURE);
                        @endphp
                        <tr data-printer-id="{{ $id ?? '' }}">
                            <td class="text-col">{{ $name }}</td>
                            <td class="text-col dbx-printer-text-cell" title="{{ $spoolQueue }}">{{ $spoolQueue }}</td>
                            <td class="text-col dbx-printer-text-cell" title="{{ $host !== '' ? $host : 'Sin host configurado' }}">{{ $host !== '' ? $host : '-' }}</td>
                            <td class="status-col">
                                <span class="badge status-chip {{ $isActive ? 'badge-success' : 'badge-danger' }}" aria-label="{{ $isActive ? 'Impresora activa' : 'Impresora inactiva' }}">
                                    {{ $isActive ? 'Sí' : 'No' }}
                                </span>
                            </td>
                            <td class="status-col">
                                <span class="ping-status badge {{ $connectionClass }}" data-id="{{ $id ?? '' }}" title="{{ $connectionTitle }}">{{ $connectionLabel }}</span>
                            </td>
                            <td class="status-col">
                                <span class="ping-port badge badge-neutral" data-id="{{ $id ?? '' }}">{{ $initialPort }}</span>
                            </td>
                            <td class="status-col">
                                @if($ippSupported === true)
                                    <span class="badge badge-success" title="Soporta IPP">IPP ✓</span>
                                @elseif($ippSupported === false)
                                    <span class="badge badge-neutral" title="No soporta IPP">IPP ✗</span>
                                @else
                                    <span class="badge badge-warning" title="Compatibilidad IPP sin comprobar">IPP ?</span>
                                @endif
                            </td>
                            <td class="actions-col">
                                @if(($isAdmin ?? false) && $id)
                                    <x-ui.action-buttons>
                                        <a href="{{ $editUrl }}" class="btn btn-ghost btn-icon btn-action-icon" aria-label="Editar impresora" title="Editar impresora">
                                            <x-ui.action-icon name="edit" label="Editar impresora" />
                                        </a>
                                        <form action="{{ route('impresoras.destroy', $id) }}" method="POST" onsubmit="return confirm('&iquest;Eliminar?')">
                                            @csrf
                                            @method('DELETE')
                                            @if($pageStoreId !== null)
                                                <input type="hidden" name="storeId" value="{{ $pageStoreId }}">
                                            @endif
                                            <button type="submit" class="btn btn-danger btn-icon btn-action-icon" aria-label="Eliminar impresora" title="Eliminar impresora">
                                                <x-ui.action-icon name="trash" label="Eliminar impresora" />
                                            </button>
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
@endfragment
</div>

<script>
(function() {
    const csrf = document.querySelector('meta[name="csrf-token"]')?.content || '';
    const pingInterval = {{ $pingIntervalSeconds ?? 30 }} * 1000;
    const isAdmin = {{ ($isAdmin ?? false) ? 'true' : 'false' }};

    function classifyConnectionState(data) {
        const status = String(data?.connectivityStatus || '').toLowerCase();
        if (status) return status;
        if (!data || !data.reachable) return 'error';
        return 'ok';
    }

    function connectionSeverity(data) {
        const severity = String(data?.connectivitySeverity || '').toLowerCase();
        if (severity) return severity;
        const state = classifyConnectionState(data);
        if (state === 'ok') return 'healthy';
        if (state === 'inactive') return 'neutral';
        if (state === 'no_host' || state === 'unchecked') return 'warning';
        return 'critical';
    }

    function userFriendlyStatusText(data) {
        const state = classifyConnectionState(data);

        if (state === 'error' && !isAdmin) {
            return 'Sin conexi\u00f3n';
        }

        if (data?.connectivityLabel) {
            return String(data.connectivityLabel);
        }

        if (state === 'ok') {
            return 'OK';
        }

        if (state === 'no_host' || isHostNotConfigured(data)) {
            return 'Sin host';
        }

        if (state === 'unchecked') {
            return 'No comprobada';
        }

        if (state === 'inactive') {
            return 'Inactiva';
        }

        if (isAdmin) {
            return 'Error';
        }

        return 'Sin conexi\u00f3n';
    }

    function isHostNotConfigured(data) {
        const error = String(data?.error || data?.detail || data?.message || '').toLowerCase();
        return error.includes('sin host')
            || error.includes('host no configurado')
            || error.includes('host not configured')
            || error.includes('hostname no configurado')
            || error.trim() === 'host_not_configured';
    }

    function technicalStatusTitle(data) {
        if (!data) return '';
        if (data.detail) return data.detail;
        if (data.error) return data.error;
        if (data.message) return data.message;
        if (data.transport) return data.transport;
        if (data.latencyMs) return 'Latencia: ' + data.latencyMs + ' ms';
        return '';
    }

    function applyConnectionBadgeClass(statusEl, data) {
        const severity = connectionSeverity(data);
        if (severity === 'healthy') {
            statusEl.className = 'ping-status badge badge-success';
            return;
        }
        if (severity === 'warning') {
            statusEl.className = 'ping-status badge badge-warning';
            return;
        }
        if (severity === 'neutral') {
            statusEl.className = 'ping-status badge badge-neutral';
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

        fetch('{{ url("/impresoras") }}/' + id + '/netconnection?persist=true', {
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
        const ids = Array.from(document.querySelectorAll('[data-printer-id]'))
            .map(row => row.dataset.printerId)
            .filter(Boolean);
        const batchSize = 4;
        let index = 0;

        function runBatch() {
            ids.slice(index, index + batchSize).forEach(doNetConnection);
            index += batchSize;

            if (index < ids.length) {
                window.setTimeout(runBatch, 120);
            }
        }

        runBatch();
    }

    if (pingInterval > 0) {
        netConnectionAll();
        setInterval(netConnectionAll, pingInterval);
    }

    let panelAbortController = null;
    let panelRequestId = 0;

    function getDynamicPanel() {
        return document.querySelector('[data-dynamic-panel="impresoras"]');
    }

    function showPanelError(panel, url) {
        const previous = panel.querySelector('.dynamic-panel-error');
        if (previous) previous.remove();

        const error = document.createElement('div');
        error.className = 'dynamic-panel-error';
        error.innerHTML = 'No se pudo actualizar el panel. <a class="btn btn-ghost btn-sm" href="' + url + '">Abrir vista completa</a>';
        panel.prepend(error);
    }

    async function loadStorePanel(url, pushState) {
        const currentPanel = getDynamicPanel();
        if (!currentPanel || !window.fetch || !window.DOMParser) {
            window.location.href = url;
            return;
        }

        if (panelAbortController) {
            panelAbortController.abort();
        }

        panelAbortController = new AbortController();
        const requestId = ++panelRequestId;
        currentPanel.classList.add('is-dynamic-loading');
        currentPanel.setAttribute('aria-busy', 'true');

        try {
            const response = await fetch(url, {
                headers: {
                    'Accept': 'text/html',
                    'X-Requested-With': 'XMLHttpRequest'
                },
                signal: panelAbortController.signal
            });

            if (!response.ok) {
                throw new Error('HTTP ' + response.status);
            }

            const html = await response.text();
            if (requestId !== panelRequestId) return;

            const doc = new DOMParser().parseFromString(html, 'text/html');
            const nextPanel = doc.querySelector('[data-dynamic-panel="impresoras"]');
            const nextStores = doc.querySelector('.dbx-routing-store-list');
            const currentStores = document.querySelector('.dbx-routing-store-list');

            if (!nextPanel) {
                throw new Error('Panel no encontrado');
            }

            currentPanel.replaceWith(nextPanel);
            if (nextStores && currentStores) {
                currentStores.replaceWith(nextStores);
            }

            if (pushState) {
                history.pushState({ dynamicPanel: 'impresoras' }, '', url);
            }

            netConnectionAll();
        } catch (error) {
            if (error.name === 'AbortError') return;
            const panel = getDynamicPanel();
            if (panel) {
                panel.classList.remove('is-dynamic-loading');
                panel.removeAttribute('aria-busy');
                showPanelError(panel, url);
            }
        }
    }

    document.addEventListener('click', function(event) {
        const link = event.target.closest('a[data-dynamic-store-link][data-dynamic-target="impresoras"]');
        if (!link || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;

        event.preventDefault();
        loadStorePanel(link.href, true);
    });

    window.addEventListener('popstate', function() {
        if (window.location.pathname.includes('/impresoras')) {
            loadStorePanel(window.location.href, false);
        }
    });
})();
</script>
@endsection
