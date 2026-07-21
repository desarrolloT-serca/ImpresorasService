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
                <td>{{ $roleLabels[$role] ?? $role }}</td>
                <td>{{ $storeId ?? '-' }}</td>
                <td>
                    @if($id)
                    <x-ui.action-buttons>
                        <a href="{{ route('usuarios.edit', $id) }}" class="btn btn-ghost btn-icon btn-action-icon" aria-label="Editar usuario" title="Editar usuario">
                            <x-ui.action-icon name="edit" label="Editar usuario" />
                        </a>
                        <form action="{{ route('usuarios.destroy', $id) }}" method="POST" onsubmit="return confirm('¿Eliminar usuario?')">
                            @csrf
                            @method('DELETE')
                            <button type="submit" class="btn btn-danger btn-icon btn-action-icon" aria-label="Eliminar usuario" title="Eliminar usuario">
                                <x-ui.action-icon name="trash" label="Eliminar usuario" />
                            </button>
                        </form>
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
