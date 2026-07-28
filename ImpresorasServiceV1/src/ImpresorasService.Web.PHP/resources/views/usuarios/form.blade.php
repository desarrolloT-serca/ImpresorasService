@extends('layouts.app')

@php
    $editing = !is_null($user ?? null);
    $userId = $user['userId'] ?? $user['UserId'] ?? null;
    $login = old('login', $user['login'] ?? $user['Login'] ?? '');
    $displayName = old('displayName', $user['displayName'] ?? $user['DisplayName'] ?? '');
    $role = old('role', $user['role'] ?? $user['Role'] ?? \App\Helpers\AuthHelper::ROLE_EMPLOYEE);
    $storeId = old('storeId', $user['storeId'] ?? $user['StoreId'] ?? '');
@endphp

@section('title', $editing ? 'Editar usuario' : 'Nuevo usuario')

@section('content')
<x-ui.card class="max-w-2xl">
    <form method="POST" action="{{ $editing ? route('usuarios.update', $userId) : route('usuarios.store') }}" class="dbx-form-grid">
        @csrf
        @if($editing)
            @method('PUT')
        @endif

        <x-ui.field label="Login" name="login" :value="$login" required minlength="3" maxlength="80" autocomplete="username" />

        <x-ui.field label="Nombre visible" name="displayName" :value="$displayName" maxlength="120" autocomplete="off" />

        <x-ui.field label="Rol" name="role" type="select" required>
            @foreach($roles as $roleValue => $roleLabel)
                <option value="{{ $roleValue }}" {{ $role === $roleValue ? 'selected' : '' }}>{{ $roleLabel }}</option>
            @endforeach
        </x-ui.field>

        <x-ui.field label="Tienda (obligatoria para jefe de tienda y empleado)" name="storeId" type="select">
            <option value="">Sin tienda</option>
            @foreach($stores as $store)
                @php
                    $sid = $store['storeId'] ?? $store['StoreId'] ?? '';
                    $sname = $store['name'] ?? $store['Name'] ?? ('Tienda ' . $sid);
                @endphp
                <option value="{{ $sid }}" {{ (string)$storeId === (string)$sid ? 'selected' : '' }}>
                    {{ \App\Helpers\StoreFormat::label($sid, $sname) }}
                </option>
            @endforeach
        </x-ui.field>

        <x-ui.field
            :label="$editing ? 'Nueva contraseña (opcional)' : 'Contraseña'"
            name="password"
            type="password"
            minlength="6"
            maxlength="80"
            autocomplete="new-password"
            :required="!$editing"
        />

        <div class="form-actions">
            <a href="{{ route('usuarios.index') }}" class="btn btn-ghost">Cancelar</a>
            <button type="submit" class="btn btn-primary">{{ $editing ? 'Guardar cambios' : 'Crear usuario' }}</button>
        </div>
    </form>
</x-ui.card>

<script>
(function() {
    const roleField = document.getElementById('role');
    const storeField = document.getElementById('storeId');

    function syncStoreRequirement() {
        const role = roleField?.value || '';
        const requiresStore = role === '{{ \App\Helpers\AuthHelper::ROLE_STORE_MANAGER }}' || role === '{{ \App\Helpers\AuthHelper::ROLE_EMPLOYEE }}';
        if (!requiresStore) {
            storeField.value = '';
        }
        storeField.required = requiresStore;
    }

    roleField?.addEventListener('change', syncStoreRequirement);
    syncStoreRequirement();
})();
</script>
@endsection
