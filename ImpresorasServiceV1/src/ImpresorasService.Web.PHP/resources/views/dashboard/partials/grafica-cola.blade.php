{{--
    Evolucion del periodo. Una serie visible cada vez, elegida con las pestanas: cola, recibidos,
    impresos y fallidos son los cuatro KPI que ya pinta el bloque de tiles justo encima, asi que la
    grafica los explica en el tiempo sin duplicar widgets.

    Todas las series se pre-renderizan en el SVG con su propia escala y solo se conmuta la
    visibilidad: sin recalculo en cliente y sin peticion extra, los cuatro campos ya vienen en
    /api/dashboard/timeseries.

    La linea del umbral critico es el motivo de que esta grafica exista. Un "47 en cola" no dice
    si hay que actuar; un 47 cruzando la linea de 30, si. Solo aplica a la serie de cola: en las
    demas no hay umbral configurado y una linea de referencia inventada seria ruido.

    Con tramos diarios (7d/30d) la serie de cola no se ofrece: es una foto del instante y no se
    puede promediar por dia sin mentir. Ahi la grafica arranca en Recibidos.

    Sin leyenda a proposito: con una unica serie visible la pestana activa ya la nombra.
--}}
@php
    $pts = is_array($timeseries ?? null) ? array_values($timeseries) : [];
    $n = count($pts);
@endphp

@if($n >= 2)
    @php
        $W = 1160; $H = 260;
        $padL = 46; $padR = 16; $padT = 18; $padB = 34;
        $plotW = $W - $padL - $padR;
        $plotH = $H - $padT - $padB;

        $critical = max(1, (int) ($criticalQueueMin ?? 30));

        $px = static fn (int $i): float => $padL + $i * $plotW / ($n - 1);

        // Escala redondeada al alza: el tope del eje es un multiplo del paso, no el maximo crudo.
        $scaleOf = static function (array $values, int $floor): array {
            $rawMax = max(max($values), $floor, 1);
            $step = $rawMax <= 10 ? 2 : ($rawMax <= 50 ? 10 : ($rawMax <= 200 ? 25 : 100));
            $maxY = max($step, (int) (ceil($rawMax / $step) * $step));
            return [$maxY, [0, (int) round($maxY / 2), $maxY]];
        };

        // Tramo horario en "hoy"; en 7d/30d el bucket de la Api es diario y la hora no distingue.
        $bucketDaily = ($window ?? 'today') !== 'today';
        $stamp = static function ($iso) use ($bucketDaily): string {
            $ts = strtotime((string) $iso);
            return $ts === false ? '' : gmdate($bucketDaily ? 'd/m' : 'H:i', $ts);
        };

        $series = [
            'queue'    => ['label' => 'En cola',   'field' => 'queueDepth', 'noun' => 'en cola',   'threshold' => $critical, 'tone' => ''],
            'received' => ['label' => 'Recibidos', 'field' => 'received',   'noun' => 'recibidos', 'threshold' => null,      'tone' => ''],
            'printed'  => ['label' => 'Impresos',  'field' => 'printed',    'noun' => 'impresos',  'threshold' => null,      'tone' => ''],
            'failed'   => ['label' => 'Fallidos',  'field' => 'failed',     'noun' => 'fallidos',  'threshold' => null,      'tone' => 'is-accent'],
        ];

        // La cola es una foto del instante, no un acumulado: por tramos de un dia el valor que se
        // reconstruye no representa nada que se pueda leer ("cuantos habia en cola el martes" no
        // tiene una sola respuesta). Solo se ofrece con tramos horarios.
        if ($bucketDaily) {
            unset($series['queue']);
        }
        $defaultSerie = array_key_first($series);

        foreach ($series as $key => $def) {
            $values = array_map(static fn ($p) => max(0, (int) ($p[$def['field']] ?? 0)), $pts);
            [$maxY, $ticks] = $scaleOf($values, (int) ($def['threshold'] ?? 0));
            $py = static fn (int $v): float => $padT + $plotH - ($v / $maxY) * $plotH;

            $coords = [];
            foreach ($values as $i => $v) {
                $coords[] = round($px($i), 1) . ',' . round($py($v), 1);
            }
            $line = implode(' ', $coords);

            $peak = max($values);
            $peakAt = array_search($peak, $values, true);

            $series[$key]['maxY'] = $maxY;
            $series[$key]['ticksY'] = array_map(static fn (int $t) => ['v' => $t, 'y' => round($py($t), 1)], $ticks);
            $series[$key]['line'] = $line;
            $series[$key]['area'] = $line . ' ' . round($px($n - 1), 1) . ',' . round($py(0), 1)
                . ' ' . round($px(0), 1) . ',' . round($py(0), 1);
            $series[$key]['thresholdY'] = $def['threshold'] !== null && $def['threshold'] <= $maxY
                ? round($py((int) $def['threshold']), 1)
                : null;
            $series[$key]['total'] = array_sum($values);
            $series[$key]['peak'] = $peak;
            $series[$key]['peakAt'] = $stamp($pts[$peakAt]['startUtc'] ?? $pts[$peakAt]['StartUtc'] ?? '');
        }

        // Etiquetas de eje X: como mucho seis, para que no se solapen en pantallas estrechas.
        $everyX = max(1, (int) ceil($n / 6));

        // Datos del tooltip, ya formateados: una entrada por tramo con los cuatro KPI.
        $tip = [];
        foreach ($pts as $p) {
            $tip[] = [
                'l' => $stamp($p['startUtc'] ?? $p['StartUtc'] ?? ''),
                'queue' => (int) ($p['queueDepth'] ?? 0),
                'received' => (int) ($p['received'] ?? 0),
                'printed' => (int) ($p['printed'] ?? 0),
                'failed' => (int) ($p['failed'] ?? 0),
            ];
        }
        $nouns = array_map(static fn (array $d) => $d['noun'], $series);
    @endphp

    <div class="dbx-op-chart" data-queue-chart
         data-points="{{ json_encode($tip, JSON_UNESCAPED_UNICODE) }}"
         data-nouns="{{ json_encode($nouns, JSON_UNESCAPED_UNICODE) }}">
        <div class="dbx-op-chart-head">
            <div class="dbx-op-chart-heading">
                <h3 class="dbx-op-chart-title">Evoluci&oacute;n del periodo</h3>
                @foreach($series as $key => $s)
                    <span class="dbx-op-meta" data-serie-meta="{{ $key }}" @if($key !== $defaultSerie) hidden @endif>
                        @if($key === 'queue')
                            Pico de {{ $s['peak'] }} en {{ $s['peakAt'] ?: '--' }}
                        @else
                            {{ number_format($s['total'], 0, ',', ' ') }} en el periodo &middot; pico de {{ $s['peak'] }} en {{ $s['peakAt'] ?: '--' }}
                        @endif
                        &middot; {{ $bucketDaily ? 'tramos diarios' : 'horas' }} UTC
                    </span>
                @endforeach
            </div>
            <div class="dbx-op-chart-tabs" role="tablist" aria-label="KPI de la gr&aacute;fica">
                @foreach($series as $key => $s)
                    <button type="button" class="dbx-op-chart-tab {{ $key === $defaultSerie ? 'is-active' : '' }}"
                            role="tab" aria-selected="{{ $key === $defaultSerie ? 'true' : 'false' }}"
                            data-serie-tab="{{ $key }}">{{ $s['label'] }}</button>
                @endforeach
            </div>
        </div>

        <svg class="dbx-op-chart-svg" viewBox="0 0 {{ $W }} {{ $H }}" role="img"
             aria-label="Evoluci&oacute;n del periodo por tramo. La serie visible se elige en las pesta&ntilde;as.">
            @foreach($series as $key => $s)
                <g data-serie="{{ $key }}" @if($key !== $defaultSerie) hidden @endif>
                    {{-- Guias horizontales, recesivas: sirven de escala, no compiten con el dato. --}}
                    @foreach($s['ticksY'] as $t)
                        <line class="dbx-op-chart-grid" x1="{{ $padL }}" x2="{{ $W - $padR }}"
                              y1="{{ $t['y'] }}" y2="{{ $t['y'] }}"></line>
                        <text class="dbx-op-chart-axis" x="{{ $padL - 9 }}" y="{{ $t['y'] + 4 }}"
                              text-anchor="end">{{ $t['v'] }}</text>
                    @endforeach

                    <polygon class="dbx-op-chart-area {{ $s['tone'] }}" points="{{ $s['area'] }}"></polygon>
                    <polyline class="dbx-op-chart-line {{ $s['tone'] }}" points="{{ $s['line'] }}"></polyline>

                    {{-- El umbral va por encima del area para que se lea siempre. --}}
                    @if($s['thresholdY'] !== null)
                        <line class="dbx-op-chart-threshold" x1="{{ $padL }}" x2="{{ $W - $padR }}"
                              y1="{{ $s['thresholdY'] }}" y2="{{ $s['thresholdY'] }}"></line>
                        <text class="dbx-op-chart-threshold-label" x="{{ $padL + 6 }}"
                              y="{{ $s['thresholdY'] - 7 }}">Umbral cr&iacute;tico {{ $s['threshold'] }}</text>
                    @endif
                </g>
            @endforeach

            @foreach($tip as $i => $t)
                @if($i % $everyX === 0)
                    <text class="dbx-op-chart-axis" x="{{ round($px($i), 1) }}" y="{{ $H - 11 }}"
                          text-anchor="middle">{{ $t['l'] }}</text>
                @endif
            @endforeach

            <line class="dbx-op-chart-cursor" x1="0" x2="0" y1="{{ $padT }}" y2="{{ $padT + $plotH }}" hidden></line>
            <circle class="dbx-op-chart-dot" r="5" cx="0" cy="0" hidden></circle>
            <rect class="dbx-op-chart-hit" x="{{ $padL }}" y="{{ $padT }}"
                  width="{{ $plotW }}" height="{{ $plotH }}"></rect>
        </svg>

        <div class="dbx-op-chart-tip" role="status" hidden></div>
    </div>
{{-- Conmutador de serie, cruceta y tooltip. Delegado en document a proposito: el dashboard se
     reemplaza entero en cada refresco automatico y asi no hay que volver a enganchar nada. --}}
<script>
(function() {
    function activeSerie(chart) {
        // La serie de cola no se ofrece con tramos diarios, asi que el respaldo es la primera que
        // haya pintado el servidor, no un nombre fijo.
        const group = chart.querySelector('[data-serie]:not([hidden])') || chart.querySelector('[data-serie]');
        return group ? group.dataset.serie : '';
    }

    function hide(chart) {
        chart.querySelectorAll('.dbx-op-chart-cursor, .dbx-op-chart-dot').forEach(function(el) {
            el.setAttribute('hidden', '');
        });
        const tip = chart.querySelector('.dbx-op-chart-tip');
        if (tip) tip.setAttribute('hidden', '');
    }

    function select(chart, serie) {
        chart.querySelectorAll('[data-serie]').forEach(function(group) {
            group.toggleAttribute('hidden', group.dataset.serie !== serie);
        });
        chart.querySelectorAll('[data-serie-meta]').forEach(function(meta) {
            meta.toggleAttribute('hidden', meta.dataset.serieMeta !== serie);
        });
        chart.querySelectorAll('[data-serie-tab]').forEach(function(tab) {
            const on = tab.dataset.serieTab === serie;
            tab.classList.toggle('is-active', on);
            tab.setAttribute('aria-selected', on ? 'true' : 'false');
        });
        hide(chart);
    }

    document.addEventListener('click', function(event) {
        const target = event.target instanceof Element ? event.target : null;
        const tab = target ? target.closest('[data-serie-tab]') : null;
        if (!tab) return;
        const chart = tab.closest('[data-queue-chart]');
        if (!chart) return;

        select(chart, tab.dataset.serieTab);
        try {
            sessionStorage.setItem('dashboard.chart.serie', tab.dataset.serieTab);
        } catch {}
    });

    // El refresco automatico reemplaza el root del dashboard y los <script> insertados asi no se
    // ejecutan: dashboard.blade.php llama a este hook para devolver la serie que estaba elegida.
    window.dbxRestoreChartSerie = function(root) {
        let serie;
        try {
            serie = sessionStorage.getItem('dashboard.chart.serie');
        } catch {
            return;
        }
        if (!serie) return;
        (root || document).querySelectorAll('[data-queue-chart]').forEach(function(chart) {
            if (chart.querySelector('[data-serie-tab="' + serie + '"]')) select(chart, serie);
        });
    };
    window.dbxRestoreChartSerie(document);

    document.addEventListener('mousemove', function(event) {
        const target = event.target instanceof Element ? event.target : null;
        const chart = target ? target.closest('[data-queue-chart]') : null;
        if (!chart) return;

        const svg = chart.querySelector('.dbx-op-chart-svg');
        const serie = activeSerie(chart);
        const line = chart.querySelector('[data-serie="' + serie + '"] .dbx-op-chart-line');
        const cursor = chart.querySelector('.dbx-op-chart-cursor');
        const dot = chart.querySelector('.dbx-op-chart-dot');
        const tip = chart.querySelector('.dbx-op-chart-tip');
        if (!svg || !line || !cursor || !dot || !tip) return;

        let points, nouns;
        try {
            points = JSON.parse(chart.dataset.points || '[]');
            nouns = JSON.parse(chart.dataset.nouns || '{}');
        } catch (error) {
            return;
        }
        if (points.length < 2) return;

        const coords = line.getAttribute('points').trim().split(/\s+/).map(function(pair) {
            const parts = pair.split(',');
            return [parseFloat(parts[0]), parseFloat(parts[1])];
        });

        const box = svg.getBoundingClientRect();
        if (box.width === 0) return;
        const ratio = svg.viewBox.baseVal.width / box.width;
        const svgX = (event.clientX - box.left) * ratio;

        let best = 0;
        for (let i = 1; i < coords.length; i++) {
            if (Math.abs(coords[i][0] - svgX) < Math.abs(coords[best][0] - svgX)) best = i;
        }

        const cx = coords[best][0];
        const cy = coords[best][1];
        const point = points[best];

        cursor.setAttribute('x1', cx);
        cursor.setAttribute('x2', cx);
        cursor.removeAttribute('hidden');
        dot.setAttribute('cx', cx);
        dot.setAttribute('cy', cy);
        dot.removeAttribute('hidden');

        tip.textContent = point.l + ' · ' + point[serie] + ' ' + (nouns[serie] || '');
        // Se recorta contra los bordes para que no se salga de la tarjeta.
        const left = Math.min(Math.max(cx / ratio, 70), box.width - 70);
        tip.style.left = left + 'px';
        tip.style.top = (cy / ratio - 8) + 'px';
        tip.removeAttribute('hidden');
    });

    document.addEventListener('mouseout', function(event) {
        const target = event.target instanceof Element ? event.target : null;
        const chart = target ? target.closest('[data-queue-chart]') : null;
        if (!chart) return;
        const to = event.relatedTarget;
        if (to instanceof Element && chart.contains(to)) return;
        hide(chart);
    });
})();
</script>
@else
    {{-- Que la ausencia de grafica no sea muda: si la Api no devuelve serie (endpoint caido o sin
         desplegar) el hueco se explica en vez de desaparecer sin dejar rastro. --}}
    <p class="dbx-op-empty dbx-op-chart-missing">
        Sin serie temporal para el periodo &mdash; <code>api/dashboard/timeseries</code> no ha devuelto tramos.
    </p>
@endif
