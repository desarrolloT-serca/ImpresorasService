@props([
    'kicker',
    'title',
    'subtitle' => null,
])

<div class="dbx-rule-form-header">
    <div>
        <span class="dbx-rule-form-kicker">{{ $kicker }}</span>
        <h2 class="dbx-title">{{ $title }}</h2>
        @if($subtitle)
            <p class="dbx-subtle">{{ $subtitle }}</p>
        @endif
    </div>
    {{ $slot }}
</div>
