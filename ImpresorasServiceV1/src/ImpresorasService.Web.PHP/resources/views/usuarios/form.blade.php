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

        <div>
            <label class="form-label">Login</label>
            <input type="text" name="login" value="{{ $login }}" class="input @error('login') border-red-500 @enderror" required minlength="3" maxlength="80">
            @error('login')<p class="field-error">{{ $message }}</p>@enderror
        </div>

        <div>
            <label class="form-label">Nombre visible</label>
            <input type="text" name="displayName" value="{{ $displayName }}" class="input @error('displayName') border-red-500 @enderror" maxlength="120">
            @error('displayName')<p class="field-error">{{ $message }}</p>@enderror
        </div>

        <div>
            <label class="form-label">Rol</label>
            <select name="role" id="role" class="select @error('role') border-red-500 @enderror" required>
                @foreach($roles as $roleValue => $roleLabel)
                    <option value="{{ $roleValue }}" {{ $role === $roleValue ? 'selected' : '' }}>{{ $roleLabel }}</option>
                @endforeach
            </select>
            @error('role')<p class="field-error">{{ $message }}</p>@enderror
        </div>

        <div id="store-wrapper">
            <label class="form-label">Tienda (obligatoria para jefe de tienda y empleado)</label>
            <select name="storeId" id="storeId" class="select @error('storeId') border-red-500 @enderror">
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
            </select>
            @error('storeId')<p class="field-error">{{ $message }}</p>@enderror
        </div>

        <div>
            <label class="form-label">
                @if($editing)
                    Nueva contrase&ntilde;a (opcional)
                @else
                    Contrase&ntilde;a
                @endif
            </label>
            <input type="password" name="password" class="input @error('password') border-red-500 @enderror" {{ $editing ? '' : 'required' }} minlength="6" maxlength="80">
            @error('password')<p class="field-error">{{ $message }}</p>@enderror
        </div>

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
