@props([
    'name',
    'value' => 'warning',
    'options' => [],
])

@php
    $current = (string) $value;
    $idBase = 'severity-' . preg_replace('/[^a-z0-9_-]+/i', '-', (string) $name);
@endphp

<div {{ $attributes->merge(['class' => 'severity-picker']) }} role="radiogroup" aria-label="Severidad">
    @foreach($options as $key => $label)
        @php
            $optionId = $idBase . '-' . $key;
        @endphp
        <label class="severity-option severity-option-{{ $key }}" for="{{ $optionId }}">
            <input id="{{ $optionId }}" type="radio" name="{{ $name }}" value="{{ $key }}" @checked($current === (string) $key)>
            <span>{{ $label }}</span>
        </label>
    @endforeach
</div>
