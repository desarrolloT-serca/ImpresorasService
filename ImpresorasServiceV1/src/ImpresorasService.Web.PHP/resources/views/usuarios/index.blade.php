@extends('layouts.app')

@section('title', 'Usuarios')

@section('content')
<div class="dbx-wrap">
@php
    $roleLabels = [
        'Admin'        => 'Administrador',
        'StoreManager' => 'Jefe de tienda',
        'Supervisor'   => 'Jefe de tienda',
        'Employee'     => 'Empleado',
    ];
    $roleClass = [
        'Admin'        => 'admin',
        'StoreManager' => 'manager',
        'Supervisor'   => 'manager',
        'Employee'     => 'employee',
    ];
    $storeNameById = collect($storeOptions ?? [])->mapWithKeys(
        fn ($s) => [(string) ($s['storeId'] ?? '') => $s['name'] ?? null]
    );
@endphp

<x-ui.card>
    <x-ui.toolbar>
        <form method="GET" class="dbx-filters" autocomplete="off">
            <div class="dbx-filter-item">
                <label for="q" class="dbx-filter-label">Buscar</label>
                <input id="q" name="q" type="text" class="input" value="{{ request('q') }}" placeholder="Login, nombre o tienda">
            </div>
        </form>
        <div class="dbx-form-actions">
            <a href="{{ route('usuarios.create') }}" class="btn btn-primary btn-icon" aria-label="Nuevo usuario" title="Nuevo usuario">
                <x-ui.action-icon name="plus" label="Nuevo usuario" />
            </a>
        </div>
    </x-ui.toolbar>

    <x-ui.table class="dbx-actions-table">
        <thead>
            <tr>
                <th>Usuario</th>
                <th>Rol</th>
                <th>Tienda</th>
                <th>Estado</th>
                <th class="actions-col">Acciones</th>
            </tr>
        </thead>
        <tbody>
            @forelse($users as $user)
            @php
                $id          = $user['userId']      ?? $user['UserId']      ?? null;
                $login       = $user['login']        ?? $user['Login']        ?? '—';
                $displayName = $user['displayName']  ?? $user['DisplayName']  ?? '';
                $role        = $user['role']          ?? $user['Role']          ?? 'Employee';
                $storeId     = $user['storeId']      ?? $user['StoreId']      ?? null;
                // Null = Api antigua sin el campo: se asume activo, que es el DEFAULT de la columna.
                $isActive    = (bool) ($user['isActive'] ?? $user['IsActive'] ?? true);
                $storeName   = $storeId !== null ? ($storeNameById[(string)$storeId] ?? null) : null;
                $rl          = $roleLabels[$role]  ?? $role;
                $rc          = $roleClass[$role]   ?? 'employee';

                $nameForInit = $displayName ?: $login;
                $parts       = array_values(array_filter(explode(' ', trim($nameForInit))));
                $initials    = count($parts) >= 2
                    ? mb_strtoupper(mb_substr($parts[0], 0, 1) . mb_substr(end($parts), 0, 1))
                    : mb_strtoupper(mb_substr($nameForInit, 0, 2));
            @endphp
            <tr>
                <td>
                    <div class="user-identity">
                        <span class="user-avatar user-avatar-{{ $rc }}" aria-hidden="true">{{ $initials }}</span>
                        <div class="user-identity-text">
                            <span class="user-login">{{ $login }}</span>
                            @if($displayName && $displayName !== $login)
                            <span class="user-displayname">{{ $displayName }}</span>
                            @endif
                        </div>
                    </div>
                </td>
                <td>
                    <span class="badge badge-neutral badge-role-{{ $rc }}">{{ $rl }}</span>
                </td>
                <td>
                    @if($storeId !== null)
                        <a href="{{ route('tiendas.index', ['q' => $storeId]) }}"
                           class="store-stat-chip"
                           title="Ver tienda {{ $storeId }}">
                            #{{ $storeId }}{{ $storeName ? ' · ' . $storeName : '' }}
                        </a>
                    @else
                        <span class="user-displayname">—</span>
                    @endif
                </td>
                <td>
                    @if($isActive)
                        <span class="badge badge-success">Activo</span>
                    @else
                        <span class="badge badge-neutral" title="No puede iniciar sesion y su token deja de valer en la siguiente peticion">Desactivado</span>
                    @endif
                </td>
                <td class="actions-col">
                    @if($id)
                    <x-ui.action-buttons>
                        <a href="{{ route('usuarios.edit', $id) }}"
                           class="btn btn-ghost btn-icon"
                           title="Editar usuario"
                           aria-label="Editar {{ $login }}">
                            <x-ui.action-icon name="edit" label="Editar" />
                        </a>
                        @if($isActive)
                        <x-ui.confirm-form
                            :action="route('usuarios.activacion', $id)"
                            title="Desactivar usuario"
                            message="{{ $displayName ?: $login }} dejara de poder entrar y su sesion abierta se cortara en la siguiente peticion. Se conserva el usuario y su historial."
                            confirm-label="Desactivar"
                        >
                            <input type="hidden" name="activar" value="0">
                            <x-slot:trigger>
                                <button type="submit" class="btn btn-ghost btn-icon" title="Desactivar usuario" aria-label="Desactivar {{ $login }}">
                                    <x-ui.action-icon name="power" label="Desactivar" />
                                </button>
                            </x-slot:trigger>
                        </x-ui.confirm-form>
                        @else
                        {{-- Reactivar no necesita confirmacion: no rompe nada y se deshace igual de rapido. --}}
                        <form action="{{ route('usuarios.activacion', $id) }}" method="POST" class="inline">
                            @csrf
                            <input type="hidden" name="activar" value="1">
                            <button type="submit" class="btn btn-ghost btn-icon" title="Activar usuario" aria-label="Activar {{ $login }}">
                                <x-ui.action-icon name="check" label="Activar" />
                            </button>
                        </form>
                        @endif
                        <x-ui.confirm-form
                            :action="route('usuarios.destroy', $id)"
                            method="DELETE"
                            title="Eliminar usuario"
                            message="Se eliminara el usuario {{ $displayName ?: $login }}. Esta accion no se puede deshacer."
                            confirm-label="Eliminar"
                            danger
                        >
                            <x-slot:trigger>
                                <button type="submit" class="btn btn-danger" aria-label="Eliminar {{ $login }}">Eliminar</button>
                            </x-slot:trigger>
                        </x-ui.confirm-form>
                    </x-ui.action-buttons>
                    @endif
                </td>
            </tr>
            @empty
            <x-ui.empty-row colspan="5" message="No hay usuarios." />
            @endforelse
        </tbody>
    </x-ui.table>
</x-ui.card>
</div>
@endsection
