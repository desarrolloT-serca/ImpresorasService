@props([
    'name',
    'label',
    'checked' => false,
])

<div class="dbx-rule-active-field">
    <label class="dbx-toggle">
        <input type="hidden" name="{{ $name }}" value="0">
        <input type="checkbox" name="{{ $name }}" value="1" {{ $checked ? 'checked' : '' }}>
        {{ $label }}
    </label>
    @error($name)<p class="field-error">{{ $message }}</p>@enderror
</div>
