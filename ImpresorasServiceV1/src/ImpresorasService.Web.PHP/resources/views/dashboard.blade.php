@extends('layouts.app')

@section('title', 'Dashboard')

@section('content')
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

<span id="autoRefreshCountdown" class="dbx-auto-refresh-status" hidden aria-live="polite" title="Próximo refresco automático"></span>

@fragment('dashboard-content')
<div class="dbx-dashboard-page" data-dashboard-dynamic aria-live="polite">
@include('dashboard.partials.filters')

<section class="dbx-wrap">
@include('dashboard.partials.tabs')

@php
    $receivedVal = (int) ($kpis['received'] ?? 0);
    $printedVal = (int) ($kpis['printed'] ?? 0);
    $failedVal = (int) ($kpis['failed'] ?? 0);
    $queueVal = (int) ($kpis['queueCurrent'] ?? 0);
    $failedNoRetryVal = (int) ($kpis['failedWithoutRetryCurrent'] ?? 0);
    $hasNoPrintActivity = $receivedVal === 0 && $queueVal === 0 && $failedVal === 0;
    $base = max(1, $receivedVal, $printedVal, $failedVal, $queueVal, $failedNoRetryVal);
    $totalPeriod = max(1, $receivedVal);
    $receivedPct = $totalPeriod > 0 ? 100 : 0;
    $printedPct = $totalPeriod > 0 ? (int) round(($printedVal / $totalPeriod) * 100) : 0;
    $failedPct = $totalPeriod > 0 ? (int) round(($failedVal / $totalPeriod) * 100) : 0;
    $queuePct = $totalPeriod > 0 ? (int) round(($queueVal / $totalPeriod) * 100) : 0;
    $failedNoRetryPct = $totalPeriod > 0 ? (int) round(($failedNoRetryVal / $totalPeriod) * 100) : 0;
    $successRate = $totalPeriod > 0 ? $printedPct : null;
    $summary = is_array($summary ?? null) ? $summary : [];
    $storesTotalSummary = (int) ($summary['storesTotal'] ?? 0);
    $storesActiveSummary = (int) ($summary['storesActive'] ?? 0);
    $printersTotalSummary = (int) ($summary['printersTotal'] ?? 0);
    $printersActiveSummary = (int) ($summary['printersActive'] ?? 0);
    $usersTotalSummary = (int) ($summary['usersTotal'] ?? 0);
    $storesList = is_array($stores ?? null) ? $stores : [];
    $storesTotal = max(1, count($storesList));
    $healthyCount = 0;
    $warningCount = 0;
    $criticalCount = 0;
    $totalQueueAllStores = 0;
    $totalFailedNoRetryAllStores = 0;
    $totalUnassignedAllStores = 0;
    $primaryStore = $storesList[0] ?? null;
    $primaryHealth = strtolower((string)($primaryStore['health'] ?? 'healthy'));
    $primaryReason = (string)($primaryStore['healthReason'] ?? 'Operacion dentro de umbrales');
    $primaryConnectedPrinters = (int)($primaryStore['connectedPrinters'] ?? 0);
    $primaryUnassignedQueue = (int)($primaryStore['unassignedQueueCurrent'] ?? 0);
    $primaryFailedNoRetry = (int)($primaryStore['failedWithoutRetryCurrent'] ?? 0);
    $primaryPrinters = is_array($primaryStore['printers'] ?? null) ? $primaryStore['printers'] : [];
    $topPrinter = $primaryPrinters[0] ?? null;
    $topPrinterName = (string)($topPrinter['printerName'] ?? 'Sin impresora destacada');
    $topPrinterQueue = (int)($topPrinter['queueCurrent'] ?? 0);

    // Tienda mas conflictiva (para widgets).
    $worstStore = null;
    $worstScore = -1;

    foreach($storesList as $storeItem) {
        $healthState = strtolower((string)($storeItem['health'] ?? 'healthy'));
        if ($healthState === 'critical') {
            $criticalCount++;
        } elseif ($healthState === 'warning') {
            $warningCount++;
        } else {
            $healthyCount++;
        }

        $queued = (int)($storeItem['queuedCurrent'] ?? 0);
        $failedNoRetry = (int)($storeItem['failedWithoutRetryCurrent'] ?? 0);
        $unassigned = (int)($storeItem['unassignedQueueCurrent'] ?? 0);

        $totalQueueAllStores += $queued;
        $totalFailedNoRetryAllStores += $failedNoRetry;
        $totalUnassignedAllStores += $unassigned;

        $score = ($healthState === 'critical' ? 1000 : ($healthState === 'warning' ? 500 : 0)) + ($queued * 2) + ($failedNoRetry * 5);
        if ($score > $worstScore) {
            $worstScore = $score;
            $worstStore = $storeItem;
        }
    }

    $healthyShare = (int) round(($healthyCount / $storesTotal) * 100);
    $warningShare = (int) round(($warningCount / $storesTotal) * 100);
    $criticalShare = max(0, 100 - $healthyShare - $warningShare);
    $healthyEnd = $healthyShare;
    $warningEnd = $healthyShare + $warningShare;
    $topStores = $storesList;
    $hasStoreRisk = $warningCount > 0 || $criticalCount > 0;
@endphp

@if($tab === 'global')
    <section class="dbx-dual-panels">
        <article class="dbx-card">
            <div class="dbx-card-body">
                <div class="dbx-title-row dbx-title-row-center">
                    <h2 class="dbx-title dbx-title-center">Estad&iacute;sticas de impresi&oacute;n</h2>
                </div>
                <div class="dbx-stats-meta">
                    <span class="dbx-subtle">Periodo: {{ $windowLabel }}</span>
                    <button type="button" id="dbx-stats-toggle" class="btn btn-ghost dbx-compact-toggle">
                        %
                    </button>
                </div>
                @if($hasNoPrintActivity)
                    <div class="dbx-empty-activity" role="status">
                        Sin actividad de impresi&oacute;n en el periodo seleccionado.
                    </div>
                @endif
                <div class="dbx-bars">
                    <div class="dbx-bar-row dbx-bar-row-info">
                        <span class="dbx-bar-label">Recibidos</span>
                        <div class="dbx-bar-track"><div class="dbx-bar-fill info" style="width: {{ (int) round(($receivedVal / $base) * 100) }}%"></div></div>
                        <span class="dbx-bar-value">
                            <span class="dbx-bar-value-number">{{ $receivedVal }}</span>
                            <span class="dbx-bar-value-percent" style="display:none">{{ $receivedPct }}%</span>
                        </span>
                    </div>
                    <div class="dbx-bar-row dbx-bar-row-success">
                        <span class="dbx-bar-label">Impresos</span>
                        <div class="dbx-bar-track"><div class="dbx-bar-fill success" style="width: {{ (int) round(($printedVal / $base) * 100) }}%"></div></div>
                        <span class="dbx-bar-value">
                            <span class="dbx-bar-value-number">{{ $printedVal }}</span>
                            <span class="dbx-bar-value-percent" style="display:none">{{ $printedPct }}%</span>
                        </span>
                    </div>
                    <div class="dbx-bar-row dbx-bar-row-warning">
                        <span class="dbx-bar-label">En cola</span>
                        <div class="dbx-bar-track"><div class="dbx-bar-fill warning" style="width: {{ (int) round(($queueVal / $base) * 100) }}%"></div></div>
                        <span class="dbx-bar-value">
                            <span class="dbx-bar-value-number">{{ $queueVal }}</span>
                            <span class="dbx-bar-value-percent" style="display:none">{{ $queuePct }}%</span>
                        </span>
                    </div>
                    <div class="dbx-bar-row dbx-bar-row-danger">
                        <span class="dbx-bar-label">Fallidos</span>
                        <div class="dbx-bar-track"><div class="dbx-bar-fill danger" style="width: {{ (int) round(($failedVal / $base) * 100) }}%"></div></div>
                        <span class="dbx-bar-value">
                            <span class="dbx-bar-value-number">{{ $failedVal }}</span>
                            <span class="dbx-bar-value-percent" style="display:none">{{ $failedPct }}%</span>
                        </span>
                    </div>
                    <div class="dbx-bar-row dbx-bar-row-danger">
                        <span class="dbx-bar-label">Sin reenviar</span>
                        <div class="dbx-bar-track"><div class="dbx-bar-fill danger" style="width: {{ (int) round(($failedNoRetryVal / $base) * 100) }}%"></div></div>
                        <span class="dbx-bar-value">
                            <span class="dbx-bar-value-number">{{ $failedNoRetryVal }}</span>
                            <span class="dbx-bar-value-percent" style="display:none">{{ $failedNoRetryPct }}%</span>
                        </span>
                    </div>
                </div>

                <div class="dbx-section-divider">
                    <div class="dbx-title-row dbx-title-row-center mb-2">
                        <h3 class="dbx-title dbx-title-center dbx-title-sm">Resumen de entidades</h3>
                    </div>
                    <div class="dbx-summary-mini-grid">
                        <div class="dbx-summary-mini">
                            <span class="dbx-summary-mini-label">Tiendas</span>
                            <strong class="dbx-summary-mini-value">{{ $storesTotalSummary }}</strong>
                        </div>
                        <div class="dbx-summary-mini">
                            <span class="dbx-summary-mini-label">Impresoras</span>
                            <strong class="dbx-summary-mini-value">{{ $printersTotalSummary }}</strong>
                        </div>
                        <div class="dbx-summary-mini">
                            <span class="dbx-summary-mini-label">Usuarios</span>
                            <strong class="dbx-summary-mini-value">{{ $usersTotalSummary }}</strong>
                        </div>
                    </div>
                </div>
            </div>
        </article>

        <article class="dbx-card">
            <div class="dbx-card-body">
                @if($isAdminView)
                    <div class="dbx-title-row dbx-title-row-center">
                        <h2 class="dbx-title dbx-title-center">Estado de las tiendas</h2>
                    </div>
                    <div class="dbx-store-health-panel">
                        <div
                            class="dbx-health-ring"
                            style="--healthy-end: {{ $healthyEnd }}%; --warning-end: {{ $warningEnd }}%;"
                            aria-label="Tiendas saludables: {{ $healthyShare }}%"
                        >
                            <div class="dbx-health-ring-core">
                                <strong>{{ $healthyShare }}%</strong>
                                <span>
                                    @if($criticalCount > 0)
                                        Cr&iacute;tico
                                    @elseif($warningCount > 0)
                                        Warning
                                    @else
                                        OK
                                    @endif
                                </span>
                            </div>
                        </div>
                        <div class="dbx-health-ring-copy">
                            @if($criticalCount > 0)
                                <strong>Atenci&oacute;n prioritaria</strong>
                                <span>Hay incidencias cr&iacute;ticas activas.</span>
                            @elseif($warningCount > 0)
                                <strong>Seguimiento recomendado</strong>
                                <span>Operativa estable con avisos abiertos.</span>
                            @else
                                <strong>Operativa estable</strong>
                                <span>Sin alertas cr&iacute;ticas activas.</span>
                            @endif
                        </div>
                        <div class="dbx-health-legend" aria-label="Resumen de estado por tiendas">
                            <div class="dbx-health-legend-row">
                                <span class="dbx-health-dot success"></span>
                                <span>Saludables</span>
                                <strong>{{ $healthyCount }}</strong>
                                <small>{{ $healthyShare }}%</small>
                            </div>
                            <div class="dbx-health-legend-row">
                                <span class="dbx-health-dot warning"></span>
                                <span>Warning</span>
                                <strong>{{ $warningCount }}</strong>
                                <small>{{ $warningShare }}%</small>
                            </div>
                            <div class="dbx-health-legend-row">
                                <span class="dbx-health-dot danger"></span>
                                <span>Cr&iacute;ticas</span>
                                <strong>{{ $criticalCount }}</strong>
                                <small>{{ $criticalShare }}%</small>
                            </div>
                        </div>
                    </div>
                @else
                    <div class="dbx-title-row">
                        <h2 class="dbx-title">Estado de mi tienda</h2>
                        <span class="dbx-subtle">Informaci&oacute;n operativa ampliada</span>
                    </div>
                    <div class="dbx-store-focus">
                        <div class="dbx-store-focus-item">
                            <span>Estado actual</span>
                            <strong class="dbx-store-health {{ $primaryHealth }}">{{ strtoupper($primaryHealth) }}</strong>
                        </div>
                        <div class="dbx-store-focus-item">
                            <span>Impresoras conectadas</span>
                            <strong>{{ $primaryConnectedPrinters }}</strong>
                        </div>
                        <div class="dbx-store-focus-item">
                            <span>Cola sin asignar</span>
                            <strong>{{ $primaryUnassignedQueue }}</strong>
                        </div>
                        <div class="dbx-store-focus-item">
                            <span>Fallos sin reenviar</span>
                            <strong>{{ $primaryFailedNoRetry }}</strong>
                        </div>
                    </div>
                    <div class="dbx-store-reason-box">
                        <span>Motivo de salud</span>
                        <p>{{ $primaryReason }}</p>
                    </div>
                    <div class="dbx-store-reason-box">
                        <span>Impresora m&aacute;s cargada</span>
                        <p>{{ $topPrinterName }} (cola: {{ $topPrinterQueue }})</p>
                    </div>
                @endif
            </div>
        </article>
    </section>

@endif

@if($isAdminUser)
    <section class="dbx-card">
        <div class="dbx-card-body">
        <div class="dbx-title-row">
            <h2 class="dbx-title">Alertas priorizadas</h2>
            @if($generatedAtUtc)
                <span class="dbx-subtle">Actualizado: {{ $generatedAtUtc }}</span>
            @endif
        </div>
        @if(count($alerts) === 0)
            <p class="dbx-subtle">Sin alertas activas para el ambito y periodo seleccionados.</p>
        @else
            <div class="dbx-table-wrap dbx-alerts-wrap table-alerts-wrap" style="--dbx-visible-rows: {{ $alertsVisibleRows }};">
                <table class="dbx-table table-alerts">
                    <thead>
                        <tr>
                            <th class="store-col">Tienda</th>
                            <th>Salud</th>
                            <th>Tipo</th>
                            <th class="text-col">Motivo</th>
                            <th>En cola</th>
                            <th>Fallo sin reenviar (periodo)</th>
                        </tr>
                    </thead>
                    <tbody>
                        @php
                            $alertsGrouped = [];
                            foreach ($alerts as $alertRow) {
                                $sid = (string) ($alertRow['storeId'] ?? '-');
                                if (!isset($alertsGrouped[$sid])) {
                                    $alertsGrouped[$sid] = [
                                        'storeId' => $alertRow['storeId'] ?? null,
                                        'storeName' => $alertRow['storeName'] ?? 'Sin nombre',
                                        'items' => [],
                                    ];
                                }
                                $alertsGrouped[$sid]['items'][] = $alertRow;
                            }
                        @endphp

                        @foreach($alertsGrouped as $group)
                            @php
                                $groupStoreId = $group['storeId'] ?? null;
                                $groupItems = is_array($group['items'] ?? null) ? $group['items'] : [];
                                $rowspan = max(1, count($groupItems));
                            @endphp

                            @foreach($groupItems as $idx => $alert)
                                @php
                                    $status = $alert['health'] ?? 'healthy';
                                    $storeId = $alert['storeId'] ?? null;
                                    $reason = (string) ($alert['healthReason'] ?? '');
                                    $href = null;
                                    $alertType = 'other';
                                    $alertTypeLabel = 'Otro';

                                    if ($storeId !== null && $storeId !== '') {
                                        // Clasificación por tipo (y destino).
                                        $mentionsNoActivePrinters =
                                            stripos($reason, 'sin impresoras activas') !== false
                                            || stripos($reason, 'no hay impresoras activas') !== false
                                            || stripos($reason, 'hay cola pero no hay impresoras activas') !== false;

                                        if ($mentionsNoActivePrinters) {
                                            $alertType = 'printers_inactive';
                                            $alertTypeLabel = 'Impresoras';
                                            $href = '/impresoras?storeId=' . urlencode((string)$storeId) . '&isActive=false';
                                        } elseif (stripos($reason, 'sin host') !== false) {
                                            $alertType = 'missing_host';
                                            $alertTypeLabel = 'Sin host';
                                            $href = '/impresoras?storeId=' . urlencode((string)$storeId);
                                        } elseif (stripos($reason, 'sin reenviar') !== false) {
                                            $alertType = 'failed_no_retry';
                                            $alertTypeLabel = 'Sin reenviar';
                                            $href = '/alertas?storeId=' . urlencode((string)$storeId);
                                        } elseif (stripos($reason, 'conectividad') !== false || stripos($reason, 'fallos de conexion') !== false) {
                                            $alertType = 'connectivity';
                                            $alertTypeLabel = 'Conectividad';
                                            $href = '/impresoras?storeId=' . urlencode((string)$storeId) . '&isActive=true';
                                        } elseif (stripos($reason, 'cola') !== false || stripos($reason, 'trabajos') !== false) {
                                            $alertType = 'queue';
                                            $alertTypeLabel = 'Cola';
                                            // Por defecto, abrimos Pendiente (lo mas accionable); si quieres lo quitamos y dejamos "Todos".
                                            $href = '/cola?storeId=' . urlencode((string)$storeId) . '&status=0';
                                        }
                                    }

                                    $alertRowClass = match ($status) {
                                        'critical' => 'alert-row-critical',
                                        'warning' => 'alert-row-warning',
                                        default => 'alert-row-ok',
                                    };
                                @endphp
                                <tr class="{{ $idx === 0 ? 'dbx-alert-group-start' : 'dbx-alert-group-cont' }} dbx-alert-row-{{ $status }} {{ $alertRowClass }}" @if($href) data-alert-href="{{ $href }}" @endif>
                                    @if($idx === 0)
                                        <td class="store-col" rowspan="{{ $rowspan }}">{{ $formatStoreLabel($groupStoreId, $group['storeName'] ?? 'Sin nombre') }}</td>
                                    @endif
                                    <td><span class="dbx-pill {{ $status }}">{{ strtoupper($status) }}</span></td>
                                    <td>
                                        {{-- La columna "Tipo" usa el mismo color que la columna "Salud" para no confundir. --}}
                                        <span class="dbx-pill {{ $status === 'healthy' ? 'healthy' : $status }}">{{ $alertTypeLabel }}</span>
                                    </td>
                                    <td class="text-col">{{ $reason !== '' ? $reason : '-' }}</td>
                                    <td>{{ $alert['queuedCurrent'] ?? 0 }}</td>
                                    <td>{{ $alert['failedWithoutRetryCurrent'] ?? 0 }}</td>
                                </tr>
                            @endforeach
                        @endforeach
                    </tbody>
                </table>
            </div>
            <script>
            (function() {
                document.querySelectorAll('tr[data-alert-href]').forEach(tr => {
                    let href = tr.getAttribute('data-alert-href');
                    if (!href) return;
                    // Defensa: si por cualquier motivo llega con comillas, las quitamos
                    href = href.trim().replace(/^"+|"+$/g, '');
                    tr.style.cursor = 'pointer';
                    tr.addEventListener('click', function(e) {
                        const el = e.target;
                        if (el && el.closest && el.closest('a, button, form')) return;
                        window.location.href = href;
                    });
                });
            })();
            </script>
        @endif
        <p class="text-xs muted-text mt-3">
            Regla de salud configurable por umbrales de cola, fallos sin reenviar y conectividad.
        </p>
        </div>
    </section>

    @php
        $storeViewParams = request()->except(['tab', 'storeView']);
        $estadoUrl = route('dashboard', array_merge($storeViewParams, ['tab' => 'global', 'storeView' => 'estado']));
        $detalleUrl = route('dashboard', array_merge($storeViewParams, ['tab' => 'global', 'storeView' => 'detalle']));
    @endphp
    <section class="dbx-card dbx-store-view-shell">
        <div class="dbx-card-body">
            <div class="dbx-title-row">
                <h2 class="dbx-title">Tiendas</h2>
                <span class="dbx-subtle">Alterna entre priorizaci&oacute;n y detalle operativo</span>
            </div>
            <div class="dbx-store-view-tabs" aria-label="Vista de tiendas">
                <a href="{{ $estadoUrl }}" class="dbx-store-view-tab {{ $storeView === 'estado' ? 'is-active' : '' }}">Estado por tienda</a>
                <a href="{{ $detalleUrl }}" class="dbx-store-view-tab {{ $storeView === 'detalle' ? 'is-active' : '' }}">Detalle compacto</a>
            </div>
        </div>
    </section>

    @if($storeView === 'estado')
        <section class="dbx-card">
            <div class="dbx-card-body">
                <div class="dbx-title-row">
                    <h2 class="dbx-title">{{ $hasStoreRisk ? 'Top tiendas por riesgo' : 'Estado por tienda' }}</h2>
                    <span class="dbx-subtle">Ranking operativo para priorizar revisi&oacute;n</span>
                </div>
                @if(count($topStores) === 0)
                    <p class="dbx-subtle mt-2">No hay tiendas para los filtros actuales.</p>
                @else
                    <div class="dbx-store-state-list mt-4">
                        @foreach($topStores as $store)
                            @php
                                $status = $store['health'] ?? 'healthy';
                                $healthClass = $status === 'critical'
                                    ? 'critical'
                                    : ($status === 'warning' ? 'warning' : 'healthy');
                                $printerRows = is_array($store['printers'] ?? null) ? $store['printers'] : [];
                                $storeIdText = (string)($store['storeId'] ?? '-');
                                $storeTitle = $formatStoreLabel($storeIdText, $store['storeName'] ?? 'Sin nombre');
                                $riskQueue = (int)($store['queuedCurrent'] ?? 0);
                                $riskFailedNoRetry = (int)($store['failedWithoutRetryCurrent'] ?? 0);
                                $riskUnassigned = (int)($store['unassignedQueueCurrent'] ?? 0);
                                $riskReason = (string)($store['healthReason'] ?? 'Operacion normal');
                                $riskReasonSource = strtolower(strip_tags(html_entity_decode($riskReason)));
                                if ($healthClass === 'healthy') {
                                    $riskReasonShort = 'Operaci&oacute;n normal';
                                } elseif (count($printerRows) === 0 || str_contains($riskReasonSource, 'sin impresora')) {
                                    $riskReasonShort = 'Sin impresoras';
                                } elseif (str_contains($riskReasonSource, 'host') || str_contains($riskReasonSource, 'unc') || str_contains($riskReasonSource, 'conect')) {
                                    $riskReasonShort = 'Conectividad';
                                } elseif ($riskFailedNoRetry > 0 || str_contains($riskReasonSource, 'sin reenviar') || str_contains($riskReasonSource, 'fallo')) {
                                    $riskReasonShort = 'Sin reenviar';
                                } elseif ($riskUnassigned > 0 || str_contains($riskReasonSource, 'sin asignar')) {
                                    $riskReasonShort = 'Sin asignar';
                                } elseif ($riskQueue > 0 || str_contains($riskReasonSource, 'cola')) {
                                    $riskReasonShort = 'Cola acumulada';
                                } else {
                                    $riskReasonShort = 'Operaci&oacute;n normal';
                                }
                                $detailHref = $detalleUrl . '#detalle-tienda-' . rawurlencode($storeIdText);
                            @endphp
                            <article class="dbx-risk-list-row dbx-risk-row-{{ $healthClass }}">
                                <div class="dbx-risk-store-name">{{ $storeTitle }}</div>
                                <span class="dbx-pill {{ $healthClass }}">{{ strtoupper($status) }}</span>
                                <span class="dbx-risk-row-reason" title="{{ $riskReason }}">{!! $riskReasonShort !!}</span>
                                <div class="dbx-risk-row-metrics">
                                    <span>Cola <strong class="{{ $riskQueue > 0 ? 'is-warning' : '' }}">{{ $riskQueue }}</strong></span>
                                    <span>Sin reenviar <strong class="{{ $riskFailedNoRetry > 0 ? 'is-danger' : '' }}">{{ $riskFailedNoRetry }}</strong></span>
                                    <span>Sin asignar <strong class="{{ $riskUnassigned > 0 ? 'is-warning' : '' }}">{{ $riskUnassigned }}</strong></span>
                                </div>
                                <a href="{{ $detailHref }}" class="dbx-risk-detail-link">Ver detalle</a>
                            </article>
                        @endforeach
                    </div>
                @endif
            </div>
        </section>
    @else
    <section class="dbx-card">
        <div class="dbx-card-body">
        <div class="dbx-title-row">
            <h2 class="dbx-title">Detalle compacto por tienda</h2>
            <span class="dbx-subtle">Configurable por filtros superiores</span>
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

        const healthEl = document.getElementById('health');
        if (healthEl) prefs.health = healthEl.value;

        const autoRefreshEl = document.getElementById('autoRefresh');
        if (autoRefreshEl) prefs.autoRefresh = autoRefreshEl.value;

        const storeSortEl = document.getElementById('storeSort');
        if (storeSortEl) prefs.storeSort = storeSortEl.value;

        const storeColsEl = document.getElementById('storeCols');
        if (storeColsEl) prefs.storeCols = storeColsEl.value;

        const showHealthyEl = form.querySelector('input[type="checkbox"][name="showHealthy"]');
        if (showHealthyEl) prefs.showHealthy = showHealthyEl.checked ? '1' : '0';

        return prefs;
    }

    function applyPrefsToUrlIfMissing(prefs) {
        return false;
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
    const toggle = document.getElementById('dbx-stats-toggle');
    if (!toggle) return;

    let mode = 'number';
    const values = document.querySelectorAll('.dbx-bar-value');

    function applyMode() {
        values.forEach(function(v) {
            const num = v.querySelector('.dbx-bar-value-number');
            const pct = v.querySelector('.dbx-bar-value-percent');
            if (!num || !pct) return;
            if (mode === 'number') {
                num.style.display = '';
                pct.style.display = 'none';
            } else {
                num.style.display = 'none';
                pct.style.display = '';
            }
        });
        toggle.textContent = mode === 'number' ? '%' : 'N';
    }

    toggle.addEventListener('click', function() {
        mode = mode === 'number' ? 'percent' : 'number';
        applyMode();
    });
})();

(function() {
    let dashboardAbortController = null;
    let dashboardRequestId = 0;
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

    function initStatsToggle(root) {
        const toggle = root.querySelector('#dbx-stats-toggle');
        if (!toggle || toggle.dataset.dynamicBound === '1') return;

        toggle.dataset.dynamicBound = '1';
        let mode = 'number';

        function applyMode() {
            root.querySelectorAll('.dbx-bar-value').forEach(function(v) {
                const num = v.querySelector('.dbx-bar-value-number');
                const pct = v.querySelector('.dbx-bar-value-percent');
                if (!num || !pct) return;
                if (mode === 'number') {
                    num.style.display = '';
                    pct.style.display = 'none';
                } else {
                    num.style.display = 'none';
                    pct.style.display = '';
                }
            });
            toggle.textContent = mode === 'number' ? '%' : 'N';
        }

        toggle.addEventListener('click', function() {
            mode = mode === 'number' ? 'percent' : 'number';
            applyMode();
        });
    }

    function prepareDynamicDashboard(root) {
        const form = root.querySelector('form[data-dashboard-filter-form]');
        if (form) {
            form.dataset.dynamicOwned = '1';
        }

        initStatsToggle(root);
        scheduleAutoRefresh(Number(root.querySelector('#autoRefresh')?.value || 0));
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

        formData.forEach(function(value, key) {
            if (value !== null && value !== '') {
                url.searchParams.append(key, value);
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
        } catch (error) {
            if (error.name === 'AbortError') return;
            const root = getDashboardRoot();
            if (root) {
                root.classList.remove('is-dynamic-loading');
                root.removeAttribute('aria-busy');
                showDashboardError(root, url);
                scheduleAutoRefresh(Number(root.querySelector('#autoRefresh')?.value || 0));
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
