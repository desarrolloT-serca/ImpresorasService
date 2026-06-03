@extends('layouts.app')

@section('title', 'Mi tienda')

@section('content')
@php
    $window = $window ?? 'today';
    $autoRefreshSeconds = (int) ($autoRefreshSeconds ?? 0);
    $stores = is_array($stores ?? null) ? $stores : [];
    $store = $stores[0] ?? [];
    $storeId = (int) ($store['storeId'] ?? ($effectiveStoreId ?? 0));
    $storeNameRaw = trim((string) ($store['storeName'] ?? 'Mi tienda'));
    $storeName = $storeId > 0 && preg_match('/\b' . preg_quote((string) $storeId, '/') . '\b/u', $storeNameRaw) !== 1
        ? '#' . $storeId . ' - ' . $storeNameRaw
        : $storeNameRaw;
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
    $successRate = $received > 0 ? (int) round(($printed / max(1, $received)) * 100) : 100;
    $generatedAt = (string) ($generatedAtUtc ?? '');
    $updatedLabel = $generatedAt !== '' ? str_replace('T', ' ', substr($generatedAt, 0, 19)) . ' UTC' : 'N/D';
    $canOperate = ($isAdmin ?? false) || ($isStoreManager ?? false);

    $attentionItems = [];
    if ($connCritical > 0) {
        $attentionItems[] = ['level' => 'critical', 'title' => 'Impresoras sin conexion', 'text' => $connCritical . ' impresora(s) requieren revision de conectividad.', 'href' => url('/impresoras?storeId=' . $storeId . '&isActive=true')];
    }
    if ($queue > 0) {
        $attentionItems[] = ['level' => $queue >= 10 ? 'warning' : 'info', 'title' => 'Trabajos en cola', 'text' => $queue . ' trabajo(s) pendientes de salida.', 'href' => url('/cola?storeId=' . $storeId . '&status=0')];
    }
    if ($failedNoRetry > 0) {
        $attentionItems[] = ['level' => 'critical', 'title' => 'Fallos sin reenviar', 'text' => $failedNoRetry . ' trabajo(s) necesitan seguimiento.', 'href' => url('/alertas?storeId=' . $storeId)];
    }
    if ($unassigned > 0) {
        $attentionItems[] = ['level' => 'warning', 'title' => 'Cola sin impresora', 'text' => $unassigned . ' trabajo(s) no tienen impresora asignada.', 'href' => url('/cola?storeId=' . $storeId)];
    }
    if ($missingHost > 0) {
        $attentionItems[] = ['level' => 'warning', 'title' => 'Configuracion incompleta', 'text' => $missingHost . ' impresora(s) sin host configurado.', 'href' => url('/impresoras?storeId=' . $storeId)];
    }
    if ($connWarning > 0) {
        $attentionItems[] = ['level' => 'warning', 'title' => 'Conectividad inestable', 'text' => $connWarning . ' impresora(s) acumulan fallos de conexion.', 'href' => url('/impresoras?storeId=' . $storeId . '&isActive=true')];
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
            <strong>{{ $successRate }}%</strong>
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
                <div class="dbx-filter-item">
                    <label for="autoRefresh" class="dbx-filter-label">Auto refresh</label>
                    <select id="autoRefresh" name="autoRefresh" class="select !w-auto" data-current="{{ $autoRefreshSeconds }}">
                        <option value="0" {{ $autoRefreshSeconds === 0 ? 'selected' : '' }}>Off</option>
                        <option value="15" {{ $autoRefreshSeconds === 15 ? 'selected' : '' }}>15s</option>
                        <option value="30" {{ $autoRefreshSeconds === 30 ? 'selected' : '' }}>30s</option>
                        <option value="60" {{ $autoRefreshSeconds === 60 ? 'selected' : '' }}>60s</option>
                        <option value="120" {{ $autoRefreshSeconds === 120 ? 'selected' : '' }}>120s</option>
                    </select>
                </div>
            </div>
            <div class="local-actions">
                <a class="btn btn-ghost" href="{{ url('/cola?storeId=' . $storeId) }}">Ver cola</a>
                <a class="btn btn-ghost" href="{{ url('/alertas?storeId=' . $storeId) }}">Ver alertas</a>
                @if($canOperate)
                    <a class="btn btn-primary" href="{{ url('/impresoras?storeId=' . $storeId) }}">Revisar impresoras</a>
                @endif
            </div>
        </form>
    </x-ui.card>

    <section class="local-kpi-grid">
        <article class="local-kpi {{ $queue > 0 ? 'is-warning' : '' }}">
            <span>Cola actual</span>
            <strong>{{ $queue }}</strong>
            <small>trabajos pendientes</small>
        </article>
        <article class="local-kpi {{ $failedNoRetry > 0 ? 'is-critical' : '' }}">
            <span>Sin reenviar</span>
            <strong>{{ $failedNoRetry }}</strong>
            <small>fallos activos</small>
        </article>
        <article class="local-kpi {{ $unassigned > 0 ? 'is-warning' : '' }}">
            <span>Sin asignar</span>
            <strong>{{ $unassigned }}</strong>
            <small>sin impresora</small>
        </article>
        <article class="local-kpi">
            <span>Impresoras activas</span>
            <strong>{{ $connectedPrinters }}</strong>
            <small>{{ count($printers) }} registradas</small>
        </article>
        <article class="local-kpi">
            <span>Recibidos</span>
            <strong>{{ $received }}</strong>
            <small>{{ $windowLabel }}</small>
        </article>
        <article class="local-kpi">
            <span>Impresos</span>
            <strong>{{ $printed }}</strong>
            <small>{{ $failed }} fallidos</small>
        </article>
    </section>

    <section class="local-main-grid">
        <x-ui.card class="local-attention-card">
            <div class="dbx-title-row">
                <h2 class="dbx-title">Requiere atencion</h2>
                <span class="dbx-subtle">Prioridad operativa</span>
            </div>
            @if(count($attentionItems) === 0)
                <div class="local-empty-state">
                    <strong>Sin incidencias activas</strong>
                    <span>La tienda esta dentro de los umbrales definidos.</span>
                </div>
            @else
                <div class="local-attention-list">
                    @foreach($attentionItems as $item)
                        @php
                            $href = ($canOperate || !str_contains($item['href'], '/impresoras')) ? $item['href'] : null;
                        @endphp
                        <div class="local-attention-item is-{{ $item['level'] }}">
                            <div>
                                <strong>{{ $item['title'] }}</strong>
                                <span>{{ $item['text'] }}</span>
                            </div>
                            @if($href)
                                <a class="btn btn-ghost" href="{{ $href }}">Abrir</a>
                            @endif
                        </div>
                    @endforeach
                </div>
            @endif
        </x-ui.card>

        <x-ui.card class="local-flow-card">
            <div class="dbx-title-row">
                <h2 class="dbx-title">Flujo del periodo</h2>
                <span class="dbx-subtle">{{ $windowLabel }}</span>
            </div>
            @php
                $flowBase = max(1, $received, $printed, $failed, $queue);
            @endphp
            <div class="local-flow-bars">
                @foreach([
                    ['label' => 'Recibidos', 'value' => $received, 'class' => 'info'],
                    ['label' => 'Impresos', 'value' => $printed, 'class' => 'success'],
                    ['label' => 'Fallidos', 'value' => $failed, 'class' => 'danger'],
                    ['label' => 'En cola', 'value' => $queue, 'class' => 'warning'],
                ] as $metric)
                    <div class="local-flow-row">
                        <span>{{ $metric['label'] }}</span>
                        <div><i class="{{ $metric['class'] }}" style="width: {{ (int) round(($metric['value'] / $flowBase) * 100) }}%"></i></div>
                        <strong>{{ $metric['value'] }}</strong>
                    </div>
                @endforeach
            </div>
        </x-ui.card>
    </section>

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
    const autoRefreshSeconds = {{ (int) ($autoRefreshSeconds ?? 0) }};
    if (autoRefreshSeconds > 0) {
        setTimeout(function() {
            window.location.reload();
        }, autoRefreshSeconds * 1000);
    }
})();
</script>
@endsection
