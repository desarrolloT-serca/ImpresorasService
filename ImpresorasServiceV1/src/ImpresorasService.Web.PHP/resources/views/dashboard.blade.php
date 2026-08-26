@extends('layouts.app')

@section('title', 'Dashboard')

@php
    $window = $window ?? 'today';
    $tab = $tab ?? 'global';
    $storeView = request('storeView', 'estado');
    if ($tab === 'stores') {
        $tab = 'global';
        $storeView = 'estado';
    }
    if (!in_array($storeView, ['estado', 'detalle'], true)) {
        $storeView = 'estado';
    }
    $health = $health ?? 'all';
    $showHealthy = (bool) ($showHealthy ?? true);
    $storeSort = $storeSort ?? 'severity';
    $storeCols = (int) ($storeCols ?? 4);
    $autoRefreshSeconds = (int) ($autoRefreshSeconds ?? 0);
    $isAdminView = (bool) ($isAdminDashboard ?? ($isAdmin ?? false));
    $isAdminUser = (bool) ($isAdminUser ?? ($isAdmin ?? false));
    $thresholds = is_array($thresholds ?? null) ? $thresholds : [];
    $warningQueueMin = (int) ($thresholds['warningQueueMin'] ?? 10);
    $criticalQueueMin = (int) ($thresholds['criticalQueueMin'] ?? 30);
    $warningFailedMin = (int) ($thresholds['warningFailedWithoutRetryMin'] ?? 1);
    $criticalFailedMin = (int) ($thresholds['criticalFailedWithoutRetryMin'] ?? 5);
    $kpis = is_array($kpis ?? null) ? $kpis : [];
    $alerts = is_array($alerts ?? null) ? $alerts : [];
    $alertsVisibleRows = 10;
    $stores = is_array($stores ?? null) ? $stores : [];
    $formatStoreLabel = static fn ($storeId, $storeName = null): string => \App\Helpers\StoreFormat::label($storeId, $storeName);
    $printerConnectivityStatus = static function (array $printer): string {
        $status = strtolower(trim((string)($printer['connectivityStatus'] ?? $printer['ConnectivityStatus'] ?? '')));
        if (in_array($status, ['ok', 'no_host', 'unchecked', 'error', 'inactive'], true)) {
            return $status;
        }

        $connOk = $printer['lastConnectionOk'] ?? null;
        $connError = strtolower(trim((string)($printer['lastConnectionError'] ?? '')));
        $connCheckAt = trim((string)($printer['lastConnectionCheckAtUtc'] ?? ''));
        $connStreak = (int)($printer['connectionFailuresStreak'] ?? 0);
        if (str_contains($connError, 'host_not_configured')
            || str_contains($connError, 'sin host')
            || str_contains($connError, 'host no configurado')
            || str_contains($connError, 'host not configured')) {
            return 'no_host';
        }
        if ($connCheckAt === '') {
            return 'unchecked';
        }
        if ($connOk === true) {
            return 'ok';
        }
        if ($connOk === false && ($connStreak > 0 || $connError !== '')) {
            return 'error';
        }
        return 'unchecked';
    };
    $printerConnectivitySeverity = static function (array $printer) use ($printerConnectivityStatus): string {
        $severity = strtolower(trim((string)($printer['connectivitySeverity'] ?? $printer['ConnectivitySeverity'] ?? '')));
        if (in_array($severity, ['healthy', 'warning', 'critical', 'neutral'], true)) {
            return $severity;
        }
        return match ($printerConnectivityStatus($printer)) {
            'ok' => 'healthy',
            'error' => 'critical',
            'inactive' => 'neutral',
            default => 'warning',
        };
    };
    $printerConnectivityLabel = static function (array $printer) use ($printerConnectivityStatus, $isAdminUser): string {
        $status = $printerConnectivityStatus($printer);
        if ($status === 'error' && !$isAdminUser) {
            return 'Sin conexion';
        }

        $label = trim((string)($printer['connectivityLabel'] ?? $printer['ConnectivityLabel'] ?? ''));
        if ($label !== '') {
            return $label;
        }

        return match ($status) {
            'ok' => 'OK',
            'no_host' => 'Sin host',
            'error' => 'Error',
            'inactive' => 'Inactiva',
            default => 'No comprobada',
        };
    };
    $printerConnectivityClass = static function (array $printer) use ($printerConnectivitySeverity): string {
        return match ($printerConnectivitySeverity($printer)) {
            'healthy' => 'ok',
            'warning' => 'warn',
            'neutral' => 'muted',
            default => 'off',
        };
    };
    $printerConnectivityTitle = static function (array $printer): string {
        $detail = trim((string)($printer['connectivityDetail'] ?? $printer['ConnectivityDetail'] ?? ''));
        $error = trim((string)($printer['lastConnectionError'] ?? ''));
        $transport = trim((string)($printer['lastConnectionTransport'] ?? ''));
        $checkedAt = trim((string)($printer['lastConnectionCheckAtUtc'] ?? ''));
        $parts = [];
        if ($detail !== '') {
            $parts[] = 'Detalle: ' . $detail;
        } elseif ($error !== '') {
            $parts[] = 'Error: ' . $error;
        }
        $parts[] = 'Ultimo chequeo: ' . ($checkedAt !== '' ? $checkedAt : 'N/D');
        $parts[] = 'Transporte: ' . ($transport !== '' ? $transport : '-');
        return implode(' | ', $parts);
    };

    $windowLabel = match($window) {
        '7d' => 'Ultimos 7 dias',
        '30d' => 'Ultimos 30 dias',
        default => 'Hoy',
    };
@endphp

@section('topbar-filters')
    @include('dashboard.partials.filters')
@endsection

@section('content')

@if(!empty($partialData))
    <div style="margin-bottom:12px;padding:10px 14px;border:1px solid var(--ui-warning);border-radius:8px;background:color-mix(in srgb, var(--ui-warning) 12%, transparent);font-size:0.9rem;">
        Datos parciales: el resumen agregado no esta disponible; se muestran como maximo 500 trabajos recientes por tienda.
    </div>
@endif

<span id="autoRefreshCountdown" class="dbx-auto-refresh-status" hidden aria-hidden="true" title="Próximo refresco automático"></span>
<span id="dashboardLiveStatus" class="sr-only" aria-live="polite" aria-atomic="true"></span>

@fragment('dashboard-content')
<div class="dbx-dashboard-page" data-dashboard-dynamic>

<section class="dbx-wrap">
@include('dashboard.partials.tabs')


@if($tab === 'global')
    @include('dashboard.partials.operacion')
@endif

@if($isAdminUser)

    @php
        $storeViewParams = request()->except(['tab', 'storeView']);
        $resumenUrl = route('dashboard', array_merge($storeViewParams, ['tab' => 'global', 'storeView' => 'estado']));
    @endphp
    @if($storeView === 'detalle')
    <section class="dbx-card">
        <div class="dbx-card-body">
        <div class="dbx-title-row">
            <h2 class="dbx-title">Detalle compacto por tienda</h2>
            <a href="{{ $resumenUrl }}" class="dbx-subtle">&larr; Volver al resumen</a>
        </div>
        @if(count($stores) === 0)
            <p class="dbx-subtle">No hay tiendas para los filtros actuales.</p>
        @else
            <div class="dbx-store-grid cols-{{ $storeCols }}">
                @foreach($stores as $store)
                    @php
                        $healthClass = ($store['health'] ?? 'healthy') === 'critical'
                            ? 'critical'
                            : (($store['health'] ?? 'healthy') === 'warning' ? 'warning' : 'healthy');
                        $printerRows = is_array($store['printers'] ?? null) ? $store['printers'] : [];
                        $storeId = $store['storeId'] ?? null;
                        $storeIdText = (string)($storeId ?? '-');
                        $storeTitle = $formatStoreLabel($storeIdText, $store['storeName'] ?? 'Sin nombre');
                        $healthReason = (string)($store['healthReason'] ?? 'Operacion normal');
                        $queuedCurrent = (int)($store['queuedCurrent'] ?? 0);
                        $failedNoRetry = (int)($store['failedWithoutRetryCurrent'] ?? 0);
                        $unassigned = (int)($store['unassignedQueueCurrent'] ?? 0);
                        $connectedPrinters = (int)($store['connectedPrinters'] ?? 0);
                        $printerTotal = count($printerRows);
                        $hasConnectivityIssue = false;

                        foreach ($printerRows as $printerForReason) {
                            if (in_array($printerConnectivitySeverity($printerForReason), ['warning', 'critical'], true)) {
                                $hasConnectivityIssue = true;
                                break;
                            }
                        }

                        $reasonSource = strtolower(strip_tags(html_entity_decode($healthReason)));
                        if ($healthClass === 'healthy') {
                            $reasonShort = 'Operaci&oacute;n normal';
                            $actionTarget = 'none';
                        } elseif ($printerTotal === 0 || str_contains($reasonSource, 'sin impresora')) {
                            $reasonShort = 'Sin impresoras';
                            $actionTarget = 'printers';
                        } elseif ($hasConnectivityIssue || str_contains($reasonSource, 'host') || str_contains($reasonSource, 'unc') || str_contains($reasonSource, 'conect')) {
                            $reasonShort = 'Conectividad';
                            $actionTarget = 'printers';
                        } elseif ($failedNoRetry > 0 || str_contains($reasonSource, 'sin reenviar') || str_contains($reasonSource, 'fallo')) {
                            $reasonShort = 'Sin reenviar';
                            $actionTarget = 'queue';
                        } elseif ($unassigned > 0 || str_contains($reasonSource, 'sin asignar')) {
                            $reasonShort = 'Sin asignar';
                            $actionTarget = 'queue';
                        } elseif ($queuedCurrent > 0 || str_contains($reasonSource, 'cola')) {
                            $reasonShort = 'Cola acumulada';
                            $actionTarget = 'queue';
                        } else {
                            $reasonShort = 'Operaci&oacute;n normal';
                            $actionTarget = 'none';
                        }

                        $queueUrl = url('/cola') . ($storeId !== null ? ('?storeId=' . urlencode((string)$storeId)) : '');
                        $printersUrl = url('/impresoras') . ($storeId !== null ? ('?storeId=' . urlencode((string)$storeId)) : '');
                        $sortedPrinterRows = $printerRows;
                        usort($sortedPrinterRows, static function ($left, $right) use ($printerConnectivitySeverity) {
                            $severityWeight = static fn (array $printer): int => match ($printerConnectivitySeverity($printer)) {
                                'critical' => 100,
                                'warning' => 50,
                                default => 0,
                            };
                            $leftIssue = $severityWeight($left) + ((int)($left['queueCurrent'] ?? 0) > 0 ? 10 : 0) + (int)($left['queueCurrent'] ?? 0);
                            $rightIssue = $severityWeight($right) + ((int)($right['queueCurrent'] ?? 0) > 0 ? 10 : 0) + (int)($right['queueCurrent'] ?? 0);
                            return $rightIssue <=> $leftIssue;
                        });
                        $visiblePrinterRows = array_slice($healthClass === 'healthy' ? $printerRows : $sortedPrinterRows, 0, 3);
                        $hiddenPrinterCount = max(0, count($sortedPrinterRows) - count($visiblePrinterRows));
                    @endphp
                    <article id="detalle-tienda-{{ $storeIdText }}" class="dbx-store dbx-diagnostic-card dbx-diagnostic-{{ $healthClass }}">
                        <div class="dbx-store-head dbx-diagnostic-head">
                            <div>
                                <h3 class="dbx-store-title">{{ $storeTitle }}</h3>
                                <p class="dbx-store-reason dbx-diagnostic-reason" title="{{ $healthReason }}">{!! $reasonShort !!}</p>
                            </div>
                            <span class="dbx-pill {{ $healthClass }}">{{ strtoupper($store['health'] ?? 'healthy') }}</span>
                        </div>
                        <div class="dbx-diagnostic-body">
                            <div class="dbx-diagnostic-kpi-strip" aria-label="Indicadores operativos actuales">
                                <div class="dbx-kpi-cell {{ $queuedCurrent > 0 ? 'is-warning' : 'is-neutral' }}">
                                    <span>Cola</span>
                                    <strong>{{ $queuedCurrent }}</strong>
                                </div>
                                <div class="dbx-kpi-cell {{ $failedNoRetry > 0 ? 'is-danger' : 'is-neutral' }}">
                                    <span>Sin reenviar</span>
                                    <strong>{{ $failedNoRetry }}</strong>
                                </div>
                                <div class="dbx-kpi-cell {{ $unassigned > 0 ? 'is-warning' : 'is-neutral' }}">
                                    <span>Sin asignar</span>
                                    <strong>{{ $unassigned }}</strong>
                                </div>
                            </div>
                        </div>
                        <div class="dbx-printers dbx-diagnostic-printers">
                            @if(count($printerRows) === 0)
                                <div class="dbx-printer-summary dbx-subtle">Sin impresoras activas en esta tienda</div>
                            @else
                                @foreach($visiblePrinterRows as $printer)
                                    <div class="dbx-printer-row dbx-diagnostic-printer-row">
                                        <span title="{{ $printer['spoolQueue'] ?? '-' }}">{{ $printer['printerName'] ?? 'Impresora' }}</span>
                                        @php
                                            $printerQueue = (int)($printer['queueCurrent'] ?? 0);
                                            $showPrinterQueue = count($printerRows) > 1 && $printerQueue > 0;
                                            $connClass = $printerConnectivityClass($printer);
                                            $connText = $printerConnectivityLabel($printer);
                                            $connTitle = $printerConnectivityTitle($printer);
                                        @endphp
                                        <span class="dbx-printer-state {{ $connClass }}" title="{{ $connTitle }}">
                                            {{ $connText }}
                                        </span>
                                        <span class="dbx-printer-queue-slot">
                                            @if($showPrinterQueue)
                                                <span class="dbx-printer-queue-chip">Cola {{ $printerQueue }}</span>
                                            @endif
                                        </span>
                                    </div>
                                @endforeach
                                @if($hiddenPrinterCount > 0)
                                    <div class="dbx-printer-more">+{{ $hiddenPrinterCount }} m&aacute;s</div>
                                @endif
                            @endif
                        </div>
                        <div class="dbx-diagnostic-actions">
                            <a href="{{ $queueUrl }}" class="btn {{ $actionTarget === 'queue' ? 'btn-primary' : 'btn-ghost' }}">Ver cola</a>
                            <a href="{{ $printersUrl }}" class="btn {{ $actionTarget === 'printers' ? 'btn-primary' : 'btn-ghost' }}">Ver impresoras</a>
                        </div>
                    </article>
                @endforeach
            </div>
        @endif
        </div>
    </section>
    @endif
@else
    <section class="dbx-card">
        <div class="dbx-card-body">
        <div class="dbx-title-row">
            <h2 class="dbx-title">
                @if($isAdminView)
                    Tiendas e impresi&oacute;n operativa
                @else
                    Detalle completo de mi tienda
                @endif
            </h2>
        </div>
        <div class="dbx-store-grid cols-{{ $storeCols }}">
            @forelse($stores as $store)
                @php
                    $status = $store['health'] ?? 'healthy';
                    $healthClass = $status === 'critical'
                        ? 'critical'
                        : ($status === 'warning' ? 'warning' : 'healthy');
                    $printerRows = is_array($store['printers'] ?? null) ? $store['printers'] : [];
                    $storeIdText = (string)($store['storeId'] ?? '-');
                    $storeTitle = $formatStoreLabel($storeIdText, $store['storeName'] ?? 'Sin nombre');
                @endphp
                <article class="dbx-store">
                    <div class="dbx-store-head">
                        <div>
                            <h3 class="dbx-store-title">{{ $storeTitle }}</h3>
                            <p class="dbx-store-reason">{{ $store['healthReason'] ?? '-' }}</p>
                        </div>
                        <span class="dbx-pill {{ $healthClass }}">{{ strtoupper($status) }}</span>
                    </div>
                    <div class="dbx-mini-kpis">
                        <div><span>Recibidos</span><strong>{{ $store['received'] ?? 0 }}</strong></div>
                        <div><span>Impresos</span><strong>{{ $store['printed'] ?? 0 }}</strong></div>
                        <div><span>Fallidos</span><strong>{{ $store['failed'] ?? 0 }}</strong></div>
                        <div><span>Cola</span><strong>{{ $store['queuedCurrent'] ?? 0 }}</strong></div>
                        <div><span>Sin reenviar</span><strong>{{ $store['failedWithoutRetryCurrent'] ?? 0 }}</strong></div>
                        <div><span>Sin asignar</span><strong>{{ $store['unassignedQueueCurrent'] ?? 0 }}</strong></div>
                    </div>
                    <div class="dbx-printers">
                        <div class="dbx-printers-head">
                            <span>Impresora</span>
                            <span>Estado</span>
                            <span>Cola</span>
                        </div>
                        @if(count($printerRows) === 0)
                            <div class="dbx-printer-row"><span class="dbx-subtle">Sin impresoras activas en esta tienda</span><span></span><span></span></div>
                        @else
                            @foreach($printerRows as $printer)
                                <div class="dbx-printer-row">
                                    <span title="{{ $printer['spoolQueue'] ?? '-' }}">{{ $printer['printerName'] ?? 'Impresora' }}</span>
                                    @php
                                        $connClass = $printerConnectivityClass($printer);
                                        $connText = $printerConnectivityLabel($printer);
                                        $connTitle = $printerConnectivityTitle($printer);
                                    @endphp
                                    <span class="dbx-printer-state {{ $connClass }}" title="{{ $connTitle }}">
                                        {{ $connText }}
                                    </span>
                                    <span>{{ $printer['queueCurrent'] ?? 0 }}</span>
                                </div>
                            @endforeach
                        @endif
                    </div>
                </article>
            @empty
                <p class="muted-text">No hay tiendas para los filtros seleccionados.</p>
            @endforelse
        </div>
        </div>
    </section>
@endif
</section>
</div>
@endfragment

<script>
(function() {
    const STORAGE_KEY = 'dashboard.filters.v1';

    function readStoredPrefs() {
        try {
            const raw = localStorage.getItem(STORAGE_KEY);
            if (!raw) return null;
            const parsed = JSON.parse(raw);
            return parsed && typeof parsed === 'object' ? parsed : null;
        } catch {
            return null;
        }
    }

    function writeStoredPrefs(prefs) {
        try {
            localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs));
        } catch {}
    }

    function clearStoredPrefs() {
        try { localStorage.removeItem(STORAGE_KEY); } catch {}
    }

    function getCurrentPrefsFromDom() {
        const form = document.querySelector('form.filters-row');
        if (!form) return null;

        const prefs = {};
        const windowEl = document.getElementById('window');
        if (windowEl) prefs.window = windowEl.value;

        const tabEl = form.querySelector('input[name="tab"]');
        if (tabEl && tabEl.value) prefs.tab = tabEl.value;

        const storeViewEl = form.querySelector('input[name="storeView"]');
        if (storeViewEl && storeViewEl.value) prefs.storeView = storeViewEl.value;

        const showHealthyEl = form.querySelector('input[type="checkbox"][name="showHealthy"]');
        if (showHealthyEl) prefs.showHealthy = showHealthyEl.checked ? '1' : '0';

        return prefs;
    }

    function syncControlsWithServerRender() {
        document.querySelectorAll('[data-current]').forEach(function(el) {
            const value = el.getAttribute('data-current');
            if (value !== null) el.value = value;
        });
    }

    document.addEventListener('DOMContentLoaded', function() {
        const form = document.querySelector('form.filters-row');
        const clearLink = document.getElementById('dashboard-clear-filters');
        syncControlsWithServerRender();

        if (clearLink) {
            clearLink.addEventListener('click', function() {
                clearStoredPrefs();
            });
        }

        if (form) {
            form.addEventListener('submit', function() {
                const prefs = getCurrentPrefsFromDom();
                if (prefs) writeStoredPrefs(prefs);
            });
        }
    });
})();

(function() {
    let dashboardAbortController = null;
    let dashboardRequestId = 0;
    // Fijo, ya no configurable desde la interfaz.
    const AUTO_REFRESH_SECONDS = 30;
    let autoRefreshTimer = null;
    let autoRefreshCountdownTimer = null;
    let autoRefreshSecondsRemaining = 0;

    function getAutoRefreshCountdownEl() {
        return document.getElementById('autoRefreshCountdown');
    }

    function updateAutoRefreshIndicator(remaining, loading) {
        const el = getAutoRefreshCountdownEl();
        if (!el) return;

        el.classList.remove('is-active', 'is-loading');

        if (loading) {
            el.hidden = false;
            el.classList.add('is-loading');
            el.textContent = '↻ …';
            el.title = 'Actualizando dashboard';
            return;
        }

        const seconds = Number(remaining) || 0;
        if (seconds > 0) {
            el.hidden = false;
            el.classList.add('is-active');
            el.textContent = '↻ ' + seconds + 's';
            el.title = 'Próximo refresco automático en ' + seconds + ' segundos';
            return;
        }

        el.hidden = true;
        el.textContent = '';
        el.title = 'Próximo refresco automático';
    }

    function stopAutoRefreshCountdown() {
        if (autoRefreshCountdownTimer) {
            window.clearInterval(autoRefreshCountdownTimer);
            autoRefreshCountdownTimer = null;
        }
    }

    function scheduleAutoRefresh(seconds) {
        if (autoRefreshTimer) {
            window.clearTimeout(autoRefreshTimer);
            autoRefreshTimer = null;
        }
        stopAutoRefreshCountdown();

        const interval = Number(seconds) || 0;
        if (interval <= 0) {
            updateAutoRefreshIndicator(0, false);
            return;
        }

        autoRefreshSecondsRemaining = interval;
        updateAutoRefreshIndicator(autoRefreshSecondsRemaining, false);

        autoRefreshCountdownTimer = window.setInterval(function() {
            autoRefreshSecondsRemaining -= 1;
            if (autoRefreshSecondsRemaining <= 0) {
                stopAutoRefreshCountdown();
                updateAutoRefreshIndicator(0, true);
                return;
            }
            updateAutoRefreshIndicator(autoRefreshSecondsRemaining, false);
        }, 1000);

        autoRefreshTimer = window.setTimeout(function() {
            stopAutoRefreshCountdown();
            loadDashboard(window.location.href, false);
        }, interval * 1000);
    }

    function getDashboardRoot() {
        return document.querySelector('[data-dashboard-dynamic]');
    }

    function announceDashboardUpdated() {
        const el = document.getElementById('dashboardLiveStatus');
        if (!el) return;
        el.textContent = 'Dashboard actualizado, ' + new Date().toLocaleTimeString();
    }

    function prepareDynamicDashboard(root) {
        // El formulario esta en la barra superior, fuera del root que se reemplaza en cada refresco.
        const form = document.querySelector('form[data-dashboard-filter-form]');
        if (form) {
            form.dataset.dynamicOwned = '1';
        }

        // La grafica se re-renderiza en la serie por defecto: se le devuelve la que estaba elegida.
        if (window.dbxRestoreChartSerie) {
            window.dbxRestoreChartSerie(root);
        }

        scheduleAutoRefresh(AUTO_REFRESH_SECONDS);
    }

    function showDashboardError(root, url) {
        const previous = root.querySelector('.dynamic-panel-error');
        if (previous) previous.remove();

        const error = document.createElement('div');
        error.className = 'dynamic-panel-error';
        error.innerHTML = 'No se pudo actualizar el dashboard. <a class="btn btn-ghost btn-sm" href="' + url + '">Abrir vista completa</a>';
        root.prepend(error);
    }

    function collectFormUrl(form) {
        const formData = new FormData(form);
        const url = new URL(form.action || window.location.href, window.location.href);
        url.search = '';

        // set y no append: showHealthy va como hidden 0 + checkbox 1 para poder enviar el "no", y
        // con append la URL acumulaba showHealthy=0&showHealthy=1 en cada navegacion.
        formData.forEach(function(value, key) {
            if (value !== null && value !== '') {
                url.searchParams.set(key, value);
            }
        });

        return url.toString();
    }

    function writeDynamicPrefs(form) {
        try {
            const prefs = {};
            new FormData(form).forEach(function(value, key) {
                prefs[key] = value;
            });
            localStorage.setItem('dashboard.filters.v1', JSON.stringify(prefs));
        } catch {}
    }

    async function loadDashboard(url, pushState) {
        const currentRoot = getDashboardRoot();
        if (!currentRoot || !window.fetch || !window.DOMParser) {
            window.location.href = url;
            return;
        }

        if (dashboardAbortController) {
            dashboardAbortController.abort();
        }

        dashboardAbortController = new AbortController();
        const requestId = ++dashboardRequestId;
        currentRoot.classList.add('is-dynamic-loading');
        currentRoot.setAttribute('aria-busy', 'true');
        stopAutoRefreshCountdown();
        updateAutoRefreshIndicator(0, true);

        try {
            const response = await fetch(url, {
                headers: {
                    'Accept': 'text/html',
                    'X-Requested-With': 'XMLHttpRequest'
                },
                signal: dashboardAbortController.signal
            });

            if (!response.ok) {
                throw new Error('HTTP ' + response.status);
            }

            const html = await response.text();
            if (requestId !== dashboardRequestId) return;

            const doc = new DOMParser().parseFromString(html, 'text/html');
            const nextRoot = doc.querySelector('[data-dashboard-dynamic]');

            if (!nextRoot) {
                throw new Error('Dashboard no encontrado');
            }

            currentRoot.replaceWith(nextRoot);
            prepareDynamicDashboard(nextRoot);

            if (pushState) {
                history.pushState({ dynamicPanel: 'dashboard' }, '', url);
            }

            announceDashboardUpdated();
        } catch (error) {
            if (error.name === 'AbortError') return;
            const root = getDashboardRoot();
            if (root) {
                root.classList.remove('is-dynamic-loading');
                root.removeAttribute('aria-busy');
                showDashboardError(root, url);
                scheduleAutoRefresh(AUTO_REFRESH_SECONDS);
            }
        }
    }

    document.addEventListener('click', function(event) {
        const link = event.target.closest('a.dbx-store-view-tab');
        if (!link || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;

        event.preventDefault();
        loadDashboard(link.href, true);
    });

    document.addEventListener('submit', function(event) {
        const form = event.target.closest('form[data-dashboard-filter-form]');
        if (!form) return;

        event.preventDefault();
        writeDynamicPrefs(form);
        loadDashboard(collectFormUrl(form), true);
    });

    document.addEventListener('change', function(event) {
        const form = event.target.closest('form[data-dashboard-filter-form][data-dynamic-owned="1"]');
        const field = event.target.closest('select, input[type="checkbox"], input[type="radio"]');
        if (!form || !field) return;

        if (typeof form.requestSubmit === 'function') {
            form.requestSubmit();
        } else {
            loadDashboard(collectFormUrl(form), true);
        }
    });

    window.addEventListener('popstate', function() {
        if (window.location.pathname === '/' || window.location.pathname === '') {
            loadDashboard(window.location.href, false);
        }
    });

    window.DashboardDynamic = {
        refresh: function() {
            return loadDashboard(window.location.href, false);
        },
        scheduleAutoRefresh: scheduleAutoRefresh
    };

    const initialDashboardRoot = getDashboardRoot();
    if (initialDashboardRoot) {
        prepareDynamicDashboard(initialDashboardRoot);
    }
})();
</script>
@endsection
