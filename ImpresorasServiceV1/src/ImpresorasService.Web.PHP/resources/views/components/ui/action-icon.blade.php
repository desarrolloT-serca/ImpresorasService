@props([
    'name' => 'edit',
    'label' => '',
])

<span {{ $attributes->merge(['class' => 'dbx-action-icon', 'role' => 'img', 'aria-label' => $label, 'title' => $label]) }}>
    <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
        @switch($name)
            @case('plus')
                <path d="M12 5v14" />
                <path d="M5 12h14" />
                @break
            @case('trash')
                <path d="M3 6h18" />
                <path d="M8 6V4h8v2" />
                <path d="M19 6l-1 14H6L5 6" />
                <path d="M10 11v5" />
                <path d="M14 11v5" />
                @break
            @case('power')
                <path d="M12 2v10" />
                <path d="M18.4 6.6a9 9 0 1 1-12.8 0" />
                @break
            @case('check')
                <path d="M20 6L9 17l-5-5" />
                @break
            @case('edit')
            @default
                <path d="M12 20h9" />
                <path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4 12.5-12.5z" />
        @endswitch
    </svg>
</span>
