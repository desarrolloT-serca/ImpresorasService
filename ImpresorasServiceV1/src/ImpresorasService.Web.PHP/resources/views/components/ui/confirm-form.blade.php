@props([
    'action',
    'method' => 'POST',
    'message',
    'title' => 'Confirmar accion',
    'confirmLabel' => 'Confirmar',
    'danger' => false,
    'typeToConfirm' => null,
])

<form
    action="{{ $action }}"
    method="POST"
    {{ $attributes->merge(['class' => 'inline']) }}
    data-confirm-message="{{ $message }}"
    data-confirm-title="{{ $title }}"
    data-confirm-label="{{ $confirmLabel }}"
    @if($danger) data-confirm-danger="1" @endif
    @if($typeToConfirm) data-confirm-type="{{ $typeToConfirm }}" @endif
>
    @csrf
    @if(strtoupper($method) !== 'POST')
        @method($method)
    @endif
    {{ $slot }}
    {{ $trigger }}
</form>
