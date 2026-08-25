{{--
    Dashboard operativo — canvas "ImpresorasService Dashboard operativo", opcion 6a.

    Cuatro bloques, en este orden deliberado: primero lo que esta roto ahora mismo (estado actual +
    incidencias accionables), despues el balance del periodo. Los dos primeros bloques NO dependen
    del filtro de periodo (A-KPI-01) y los rotulos lo dicen explicitamente, que era justo la
    confusion que producia el widget anterior de barras con porcentajes.
--}}
@php
    $opStores = is_array($stores ?? null) ? $stores : [];
    $opAlerts = is_array($alerts ?? null) ? $alerts : [];

    $fmtNum = static fn (int $v): string => number_format($v, 0, ',', ' ');

    // "Desde" de cada incidencia. Relativo, con el instante exacto en el title.
    $sinceLabel = static function ($iso): string {
        $raw = trim((string) ($iso ?? ''));
        if ($raw === '') {
            return 'sin comprobar';
        }
        $ts = strtotime($raw);
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

    // Indice por tienda: la tabla de incidencias necesita nombrar la impresora concreta que falla,
    // y esos campos solo viven en stores[].printers.
    $opStoreById = [];
    foreach ($opStores as $opStore) {
        $opStoreById[(string) ($opStore['storeId'] ?? '')] = $opStore;
    }

    $findPrinter = static function ($storeId, string $wantedStatus) use ($opStoreById, $printerConnectivityStatus): ?array {
        $store = $opStoreById[(string) $storeId] ?? null;
        $printers = is_array($store['printers'] ?? null) ? $store['printers'] : [];
        foreach ($printers as $printer) {
            if (is_array($printer) && $printerConnectivityStatus($printer) === $wantedStatus) {
                return $printer;
            }
        }
        return null;
    };

    // --- Bloque 1: estado actual -------------------------------------------------------------
    $opQueue = (int) ($kpis['queueCurrent'] ?? 0);
    $opFailedNoRetry = (int) ($kpis['failedWithoutRetryCurrent'] ?? 0);
    $opUnassigned = 0;
    $opPrintersOffline = 0;
    $opPrintersNoHost = 0;
    $opPrintersUnchecked = 0;
    $opPrintersOk = 0;
    $opPrintersTotal = 0;
    $opCritical = 0;
    $opWarning = 0;
    foreach ($opStores as $opStore) {
        $opUnassigned += (int) ($opStore['unassignedQueueCurrent'] ?? 0);
        $opHealth = strtolower((string) ($opStore['health'] ?? 'healthy'));
        if ($opHealth === 'critical') {
            $opCritical++;
        } elseif ($opHealth === 'warning') {
            $opWarning++;
        }
        foreach ((is_array($opStore['printers'] ?? null) ? $opStore['printers'] : []) as $opPrinter) {
            if (!is_array($opPrinter)) {
                continue;
            }
            $opPrintersTotal++;
            match ($printerConnectivityStatus($opPrinter)) {
                'error' => $opPrintersOffline++,
                'no_host' => $opPrintersNoHost++,
                'ok' => $opPrintersOk++,
                default => $opPrintersUnchecked++,
            };
        }
    }
    $opPrintersBad = $opPrintersOffline + $opPrintersNoHost + $opPrintersUnchecked;
    $opPrintersBreakdown = implode(' · ', array_filter([
        $opPrintersOffline > 0 ? $opPrintersOffline . ' sin conexión' : null,
        $opPrintersNoHost > 0 ? $opPrintersNoHost . ' sin host' : null,
        $opPrintersUnchecked > 0 ? $opPrintersUnchecked . ' sin comprobar' : null,
    ])) ?: 'todas responden';

    // --- Bloque 3: rendimiento del periodo ---------------------------------------------------
    $opReceived = (int) ($kpis['received'] ?? 0);
    $opPrinted = (int) ($kpis['printed'] ?? 0);
    $opFailed = (int) ($kpis['failed'] ?? 0);
    // Igual que en el resto del dashboard: sin recibidos no hay tasa, no un 100% ficticio.
    $opSuccessRate = $opReceived > 0 ? (int) round(($opPrinted / $opReceived) * 100) : null;

    // --- Bloque 4: salud por tienda ----------------------------------------------------------
    $opRank = ['critical' => 0, 'warning' => 1, 'info' => 2, 'healthy' => 3];
    $opSorted = $opStores;
    usort($opSorted, static function (array $a, array $b) use ($opRank): int {
        $byHealth = ($opRank[strtolower((string) ($a['health'] ?? 'healthy'))] ?? 3)
            <=> ($opRank[strtolower((string) ($b['health'] ?? 'healthy'))] ?? 3);
        if ($byHealth !== 0) {
            return $byHealth;
        }
        $byFailed = (int) ($b['failedWithoutRetryCurrent'] ?? 0) <=> (int) ($a['failedWithoutRetryCurrent'] ?? 0);
        return $byFailed !== 0 ? $byFailed : (int) ($b['queuedCurrent'] ?? 0) <=> (int) ($a['queuedCurrent'] ?? 0);
    });
    $opTopStores = array_slice($opSorted, 0, 6);
    $opDetalleUrl = route('dashboard', array_merge(request()->except(['tab', 'storeView']), ['tab' => 'global', 'storeView' => 'detalle']));
@endphp

<div class="dbx-op">

    {{-- 1. Estado actual de la operacion --------------------------------------------------- --}}
    <section class="dbx-op-card {{ $opCritical > 0 ? 'dbx-op-card-alert' : '' }}">
        <div class="dbx-op-body">
            <div class="dbx-op-head">
                <h2 class="dbx-op-title">Estado actual de la operaci&oacute;n</h2>
                @if($opCritical > 0)
                    <span class="dbx-op-meta-alert">REQUIERE ATENCI&Oacute;N &middot; {{ $opCritical }} {{ $opCritical === 1 ? 'TIENDA CRÍTICA' : 'TIENDAS CRÍTICAS' }}</span>
                @elseif($opWarning > 0)
                    <span class="dbx-op-meta-alert" style="color: var(--ui-warning)">{{ $opWarning }} {{ $opWarning === 1 ? 'TIENDA EN AVISO' : 'TIENDAS EN AVISO' }}</span>
                @else
                    <span class="dbx-op-meta-ok">OPERACI&Oacute;N NORMAL</span>
                @endif
            </div>
            <div class="dbx-op-tiles">
                <div class="dbx-op-tile">
                    <span class="dbx-op-tile-label">EN COLA</span>
                    <strong class="dbx-op-tile-value">{{ $fmtNum($opQueue) }}</strong>
                    <span class="dbx-op-tile-note">trabajos esperando</span>
                </div>
                <div class="dbx-op-tile {{ $opFailedNoRetry > 0 ? 'dbx-op-tile-alert' : '' }}">
                    <span class="dbx-op-tile-label">SIN REENVIAR</span>
                    <strong class="dbx-op-tile-value">{{ $fmtNum($opFailedNoRetry) }}</strong>
                    <span class="dbx-op-tile-note">necesitan seguimiento</span>
                </div>
                <div class="dbx-op-tile {{ $opPrintersOffline > 0 ? 'dbx-op-tile-alert' : '' }}">
                    <span class="dbx-op-tile-label">IMPRESORAS CON PROBLEMA</span>
                    <strong class="dbx-op-tile-value">{{ $fmtNum($opPrintersBad) }}</strong>
                    <span class="dbx-op-tile-note">{{ $opPrintersBreakdown }}</span>
                </div>
                <div class="dbx-op-tile">
                    <span class="dbx-op-tile-label">SIN IMPRESORA</span>
                    <strong class="dbx-op-tile-value">{{ $fmtNum($opUnassigned) }}</strong>
                    <span class="dbx-op-tile-note">trabajos sin asignar</span>
                </div>
            </div>
        </div>
    </section>

    {{-- 2. Incidencias priorizadas ----------------------------------------------------------- --}}
    @if($isAdminUser)
        @php
            // Una fila por incidencia. Las de $alerts vienen de la Api (misma fuente que Telegram);
            // "sin impresora asignada" se deriva de unassignedQueueCurrent, que no genera alerta
            // propia en la Api pero si es accionable y el diseno la pide.
            $opIncidents = [];
            foreach ($opAlerts as $opAlert) {
                $sid = $opAlert['storeId'] ?? null;
                if ($sid === null || $sid === '') {
                    continue;
                }
                $severity = strtolower((string) ($opAlert['health'] ?? 'warning'));
                $reason = (string) ($opAlert['healthReason'] ?? '');
                $lower = strtolower($reason);
                $store = $opStoreById[(string) $sid] ?? [];
                $queued = (int) ($opAlert['queuedCurrent'] ?? 0);
                $noRetry = (int) ($opAlert['failedWithoutRetryCurrent'] ?? 0);
                $sidParam = urlencode((string) $sid);

                if (str_contains($lower, 'sin impresoras activas') || str_contains($lower, 'no hay impresoras activas')) {
                    $opIncidents[] = [
                        'storeId' => $sid, 'storeName' => $opAlert['storeName'] ?? null, 'severity' => $severity,
                        'kicker' => 'IMPRESORAS',
                        'lead' => 'Sin impresoras activas',
                        'tail' => $queued > 0 ? $queued . ' trabajo(s) en cola sin salida posible' : 'la tienda no puede imprimir',
                        'since' => $store['oldestQueuedAtUtc'] ?? null,
                        'action' => ['Ver impresoras', '/impresoras?storeId=' . $sidParam],
                        'link' => ['Ver en cola', '/cola?storeId=' . $sidParam . '&status=0'],
                    ];
                } elseif (str_contains($lower, 'sin host')) {
                    $printer = $findPrinter($sid, 'no_host');
                    $opIncidents[] = [
                        'storeId' => $sid, 'storeName' => $opAlert['storeName'] ?? null, 'severity' => $severity,
                        'kicker' => 'SIN HOST',
                        'lead' => trim(($printer['printerName'] ?? 'Impresora') . ' sin host configurado'),
                        'tail' => 'no puede recibir trabajos',
                        'since' => $printer['lastConnectionCheckAtUtc'] ?? null,
                        'action' => ['Configurar host', $printer !== null
                            ? '/impresoras?storeId=' . $sidParam . '&printerId=' . (int) $printer['printerId']
                            : '/impresoras?storeId=' . $sidParam],
                        'link' => ['Ver todas las impresoras', '/impresoras?storeId=' . $sidParam],
                    ];
                } elseif (str_contains($lower, 'sin reenviar')) {
                    $opIncidents[] = [
                        'storeId' => $sid, 'storeName' => $opAlert['storeName'] ?? null, 'severity' => $severity,
                        'kicker' => 'SIN REENVIAR',
                        'lead' => $noRetry . ' ' . ($noRetry === 1 ? 'trabajo sin reenviar' : 'trabajos sin reenviar'),
                        'tail' => 'reintentos agotados y nadie los ha reenviado',
                        'since' => $store['oldestFailedWithoutRetryAtUtc'] ?? null,
                        // Navega al listado ya filtrado: el reenvio se confirma alli, no a ciegas.
                        'action' => ['Reenviar ' . $noRetry, '/cola?storeId=' . $sidParam . '&failedWithoutRetry=1'],
                        'link' => ['Ver toda la cola', '/cola?storeId=' . $sidParam],
                    ];
                } elseif (str_contains($lower, 'conectividad') || str_contains($lower, 'conexion')) {
                    $printer = $findPrinter($sid, 'error');
                    $streak = (int) ($printer['connectionFailuresStreak'] ?? 0);
                    $transport = trim((string) ($printer['lastConnectionTransport'] ?? ''));
                    $detail = array_filter([
                        $streak > 0 ? $streak . ' ' . ($streak === 1 ? 'fallo consecutivo' : 'fallos consecutivos') : null,
                        $transport !== '' ? 'transporte ' . $transport : null,
                    ]);
                    $opIncidents[] = [
                        'storeId' => $sid, 'storeName' => $opAlert['storeName'] ?? null, 'severity' => $severity,
                        'kicker' => 'CONECTIVIDAD',
                        'lead' => $printer !== null
                            ? trim($printer['printerName'] . ' sin conexión')
                            : 'Impresoras pendientes de comprobación',
                        'tail' => $detail !== [] ? implode(', ', $detail) : 'revisar conectividad de la tienda',
                        'since' => $printer['lastConnectionCheckAtUtc'] ?? null,
                        'action' => ['Probar conexión', $printer !== null
                            ? '/impresoras?storeId=' . $sidParam . '&printerId=' . (int) $printer['printerId']
                            : '/impresoras?storeId=' . $sidParam],
                        'link' => ['Ver todas las impresoras', '/impresoras?storeId=' . $sidParam],
                    ];
                } elseif (str_contains($lower, 'cola') || str_contains($lower, 'trabajos')) {
                    $umbral = $severity === 'critical' ? $criticalQueueMin : $warningQueueMin;
                    $opIncidents[] = [
                        'storeId' => $sid, 'storeName' => $opAlert['storeName'] ?? null, 'severity' => $severity,
                        'kicker' => 'COLA',
                        'lead' => 'Cola de ' . $queued . ' ' . ($queued === 1 ? 'trabajo' : 'trabajos'),
                        'tail' => 'por encima del umbral ' . ($severity === 'critical' ? 'crítico' : 'de aviso') . ' (' . $umbral . ')',
                        'since' => $store['oldestQueuedAtUtc'] ?? null,
                        'action' => ['Ver en cola', '/cola?storeId=' . $sidParam . '&status=0'],
                        'link' => ['Ver impresoras', '/impresoras?storeId=' . $sidParam],
                    ];
                } else {
                    $opIncidents[] = [
                        'storeId' => $sid, 'storeName' => $opAlert['storeName'] ?? null, 'severity' => $severity,
                        'kicker' => 'OTRO',
                        'lead' => $reason !== '' ? $reason : 'Incidencia sin clasificar',
                        'tail' => '',
                        'since' => null,
                        'action' => ['Ver en cola', '/cola?storeId=' . $sidParam],
                        'link' => ['Ver impresoras', '/impresoras?storeId=' . $sidParam],
                    ];
                }
            }

            foreach ($opStores as $opStore) {
                $unassigned = (int) ($opStore['unassignedQueueCurrent'] ?? 0);
                if ($unassigned <= 0) {
                    continue;
                }
                $sidParam = urlencode((string) ($opStore['storeId'] ?? ''));
                $opIncidents[] = [
                    'storeId' => $opStore['storeId'] ?? null, 'storeName' => $opStore['storeName'] ?? null,
                    'severity' => 'warning',
                    'kicker' => 'SIN IMPRESORA ASIGNADA',
                    'lead' => $unassigned . ' ' . ($unassigned === 1 ? 'trabajo sin impresora' : 'trabajos sin impresora'),
                    'tail' => 'ninguna regla de enrutado coincide',
                    'since' => $opStore['oldestQueuedAtUtc'] ?? null,
                    'action' => $isAdminUser
                        ? ['Reasignar impresora', route('reglas.index')]
                        : ['Ver los sin impresora', '/cola?storeId=' . $sidParam . '&unassigned=1'],
                    'link' => ['Ver los sin impresora', '/cola?storeId=' . $sidParam . '&unassigned=1'],
                ];
            }

            usort($opIncidents, static fn (array $a, array $b): int =>
                ($opRank[$a['severity']] ?? 3) <=> ($opRank[$b['severity']] ?? 3));
        @endphp

        <section class="dbx-op-card {{ $opIncidents !== [] ? 'dbx-op-card-alert' : '' }}">
            <div class="dbx-op-body">
                <div class="dbx-op-head">
                    <h2 class="dbx-op-title">Incidencias priorizadas &mdash; acci&oacute;n requerida</h2>
                    <span class="dbx-op-meta">
                        {{ count($opIncidents) }} {{ count($opIncidents) === 1 ? 'abierta' : 'abiertas' }}
                        &middot; umbrales: cola {{ $warningQueueMin }} / {{ $criticalQueueMin }}
                        &middot; sin reenviar {{ $warningFailedMin }} / {{ $criticalFailedMin }}
                    </span>
                </div>
                @if($opIncidents === [])
                    <p class="dbx-op-empty">Sin incidencias abiertas para el &aacute;mbito seleccionado.</p>
                @else
                    <div class="dbx-op-table-wrap">
                        <table class="dbx-op-table">
                            <thead>
                                <tr>
                                    <th style="width:172px">Tienda</th>
                                    <th style="width:104px">Severidad</th>
                                    <th>Incidencia</th>
                                    <th style="width:112px">Desde</th>
                                    <th style="width:196px">Acci&oacute;n</th>
                                </tr>
                            </thead>
                            <tbody>
                                @foreach($opIncidents as $incident)
                                    <tr class="is-{{ $incident['severity'] }}">
                                        <td class="dbx-op-store">{{ $formatStoreLabel($incident['storeId'], $incident['storeName']) }}</td>
                                        <td><span class="dbx-op-sev dbx-op-sev-{{ $incident['severity'] }}">{{ ['critical' => 'CRÍTICO', 'warning' => 'AVISO', 'info' => 'INFO'][$incident['severity']] ?? 'AVISO' }}</span></td>
                                        <td class="dbx-op-cell-text">
                                            <span class="dbx-op-kicker">{{ $incident['kicker'] }}</span>
                                            <span class="dbx-op-text"><b>{{ $incident['lead'] }}</b>@if($incident['tail'] !== '') &mdash; {{ $incident['tail'] }}@endif</span>
                                        </td>
                                        <td class="dbx-op-since" title="{{ $incident['since'] ?? 'Sin dato de inicio' }}">{{ $sinceLabel($incident['since']) }}</td>
                                        <td>
                                            <a href="{{ url($incident['action'][1]) }}" class="dbx-op-action {{ $incident['severity'] === 'critical' ? 'dbx-op-action-critical' : '' }}">{{ $incident['action'][0] }}</a>
                                            <a href="{{ url($incident['link'][1]) }}" class="dbx-op-action-link">{{ $incident['link'][0] }}</a>
                                        </td>
                                    </tr>
                                @endforeach
                            </tbody>
                        </table>
                    </div>
                @endif
                <p class="dbx-op-foot">Regla de salud configurable por umbrales de cola, fallos sin reenviar y conectividad.</p>
            </div>
        </section>
    @endif

    {{-- 3. Rendimiento del periodo ------------------------------------------------------------ --}}
    <section class="dbx-op-card">
        <div class="dbx-op-body">
            <div class="dbx-op-head">
                <h2 class="dbx-op-title">Rendimiento del periodo &mdash; {{ $windowLabel }}</h2>
                <span class="dbx-op-meta">
                    No incluye estado actual &middot;
                    {{ $opReceived > 0 ? 'porcentajes sobre ' . $fmtNum($opReceived) . ' recibidos' : 'sin trabajos recibidos en el periodo' }}
                </span>
            </div>
            <div class="dbx-op-tiles dbx-op-tiles-kpi">
                <div class="dbx-op-tile">
                    <span class="dbx-op-tile-label">RECIBIDOS</span>
                    <strong class="dbx-op-tile-value">{{ $fmtNum($opReceived) }}</strong>
                </div>
                <div class="dbx-op-tile">
                    <span class="dbx-op-tile-label">IMPRESOS</span>
                    <strong class="dbx-op-tile-value">{{ $fmtNum($opPrinted) }}</strong>
                </div>
                <div class="dbx-op-tile {{ $opFailed > 0 ? 'dbx-op-tile-alert' : '' }}">
                    <span class="dbx-op-tile-label">FALLIDOS</span>
                    <strong class="dbx-op-tile-value">{{ $fmtNum($opFailed) }}</strong>
                </div>
                <div class="dbx-op-tile dbx-op-tile-rate">
                    <span class="dbx-op-tile-label">TASA DE &Eacute;XITO</span>
                    <strong class="dbx-op-tile-value">{{ $opSuccessRate === null ? '—' : $opSuccessRate . ' %' }}</strong>
                    <span class="dbx-op-rate-bar"><span style="width: {{ $opSuccessRate ?? 0 }}%"></span></span>
                </div>
            </div>
        </div>
    </section>

    {{-- 4. Salud por tienda -------------------------------------------------------------------- --}}
    <section class="dbx-op-card">
        <div class="dbx-op-body">
            <div class="dbx-op-head">
                <h2 class="dbx-op-title">Salud por tienda &mdash; estado actual</h2>
                <span class="dbx-op-meta">Problem&aacute;ticas primero &middot; {{ count($opTopStores) }} de {{ count($opStores) }}</span>
            </div>
            @if($opTopStores === [])
                <p class="dbx-op-empty">No hay tiendas para los filtros actuales.</p>
            @else
                <div class="dbx-op-table-wrap">
                    <table class="dbx-op-table dbx-op-table-stores">
                        <thead>
                            <tr>
                                <th>Tienda</th>
                                <th style="width:92px">Salud</th>
                                <th style="width:224px">Motivo</th>
                                <th style="width:78px">Cola</th>
                                <th style="width:110px">Sin reenviar</th>
                                <th style="width:112px">Impresoras</th>
                                <th style="width:186px">Ir a</th>
                            </tr>
                        </thead>
                        <tbody>
                            @foreach($opTopStores as $opStore)
                                @php
                                    $sHealth = strtolower((string) ($opStore['health'] ?? 'healthy'));
                                    $sQueue = (int) ($opStore['queuedCurrent'] ?? 0);
                                    $sFailed = (int) ($opStore['failedWithoutRetryCurrent'] ?? 0);
                                    $sPrinters = is_array($opStore['printers'] ?? null) ? $opStore['printers'] : [];
                                    $sOk = 0;
                                    foreach ($sPrinters as $sPrinter) {
                                        if (is_array($sPrinter) && $printerConnectivityStatus($sPrinter) === 'ok') {
                                            $sOk++;
                                        }
                                    }
                                    $sTotal = count($sPrinters);
                                    $sParam = urlencode((string) ($opStore['storeId'] ?? ''));
                                    $sLabel = match ($sHealth) {
                                        'critical' => 'CRÍTICA',
                                        'warning' => 'AVISO',
                                        'info' => 'INFO',
                                        default => 'SALUDABLE',
                                    };
                                @endphp
                                <tr class="is-{{ $sHealth }}">
                                    <td class="dbx-op-store">{{ $formatStoreLabel($opStore['storeId'] ?? null, $opStore['storeName'] ?? null) }}</td>
                                    <td><span class="dbx-op-sev dbx-op-sev-{{ $sHealth }}">{{ $sLabel }}</span></td>
                                    <td class="dbx-op-cell-text dbx-op-since">{{ $opStore['healthReason'] ?? 'Operacion dentro de umbrales' }}</td>
                                    <td><span class="dbx-op-num {{ $sQueue > 0 ? 'dbx-op-num-queue' : '' }}">{{ $fmtNum($sQueue) }}</span></td>
                                    <td><span class="dbx-op-num {{ $sFailed > 0 ? 'dbx-op-num-failed' : '' }}">{{ $fmtNum($sFailed) }}</span></td>
                                    <td><span class="dbx-op-num-muted {{ $sTotal > 0 && $sOk < $sTotal ? 'dbx-op-num-bad' : '' }}">{{ $sOk }} / {{ $sTotal }} OK</span></td>
                                    <td>
                                        <span class="dbx-op-links">
                                            <a href="{{ url('/cola?storeId=' . $sParam) }}">Cola</a>
                                            <a href="{{ url('/impresoras?storeId=' . $sParam) }}">Impresoras</a>
                                        </span>
                                    </td>
                                </tr>
                            @endforeach
                        </tbody>
                    </table>
                </div>
            @endif
            <div class="dbx-op-more">
                <a href="{{ $opDetalleUrl }}">Ver las {{ count($opStores) }} tiendas &rarr;</a>
            </div>
        </div>
    </section>

</div>
