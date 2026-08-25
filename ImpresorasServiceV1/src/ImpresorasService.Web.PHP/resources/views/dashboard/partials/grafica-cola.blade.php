{{--
    Cola a lo largo del periodo. Una sola serie: la profundidad de cola reconstruida por la Api.

    La linea del umbral critico es el motivo de que esta grafica exista. Un "47 en cola" no dice
    si hay que actuar; un 47 cruzando la linea de 30, si. Sin ese punto de referencia la grafica
    seria decorativa.

    Sin leyenda a proposito: con una unica serie el titulo ya la nombra.
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

        $depths = array_map(static fn ($p) => max(0, (int) ($p['queueDepth'] ?? 0)), $pts);
        $critical = max(1, (int) ($criticalQueueMin ?? 30));
        $rawMax = max(max($depths), $critical);
        $step = $rawMax <= 10 ? 2 : ($rawMax <= 50 ? 10 : ($rawMax <= 200 ? 25 : 100));
        $maxY = max($step, (int) (ceil($rawMax / $step) * $step));

        $px = static fn (int $i): float => $padL + $i * $plotW / ($n - 1);
        $py = static fn (int $v): float => $padT + $plotH - ($v / $maxY) * $plotH;

        $coords = [];
        foreach ($depths as $i => $d) {
            $coords[] = round($px($i), 1) . ',' . round($py($d), 1);
        }
        $line = implode(' ', $coords);
        $area = $line . ' ' . round($px($n - 1), 1) . ',' . round($py(0), 1)
              . ' ' . round($px(0), 1) . ',' . round($py(0), 1);

        // Etiquetas de eje: como mucho seis, para que no se solapen en pantallas estrechas.
        $everyX = max(1, (int) ceil($n / 6));
        $hour = static function ($iso): string {
            $ts = strtotime((string) $iso);
            return $ts === false ? '' : gmdate('H:i', $ts);
        };

        $ticks = [0, (int) round($maxY / 2), $maxY];

        // Datos del tooltip: etiqueta e importe por tramo, ya formateados.
        $tip = [];
        foreach ($pts as $i => $p) {
            $tip[] = [
                'l' => $hour($p['startUtc'] ?? $p['StartUtc'] ?? ''),
                'q' => (int) ($p['queueDepth'] ?? 0),
                'r' => (int) ($p['received'] ?? 0),
                'p' => (int) ($p['printed'] ?? 0),
            ];
        }

        $peak = max($depths);
        $peakAt = array_search($peak, $depths, true);
    @endphp

    <div class="dbx-op-chart" data-queue-chart data-points="{{ json_encode($tip, JSON_UNESCAPED_UNICODE) }}">
        <div class="dbx-op-chart-head">
            <h3 class="dbx-op-chart-title">Cola a lo largo del periodo</h3>
            <span class="dbx-op-meta">
                Pico de {{ $peak }} a las {{ $tip[$peakAt]['l'] ?? '--:--' }} &middot; horas UTC
            </span>
        </div>

        <svg class="dbx-op-chart-svg" viewBox="0 0 {{ $W }} {{ $H }}" role="img"
             aria-label="Profundidad de la cola por tramo. Pico de {{ $peak }} trabajos. Umbral critico en {{ $critical }}.">
            {{-- Guias horizontales, recesivas: sirven de escala, no compiten con el dato. --}}
            @foreach($ticks as $t)
                <line class="dbx-op-chart-grid" x1="{{ $padL }}" x2="{{ $W - $padR }}"
                      y1="{{ round($py($t), 1) }}" y2="{{ round($py($t), 1) }}"></line>
                <text class="dbx-op-chart-axis" x="{{ $padL - 9 }}" y="{{ round($py($t) + 4, 1) }}"
                      text-anchor="end">{{ $t }}</text>
            @endforeach

            <polygon class="dbx-op-chart-area" points="{{ $area }}"></polygon>
            <polyline class="dbx-op-chart-line" points="{{ $line }}"></polyline>

            {{-- El umbral va por encima del area para que se lea siempre. --}}
            @if($critical <= $maxY)
                <line class="dbx-op-chart-threshold" x1="{{ $padL }}" x2="{{ $W - $padR }}"
                      y1="{{ round($py($critical), 1) }}" y2="{{ round($py($critical), 1) }}"></line>
                <text class="dbx-op-chart-threshold-label" x="{{ $padL + 6 }}"
                      y="{{ round($py($critical) - 7, 1) }}">Umbral cr&iacute;tico {{ $critical }}</text>
            @endif

            @foreach($pts as $i => $p)
                @if($i % $everyX === 0)
                    <text class="dbx-op-chart-axis" x="{{ round($px($i), 1) }}" y="{{ $H - 11 }}"
                          text-anchor="middle">{{ $tip[$i]['l'] }}</text>
                @endif
            @endforeach

            <line class="dbx-op-chart-cursor" x1="0" x2="0" y1="{{ $padT }}" y2="{{ $padT + $plotH }}" hidden></line>
            <circle class="dbx-op-chart-dot" r="5" cx="0" cy="0" hidden></circle>
            <rect class="dbx-op-chart-hit" x="{{ $padL }}" y="{{ $padT }}"
                  width="{{ $plotW }}" height="{{ $plotH }}"></rect>
        </svg>

        <div class="dbx-op-chart-tip" role="status" hidden></div>
    </div>
{{-- Cruceta y tooltip de la grafica de cola. Delegado en document a proposito: el dashboard se
     reemplaza entero en cada refresco automatico y asi no hay que volver a enganchar nada. --}}
<script>
(function() {
    function hide(chart) {
        chart.querySelectorAll('.dbx-op-chart-cursor, .dbx-op-chart-dot').forEach(function(el) {
            el.setAttribute('hidden', '');
        });
        const tip = chart.querySelector('.dbx-op-chart-tip');
        if (tip) tip.setAttribute('hidden', '');
    }

    document.addEventListener('mousemove', function(event) {
        const target = event.target instanceof Element ? event.target : null;
        const chart = target ? target.closest('[data-queue-chart]') : null;
        if (!chart) return;

        const svg = chart.querySelector('.dbx-op-chart-svg');
        const line = chart.querySelector('.dbx-op-chart-line');
        const cursor = chart.querySelector('.dbx-op-chart-cursor');
        const dot = chart.querySelector('.dbx-op-chart-dot');
        const tip = chart.querySelector('.dbx-op-chart-tip');
        if (!svg || !line || !cursor || !dot || !tip) return;

        let points;
        try {
            points = JSON.parse(chart.dataset.points || '[]');
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

        tip.textContent = point.l + ' · ' + point.q + ' en cola — '
            + point.r + ' recibidos, ' + point.p + ' impresos';
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
@endif
