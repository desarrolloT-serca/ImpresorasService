@props([
    'title',
    'hint' => null,
])

<section class="dbx-rule-form-section">
    <div class="dbx-rule-form-section-title">
        <h3>{{ $title }}</h3>
        @if($hint)
            <span>{{ $hint }}</span>
        @endif
    </div>
    <div class="dbx-rule-form-grid">
        {{ $slot }}
    </div>
</section>
