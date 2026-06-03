@extends('layouts.app')

@section('title', 'Ajustes')

@section('content')
    @if(session('success'))
        <div class="mb-4 alert alert-success">{{ session('success') }}</div>
    @endif

    @php
        $thresholds = is_array($thresholds ?? null) ? $thresholds : [];
        $severityOptions = [
            'info' => 'Info',
            'warning' => 'Warning',
            'critical' => 'Critica',
        ];
        $severityClass = [
            'info' => 'info',
            'warning' => 'warning',
            'critical' => 'critical',
        ];
        $thresholdRules = is_array($thresholds['thresholdRules'] ?? null) ? $thresholds['thresholdRules'] : [];
        $ruleGroups = [
            'queue' => [
                'title' => 'Cola de impresion',
                'hint' => 'Trabajos encolados actualmente',
                'valueLabel' => 'Cola >=',
                'empty' => [['min' => 10, 'severity' => 'warning'], ['min' => 30, 'severity' => 'critical']],
            ],
            'failed' => [
                'title' => 'Fallos sin reenviar',
                'hint' => 'Errores finales recientes',
                'valueLabel' => 'Fallos >=',
                'empty' => [['min' => 1, 'severity' => 'warning'], ['min' => 5, 'severity' => 'critical']],
            ],
            'missingHost' => [
                'title' => 'Conectividad: host',
                'hint' => 'Impresoras sin `Host` ni UNC',
                'valueLabel' => 'Sin host >=',
                'empty' => [['min' => 1, 'severity' => 'warning']],
            ],
            'conn' => [
                'title' => 'Conectividad: fallos',
                'hint' => 'Fallos consecutivos del monitor',
                'valueLabel' => 'Streak >=',
                'empty' => [['min' => 2, 'severity' => 'warning'], ['min' => 3, 'severity' => 'critical']],
            ],
        ];

        foreach ($ruleGroups as $metric => $group) {
            $rules = array_values(is_array($thresholdRules[$metric] ?? null) ? $thresholdRules[$metric] : $group['empty']);
            usort($rules, fn (array $a, array $b): int => ((int) ($a['min'] ?? 0)) <=> ((int) ($b['min'] ?? 0)));
            $ruleGroups[$metric]['rules'] = $rules;
        }
    @endphp

    <section class="dbx-card ajustes-wrap">
        <div class="dbx-card-body">
            <form method="POST" action="{{ route('ajustes.thresholds.update') }}" class="dbx-thresholds-form">
                @csrf

                <div class="dbx-title-row">
                    <h3 class="dbx-title dbx-thresholds-title">Reglas de alertas</h3>
                    <span class="dbx-subtle">Hasta tres umbrales por regla: Info, Warning y Critica</span>
                </div>

                <div class="threshold-grid ajustes-grid">
                    @foreach($ruleGroups as $metric => $group)
                        <article class="dbx-card ajustes-card">
                            <div class="dbx-card-body">
                                <div class="dbx-title-row">
                                    <h3 class="dbx-title">{{ $group['title'] }}</h3>
                                    <span class="dbx-subtle">{{ $group['hint'] }}</span>
                                </div>

                                <div class="dbx-subtle text-sm ajustes-preview">
                                    Vista previa:
                                    @foreach($group['rules'] as $rule)
                                        <span class="dbx-pill {{ $severityClass[$rule['severity'] ?? 'warning'] ?? 'warning' }}">
                                            {{ strtoupper((string) ($rule['severity'] ?? 'warning')) }}
                                        </span>
                                        desde <strong>{{ (int) ($rule['min'] ?? 0) }}</strong>@if(!$loop->last),@else.@endif
                                    @endforeach
                                </div>

                                <x-threshold-rule-list
                                    :metric="$metric"
                                    :rules="$group['rules']"
                                    :severity-options="$severityOptions"
                                    :value-label="$group['valueLabel']" />
                            </div>
                        </article>
                    @endforeach
                </div>

                @error('thresholds')
                    <p class="field-error">{{ $message }}</p>
                @enderror

                <div class="dbx-form-actions">
                    <button class="btn btn-primary" type="submit">Guardar reglas</button>
                </div>
            </form>
        </div>
    </section>

    <script>
        (function() {
            const severities = ['info', 'warning', 'critical'];

            function refreshList(list) {
                const rows = Array.from(list.querySelectorAll('[data-threshold-row]'));
                const addButton = list.querySelector('[data-add-threshold]');
                rows.forEach(row => {
                    const removeButton = row.querySelector('[data-remove-threshold]');
                    if (removeButton) removeButton.disabled = rows.length <= 1;
                });
                if (addButton) addButton.disabled = rows.length >= severities.length;
            }

            function renameTemplateRow(row, metric, index) {
                const minInput = row.querySelector('[data-threshold-name="min"]');
                if (minInput) {
                    minInput.name = `thresholdRules[${metric}][${index}][min]`;
                    minInput.id = `threshold-${metric}-${index}-min`;
                    minInput.value = '';
                    row.querySelector('label')?.setAttribute('for', minInput.id);
                }

                row.querySelectorAll('input[type="radio"]').forEach((input) => {
                    const severity = input.value;
                    const id = `threshold-${metric}-${index}-severity-${severity}`;
                    input.name = `thresholdRules[${metric}][${index}][severity]`;
                    input.id = id;
                    input.checked = severity === 'info';
                    input.closest('label')?.setAttribute('for', id);
                });
            }

            document.querySelectorAll('[data-threshold-list]').forEach(list => {
                refreshList(list);
            });

            document.addEventListener('click', function(event) {
                const add = event.target.closest('[data-add-threshold]');
                if (add) {
                    const metric = add.dataset.addThreshold;
                    const list = add.closest('[data-threshold-list]');
                    const template = list?.querySelector(`template[data-threshold-template="${metric}"]`);
                    const rowsContainer = list?.querySelector('.threshold-rule-rows');
                    if (!list || !template || !rowsContainer) return;

                    const rows = list.querySelectorAll('[data-threshold-row]');
                    if (rows.length >= severities.length) return;

                    const index = Number(list.dataset.nextIndex || rows.length);
                    const fragment = template.content.cloneNode(true);
                    const row = fragment.querySelector('[data-threshold-row]');
                    renameTemplateRow(row, metric, index);
                    rowsContainer.appendChild(fragment);
                    list.dataset.nextIndex = String(index + 1);
                    refreshList(list);
                    return;
                }

                const remove = event.target.closest('[data-remove-threshold]');
                if (remove) {
                    const list = remove.closest('[data-threshold-list]');
                    const rows = list ? list.querySelectorAll('[data-threshold-row]') : [];
                    if (!list || rows.length <= 1) return;
                    remove.closest('[data-threshold-row]')?.remove();
                    refreshList(list);
                }
            });
        })();
    </script>
@endsection
