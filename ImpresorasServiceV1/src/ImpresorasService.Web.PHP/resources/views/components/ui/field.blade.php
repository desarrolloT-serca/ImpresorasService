@props([
    'label',
    'name',
    'type' => 'text',
    'id' => null,
    'value' => null,
    'help' => null,
])

@php
    $fieldId = $id ?? $name;
    $hasError = $errors->has($name);
    $controlClass = ($type === 'select' ? 'select' : 'input') . ($hasError ? ' border-red-500' : '');
@endphp

<div>
    <label for="{{ $fieldId }}" class="form-label">{{ $label }}</label>
    @if($type === 'select')
        <select id="{{ $fieldId }}" name="{{ $name }}" {{ $attributes->merge(['class' => $controlClass]) }}>
            {{ $slot }}
        </select>
    @else
        <input type="{{ $type }}" id="{{ $fieldId }}" name="{{ $name }}" value="{{ $value }}" {{ $attributes->merge(['class' => $controlClass]) }}>
    @endif
    @if($help)
        <p class="form-hint">{{ $help }}</p>
    @endif
    @error($name)<p class="field-error">{{ $message }}</p>@enderror
</div>
