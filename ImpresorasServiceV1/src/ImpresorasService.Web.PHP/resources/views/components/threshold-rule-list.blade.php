@props([
    'metric',
    'rules' => [],
    'severityOptions' => [],
    'valueLabel' => 'Umbral',
])

@php
    $rules = array_values(is_array($rules) ? $rules : []);
@endphp

<div class="threshold-rule-list" data-threshold-list="{{ $metric }}" data-next-index="{{ count($rules) }}">
    <div class="threshold-rule-head" aria-hidden="true">
        <span>{{ $valueLabel }}</span>
        <span>Severidad</span>
        <span></span>
    </div>

    <div class="threshold-rule-rows">
        @foreach($rules as $index => $rule)
            <div class="threshold-rule-row" data-threshold-row>
                <div class="dbx-filter-item threshold-value-field">
                    <label class="dbx-filter-label sr-only" for="threshold-{{ $metric }}-{{ $index }}-min">{{ $valueLabel }}</label>
                    <input id="threshold-{{ $metric }}-{{ $index }}-min" type="number" min="0"
                        name="thresholdRules[{{ $metric }}][{{ $index }}][min]"
                        class="input threshold-min-input"
                        value="{{ old("thresholdRules.$metric.$index.min", $rule['min'] ?? 0) }}">
                </div>
                <div class="dbx-filter-item severity-field">
                    <x-severity-picker
                        name="thresholdRules[{{ $metric }}][{{ $index }}][severity]"
                        :value="old('thresholdRules.' . $metric . '.' . $index . '.severity', $rule['severity'] ?? 'warning')"
                        :options="$severityOptions" />
                </div>
                <button type="button" class="btn btn-ghost threshold-remove" data-remove-threshold aria-label="Eliminar umbral" title="Eliminar umbral">
                    <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
                        <path d="M3 6h18" />
                        <path d="M8 6V4h8v2" />
                        <path d="M19 6l-1 14H6L5 6" />
                        <path d="M10 11v5" />
                        <path d="M14 11v5" />
                    </svg>
                </button>
            </div>
        @endforeach
    </div>

    <button type="button" class="btn btn-ghost threshold-add" data-add-threshold="{{ $metric }}" aria-label="Anadir umbral" title="Anadir umbral">
        <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
            <path d="M12 5v14" />
            <path d="M5 12h14" />
        </svg>
    </button>

    <template data-threshold-template="{{ $metric }}">
        <div class="threshold-rule-row" data-threshold-row>
            <div class="dbx-filter-item threshold-value-field">
                <label class="dbx-filter-label sr-only">{{ $valueLabel }}</label>
                <input type="number" min="0" class="input threshold-min-input" data-threshold-name="min">
            </div>
            <div class="dbx-filter-item severity-field">
                <x-severity-picker
                    name="__NAME__"
                    value="info"
                    :options="$severityOptions"
                    data-template-picker="1" />
            </div>
            <button type="button" class="btn btn-ghost threshold-remove" data-remove-threshold aria-label="Eliminar umbral" title="Eliminar umbral">
                <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
                    <path d="M3 6h18" />
                    <path d="M8 6V4h8v2" />
                    <path d="M19 6l-1 14H6L5 6" />
                    <path d="M10 11v5" />
                    <path d="M14 11v5" />
                </svg>
            </button>
        </div>
    </template>
</div>
