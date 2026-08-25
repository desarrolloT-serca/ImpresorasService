@extends('layouts.app')

@section('title', 'Mi tienda')

@section('content')
@php
    $window = $window ?? 'today';
    $autoRefreshSeconds = 30; // fijo: el selector de segundos se retiro de la interfaz
    $stores = is_array($stores ?? null) ? $stores : [];
    $store = $stores[0] ?? [];
    $storeId = (int) ($store['storeId'] ?? ($effectiveStoreId ?? 0));
    $storeName = \App\Helpers\StoreFormat::label($storeId, $store['storeName'] ?? 'Mi tienda');
    $health = strtolower((string) ($store['health'] ?? 'healthy'));
    $healthClass = in_array($health, ['critical', 'warning', 'healthy'], true) ? $health : 'healthy';
    $healthLabel = match($healthClass) {
        'critical' => 'Critica',
        'warning' => 'Atencion',
        default => 'Operativa',
    };
    $healthReason = (string) ($store['healthReason'] ?? 'Operacion dentro de umbrales');
    $printers = is_array($store['printers'] ?? null) ? $store['printers'] : [];
    $connectedPrinters = (int) ($store['connectedPrinters'] ?? 0);
    $queue = (int) ($store['queuedCurrent'] ?? 0);
    $failedNoRetry = (int) ($store['failedWithoutRetryCurrent'] ?? 0);
    $unassigned = (int) ($store['unassignedQueueCurrent'] ?? 0);
    $received = (int) ($store['received'] ?? 0);
    $printed = (int) ($store['printed'] ?? 0);
    $failed = (int) ($store['failed'] ?? 0);
    $missingHost = (int) ($store['printersMissingHost'] ?? 0);
    $connWarning = (int) ($store['printersConnWarning'] ?? 0);
    $connCritical = (int) ($store['printersConnCritical'] ?? 0);
    $windowLabel = match($window) {
        '7d' => 'Ultimos 7 dias',
        '30d' => 'Ultimos 30 dias',
        default => 'Hoy',
    };
    $successRate = $received > 0 ? (int) round(($printed / $received) * 100) : null; // sin recibidos no hay tasa que mostrar, no un 100% ficticio
    $generatedAt = (string) ($generatedAtUtc ?? '');
    $updatedLabel = $generatedAt !== '' ? str_replace('T', ' ', substr($generatedAt, 0, 19)) . ' UTC' : 'N/D';
    $canOperate = ($isAdmin ?? false) || ($isStoreManager ?? false);

    // "Desde" de cada incidencia (mismo criterio que el dashboard de admin): el trabajo en cola
    // mas viejo, el fallo sin reenviar mas viejo, o el ultimo chequeo de la impresora.
    $sinceLabel = static function ($iso): string {
        $raw = trim((string) ($iso ?? ''));
        $ts = $raw !== '' ? strtotime($raw) : false;
        if ($ts === false) {
            return 'sin comprobar';
        }
        $mins = (int) floor((time() - $ts) / 60);
        if ($mins < 1) {
            return 'ahora mismo';
        }
        if ($mins < 60) {
            return $mins . ' min';
        }
        $hours = intdiv($mins, 60);
        if ($hours < 24) {
            $rest = $mins % 60;
            return $rest === 0 ? $hours . ' h' : $hours . ' h ' . str_pad((string) $rest, 2, '0', STR_PAD_LEFT) . ' min';
        }
        return intdiv($hours, 24) . ' d ' . ($hours % 24) . ' h';
    };
    $oldestQueued = $store['oldestQueuedAtUtc'] ?? null;
    $oldestFailed = $store['oldestFailedWithoutRetryAtUtc'] ?? null;
    $offlinePrinter = null;
    $noHostPrinter = null;
    foreach ($printers as $p) {
        $err = strtolower((string) ($p['lastConnectionError'] ?? ''));
        if ($noHostPrinter === null && (trim((string) ($p['host'] ?? '')) === '' || str_contains($err, 'host_not_configured'))) {
            $noHostPrinter = $p;
        } elseif ($offlinePrinter === null && ($p['lastConnectionOk'] ?? null) === false) {
            $offlinePrinter = $p;
        }
    }

    $attentionItems = [];
    if ($connCritical > 0) {
        $streak = (int) ($offlinePrinter['connectionFailuresStreak'] ?? 0);
        $transport = trim((string) ($offlinePrinter['lastConnectionTransport'] ?? ''));
        $detail = array_filter([
            $streak > 0 ? $streak . ' ' . ($streak === 1 ? 'fallo consecutivo' : 'fallos consecutivos') : null,
            $transport !== '' ? 'transporte ' . $transport : null,
        ]);
        $attentionItems[] = [
            'level' => 'critical', 'kicker' => 'CONECTIVIDAD',
            'lead' => $offlinePrinter !== null ? $offlinePrinter['printerName'] . ' sin conexión' : $connCritical . ' impresora(s) sin conexión',
            'tail' => $detail !== [] ? implode(', ', $detail) : 'requieren revisión de conectividad',
            'since' => $offlinePrinter['lastConnectionCheckAtUtc'] ?? null,
            'action' => ['Ver impresoras', $offlinePrinter !== null
                ? '/impresoras?storeId=' . $storeId . '&printerId=' . (int) $offlinePrinter['printerId']
                : '/impresoras?storeId=' . $storeId],
        ];
    }
    if ($failedNoRetry > 0) {
        $attentionItems[] = [
            'level' => 'critical', 'kicker' => 'SIN REENVIAR',
            'lead' => $failedNoRetry . ' ' . ($failedNoRetry === 1 ? 'trabajo sin reenviar' : 'trabajos sin reenviar'),
            'tail' => 'reintentos agotados y nadie los ha reenviado',
            'since' => $oldestFailed,
            'action' => ['Reenviar ' . $failedNoRetry, '/cola?storeId=' . $storeId . '&failedWithoutRetry=1'],
        ];
    }
    if ($queue > 0) {
        $attentionItems[] = [
            'level' => $queue >= 10 ? 'warning' : 'info', 'kicker' => 'COLA',
            'lead' => 'Cola de ' . $queue . ' ' . ($queue === 1 ? 'trabajo' : 'trabajos'),
            'tail' => 'pendientes de salida',
            'since' => $oldestQueued,
            'action' => ['Ver en cola', '/cola?storeId=' . $storeId . '&status=0'],
        ];
    }
    if ($unassigned > 0) {
        $attentionItems[] = [
            'level' => 'warning', 'kicker' => 'SIN IMPRESORA ASIGNADA',
            'lead' => $unassigned . ' ' . ($unassigned === 1 ? 'trabajo sin impresora' : 'trabajos sin impresora'),
            'tail' => 'ninguna regla de enrutado coincide',
            'since' => $oldestQueued,
            'action' => ['Ver los sin impresora', '/cola?storeId=' . $storeId . '&unassigned=1'],
        ];
    }
    if ($missingHost > 0) {
        $attentionItems[] = [
            'level' => 'warning', 'kicker' => 'SIN HOST',
            'lead' => $noHostPrinter !== null ? $noHostPrinter['printerName'] . ' sin host configurado' : $missingHost . ' impresora(s) sin host',
            'tail' => 'no puede recibir trabajos',
            'since' => $noHostPrinter['lastConnectionCheckAtUtc'] ?? null,
            'action' => ['Configurar host', $noHostPrinter !== null
                ? '/impresoras?storeId=' . $storeId . '&printerId=' . (int) $noHostPrinter['printerId']
                : '/impresoras?storeId=' . $storeId],
        ];
    }
    if ($connWarning > 0) {
        $attentionItems[] = [
            'level' => 'warning', 'kicker' => 'CONECTIVIDAD',
            'lead' => $connWarning . ' impresora(s) con conectividad inestable',
            'tail' => 'acumulan fallos de conexión',
            'since' => $offlinePrinter['lastConnectionCheckAtUtc'] ?? null,
            'action' => ['Ver impresoras', '/impresoras?storeId=' . $storeId],
        ];
    }
    if (!empty($partialData)) {
        $attentionItems[] = [
            'level' => 'warning', 'kicker' => 'DATOS PARCIALES',
            'lead' => 'Resumen agregado no disponible',
            'tail' => 'se muestran como máximo 500 trabajos recientes',
            'since' => null,
            'action' => null,
        ];
    }

    $sortedPrinters = $printers;
    usort($sortedPrinters, function (array $a, array $b): int {
        $aProblem = (($a['lastConnectionOk'] ?? null) === true ? 0 : 1) + ((int) ($a['queueCurrent'] ?? 0) > 0 ? 1 : 0);
        $bProblem = (($b['lastConnectionOk'] ?? null) === true ? 0 : 1) + ((int) ($b['queueCurrent'] ?? 0) > 0 ? 1 : 0);
        return ($bProblem <=> $aProblem)
            ?: ((int) ($b['queueCurrent'] ?? 0) <=> (int) ($a['queueCurrent'] ?? 0))
            ?: strcmp((string) ($a['printerName'] ?? ''), (string) ($b['printerName'] ?? ''));
    });
@endphp

<div class="dbx-dashboard-page local-dashboard">
    <section class="local-hero local-hero-{{ $healthClass }}">
        <div class="local-hero-main">
            <span class="local-eyebrow">Operacion local</span>
            <h2>{{ $storeName }}</h2>
            <p>{{ $healthReason }}</p>
        </div>
        <div class="local-health-panel">
            <span class="dbx-pill {{ $healthClass }}">{{ $healthLabel }}</span>
            <strong>{{ $successRate === null ? '—' : $successRate . '%' }}</strong>
            <span>impresos sobre recibidos</span>
            <small>Actualizado: {{ $updatedLabel }}</small>
        </div>
    </section>

    <x-ui.card class="local-filters-card">
        <form method="GET" class="filters-row local-filters" autocomplete="off">
            <div class="dbx-filters">
                <div class="dbx-filter-item">
                    <label for="window" class="dbx-filter-label">Periodo</label>
                    <select id="window" name="window" class="select !w-auto" data-current="{{ $window }}">
                        <option value="today" {{ $window === 'today' ? 'selected' : '' }}>Hoy</option>
                        <option value="7d" {{ $window === '7d' ? 'selected' : '' }}>7 dias</option>
                        <option value="30d" {{ $window === '30d' ? 'selected' : '' }}>30 dias</option>
                    </select>
                </div>
            </div>
            <div class="local-actions">
                <a class="btn btn-ghost" href="{{ url('/cola?storeId=' . $storeId) }}">Ver cola</a>
                <a class="btn btn-ghost" href="{{ url('/cola?storeId=' . $storeId . '&status=8') }}">Ver errores</a>
                @if($canOperate)
                    <a class="btn btn-primary" href="{{ url('/impresoras?storeId=' . $storeId) }}">Revisar impresoras</a>
                @endif
            </div>
        </form>
    </x-ui.card>

    <div class="dbx-op">

        {{-- Estado actual: no depende del periodo del filtro (A-KPI-01). --}}
        <section class="dbx-op-card dbx-op-card-hero {{ $healthClass === 'critical' ? 'dbx-op-card-alert' : '' }}">
            <div class="dbx-op-body">
                <div class="dbx-op-head">
                    <h2 class="dbx-op-title">Estado actual de la operaci&oacute;n</h2>
                    @if($healthClass === 'critical')
                        <span class="dbx-op-meta-alert">REQUIERE ATENCIÓN</span>
                    @elseif($healthClass === 'warning')
                        <span class="dbx-op-meta-alert" style="color: var(--ui-warning)">REVISAR</span>
                    @else
                        <span class="dbx-op-meta-ok">OPERACIÓN NORMAL</span>
                    @endif
                </div>
                <div class="dbx-op-tiles">
                    <div class="dbx-op-tile">
                        <span class="dbx-op-tile-label">EN COLA</span>
                        <strong class="dbx-op-tile-value">{{ $queue }}</strong>
                        <span class="dbx-op-tile-note">trabajos esperando</span>
                    </div>
                    <div class="dbx-op-tile {{ $failedNoRetry > 0 ? 'dbx-op-tile-alert' : '' }}">
                        <span class="dbx-op-tile-label">SIN REENVIAR</span>
                        <strong class="dbx-op-tile-value">{{ $failedNoRetry }}</strong>
                        <span class="dbx-op-tile-note">necesitan seguimiento</span>
                    </div>
                    <div class="dbx-op-tile {{ $connCritical > 0 ? 'dbx-op-tile-alert' : '' }}">
                        <span class="dbx-op-tile-label">IMPRESORAS ACTIVAS</span>
                        <strong class="dbx-op-tile-value">{{ $connectedPrinters }}</strong>
                        <span class="dbx-op-tile-note">{{ count($printers) }} registradas</span>
                    </div>
                    <div class="dbx-op-tile">
                        <span class="dbx-op-tile-label">SIN IMPRESORA</span>
                        <strong class="dbx-op-tile-value">{{ $unassigned }}</strong>
                        <span class="dbx-op-tile-note">trabajos sin asignar</span>
                    </div>
                </div>
            </div>
        </section>

        <section class="dbx-op-card">
            <div class="dbx-op-body">
                <div class="dbx-op-head">
                    <h2 class="dbx-op-title">Incidencias priorizadas &mdash; acci&oacute;n requerida</h2>
                    <span class="dbx-op-meta">{{ count($attentionItems) }} {{ count($attentionItems) === 1 ? 'abierta' : 'abiertas' }}</span>
                </div>
                @if($attentionItems === [])
                    <p class="dbx-op-empty">Sin incidencias activas. La tienda est&aacute; dentro de los umbrales definidos.</p>
                @else
                    <div class="dbx-op-table-wrap">
                        <table class="dbx-op-table">
                            <thead>
                                <tr>
                                    <th style="width:104px">Severidad</th>
                                    <th>Incidencia</th>
                                    <th style="width:112px">Desde</th>
                                    <th style="width:196px">Acci&oacute;n</th>
                                </tr>
                            </thead>
                            <tbody>
                                @foreach($attentionItems as $item)
                                    @php
                                        // Las paginas de impresoras solo se ofrecen a quien puede operar sobre ellas.
                                        $href = $item['action'] !== null && ($canOperate || !str_contains($item['action'][1], '/impresoras'))
                                            ? $item['action'][1]
                                            : null;
                                    @endphp
                                    <tr class="is-{{ $item['level'] }}">
                                        <td><span class="dbx-op-sev dbx-op-sev-{{ $item['level'] }}">{{ ['critical' => 'CRÍTICO', 'warning' => 'AVISO', 'info' => 'INFO'][$item['level']] ?? 'AVISO' }}</span></td>
                                        <td class="dbx-op-cell-text">
                                            <span class="dbx-op-kicker">{{ $item['kicker'] }}</span>
                                            <span class="dbx-op-text"><b>{{ $item['lead'] }}</b> &mdash; {{ $item['tail'] }}</span>
                                        </td>
                                        <td class="dbx-op-since" title="{{ $item['since'] ?? 'Sin dato de inicio' }}">{{ $sinceLabel($item['since']) }}</td>
                                        <td>
                                            @if($href)
                                                <a href="{{ url($href) }}" class="dbx-op-action {{ $item['level'] === 'critical' ? 'dbx-op-action-critical' : '' }}">{{ $item['action'][0] }}</a>
                                            @else
                                                <span class="dbx-op-since">&mdash;</span>
                                            @endif
                                        </td>
                                    </tr>
                                @endforeach
                            </tbody>
                        </table>
                    </div>
                @endif
            </div>
        </section>

        <section class="dbx-op-card">
            <div class="dbx-op-body">
                <div class="dbx-op-head">
                    <h2 class="dbx-op-title">Rendimiento del periodo &mdash; {{ $windowLabel }}</h2>
                    <span class="dbx-op-meta">
                        No incluye estado actual &middot;
                        {{ $received > 0 ? 'porcentajes sobre ' . $received . ' recibidos' : 'sin trabajos recibidos en el periodo' }}
                    </span>
                </div>
                <div class="dbx-op-tiles dbx-op-tiles-kpi">
                    <div class="dbx-op-tile">
                        <span class="dbx-op-tile-label">RECIBIDOS</span>
                        <strong class="dbx-op-tile-value">{{ $received }}</strong>
                    </div>
                    <div class="dbx-op-tile">
                        <span class="dbx-op-tile-label">IMPRESOS</span>
                        <strong class="dbx-op-tile-value">{{ $printed }}</strong>
                    </div>
                    <div class="dbx-op-tile {{ $failed > 0 ? 'dbx-op-tile-alert' : '' }}">
                        <span class="dbx-op-tile-label">FALLIDOS</span>
                        <strong class="dbx-op-tile-value">{{ $failed }}</strong>
                    </div>
                    <div class="dbx-op-tile dbx-op-tile-rate">
                        <span class="dbx-op-tile-label">TASA DE &Eacute;XITO</span>
                        <strong class="dbx-op-tile-value">{{ $successRate === null ? '—' : $successRate . ' %' }}</strong>
                        <span class="dbx-op-rate-bar"><span style="width: {{ $successRate ?? 0 }}%"></span></span>
                    </div>
                </div>
            </div>
        </section>

    </div>

    <x-ui.card class="local-printers-card">
        <div class="dbx-title-row">
            <h2 class="dbx-title">Impresoras de la tienda</h2>
            <span class="dbx-subtle">Estado de conectividad y carga</span>
        </div>
        @if(count($sortedPrinters) === 0)
            <p class="dbx-subtle">No hay impresoras activas asociadas a esta tienda.</p>
        @else
            <div class="local-printer-table">
                <div class="local-printer-head">
                    <span>Impresora</span>
                    <span>Conexion</span>
                    <span>Cola</span>
                    <span>Ultimo chequeo</span>
                    <span>Detalle</span>
                </div>
                @foreach($sortedPrinters as $printer)
                    @php
                        $connOk = $printer['lastConnectionOk'] ?? null;
                        $connError = strtoupper((string) ($printer['lastConnectionError'] ?? ''));
                        $connCheckAt = (string) ($printer['lastConnectionCheckAtUtc'] ?? '');
                        $connStreak = (int) ($printer['connectionFailuresStreak'] ?? 0);
                        $connClass = $connOk === true ? 'ok' : 'off';
                        $connText = $connOk === true
                            ? 'OK'
                            : (($connError === 'HOST_NOT_CONFIGURED' || $connCheckAt === '') ? 'Sin host' : 'Sin conexion');
                        $detail = $connOk === true
                            ? 'Operativa'
                            : ($connError !== '' ? str_replace('_', ' ', strtolower($connError)) : 'Pendiente de chequeo');
                        $checkLabel = $connCheckAt !== '' ? str_replace('T', ' ', substr($connCheckAt, 0, 16)) : 'N/D';
                    @endphp
                    <div class="local-printer-row">
                        <span>
                            <strong>{{ $printer['printerName'] ?? 'Impresora' }}</strong>
                            <small>{{ $printer['spoolQueue'] ?? '-' }}</small>
                        </span>
                        <span><b class="dbx-printer-state {{ $connClass }}">{{ $connText }}</b></span>
                        <span>{{ $printer['queueCurrent'] ?? 0 }}</span>
                        <span>{{ $checkLabel }}</span>
                        <span>{{ $detail }}@if($connStreak > 0) · {{ $connStreak }} fallo(s)@endif</span>
                    </div>
                @endforeach
            </div>
        @endif
    </x-ui.card>
</div>

<script>
(function() {
    const autoRefreshSeconds = {{ (int) $autoRefreshSeconds }};
    if (autoRefreshSeconds > 0) {
        setTimeout(function() {
            window.location.reload();
        }, autoRefreshSeconds * 1000);
    }
})();
</script>
@endsection
