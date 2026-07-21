@extends('layouts.app')

@section('title', 'Usuarios')

@section('content')
<div class="dbx-wrap">
@php
    $roleLabels = [
        'Admin' => 'Administrador',
        'StoreManager' => 'Jefe de tienda',
        'Supervisor' => 'Jefe de tienda',
        'Employee' => 'Empleado',
    ];
    $storeNameById = collect($storeOptions ?? [])->mapWithKeys(
        fn ($store) => [(string) ($store['storeId'] ?? '') => $store['name'] ?? null]
    );
@endphp

<x-ui.card>
    <x-ui.toolbar>
        <form method="GET" class="dbx-filters" autocomplete="off">
            <div class="dbx-filter-item">
                <label for="q" class="dbx-filter-label">Buscar</label>
                <input id="q" name="q" type="text" class="input" value="{{ request('q') }}" placeholder="Buscar por login, nombre o tienda">
            </div>
        </form>
        <div class="dbx-form-actions">
            <a href="{{ route('usuarios.create') }}" class="btn btn-primary btn-icon btn-action-icon" aria-label="Nuevo usuario" title="Nuevo usuario">
                <x-ui.action-icon name="plus" label="Nuevo usuario" />
            </a>
        </div>
    </x-ui.toolbar>
<x-ui.table class="dbx-actions-table">
        <thead>
            <tr>
                <th>ID</th>
                <th>Login</th>
                <th>Nombre</th>
                <th>Rol</th>
                <th>Tienda</th>
                <th>Acciones</th>
            </tr>
        </thead>
        <tbody>
            @forelse($users as $user)
            @php
                $id = $user['userId'] ?? $user['UserId'] ?? null;
                $role = $user['role'] ?? $user['Role'] ?? 'Employee';
                $storeId = $user['storeId'] ?? $user['StoreId'] ?? null;
            @endphp
            <tr>
                <td>{{ $id }}</td>
                <td>{{ $user['login'] ?? $user['Login'] ?? '-' }}</td>
                <td>{{ $user['displayName'] ?? $user['DisplayName'] ?? '-' }}</td>
                <td><span class="badge badge-neutral">{{ $roleLabels[$role] ?? $role }}</span></td>
                <td>{{ $storeId !== null ? \App\Helpers\StoreFormat::label($storeId, $storeNameById[(string) $storeId] ?? null) : '-' }}</td>
                <td>
                    @if($id)
                    <x-ui.action-buttons>
                        <a href="{{ route('usuarios.edit', $id) }}" class="btn btn-ghost">Editar</a>
                        <x-ui.confirm-form
                            :action="route('usuarios.destroy', $id)"
                            method="DELETE"
                            title="Eliminar usuario"
                            message="Se eliminara el usuario {{ $user['displayName'] ?? $user['DisplayName'] ?? $user['login'] ?? $id }}. Esta accion no se puede deshacer."
                            confirm-label="Eliminar"
                            danger
                        >
                            <x-slot:trigger>
                                <button type="submit" class="btn btn-danger">Eliminar</button>
                            </x-slot:trigger>
                        </x-ui.confirm-form>
                    </x-ui.action-buttons>
                    @endif
                </td>
            </tr>
            @empty
            <x-ui.empty-row colspan="6" message="No hay usuarios." />
            @endforelse
        </tbody>
</x-ui.table>
</x-ui.card>
</div>
@endsection
