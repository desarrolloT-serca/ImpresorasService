@props([
    'level' => 'neutral',
])

@php
    $level = in_array($level, ['healthy', 'warning', 'critical', 'neutral'], true) ? $level : 'neutral';
@endphp

<span {{ $attributes->merge(['class' => 'dbx-pill ' . $level]) }}>{{ $slot }}</span>
