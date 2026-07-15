@extends('layouts.app')

@section('title', 'Tiendas')

@section('content')
<div class="dbx-wrap">
<x-ui.card>
    <x-ui.toolbar>
        <form method="GET" class="dbx-filters" autocomplete="off">
            <div class="dbx-filter-item">
                <label for="q" class="dbx-filter-label">Buscar</label>
                <input id="q" name="q" type="text" class="input" value="{{ request('q') }}" placeholder="Buscar por número o nombre">
            </div>
        </form>
        <div class="dbx-form-actions">
            <a href="{{ route('tiendas.create') }}" class="btn btn-primary">Nueva tienda</a>
        </div>
    </x-ui.toolbar>
<x-ui.table class="dbx-actions-table">
        <thead>
            <tr>
                <th class="number-col">Numero</th>
                <th class="text-col">Nombre</th>
                <th class="number-col">Usuarios</th>
                <th class="number-col">Impresoras</th>
                <th class="status-col">Activa</th>
                <th class="actions-col">Acciones</th>
            </tr>
        </thead>
        <tbody>
            @forelse($stores as $store)
            @php
                $id = $store['storeId'] ?? $store['StoreId'] ?? null;
                $name = $store['name'] ?? $store['Name'] ?? '-';
                $isActive = $store['isActive'] ?? $store['IsActive'] ?? false;
                $usersCount = (int) ($store['usersCount'] ?? $store['UsersCount'] ?? 0);
                $printersCount = (int) ($store['printersCount'] ?? $store['PrintersCount'] ?? 0);
            @endphp
            <tr>
                <td class="number-col">{{ $id }}</td>
                <td class="text-col">{{ $name }}</td>
                <td class="number-col">{{ $usersCount }}</td>
                <td class="number-col">{{ $printersCount }}</td>
                <td class="status-col">
                    <span class="badge status-chip {{ $isActive ? 'badge-success' : 'badge-danger' }}" aria-label="{{ $isActive ? 'Tienda activa' : 'Tienda inactiva' }}">
                        {{ $isActive ? 'Si' : 'No' }}
                    </span>
                </td>
                <td class="actions-col">
                    @if($id !== null && $id !== '')
                    <x-ui.action-buttons>
                        <a href="{{ route('tiendas.edit', $id) }}" class="btn btn-ghost">Editar</a>
                        @if($isActive)
                            <form action="{{ route('tiendas.destroy', $id) }}" method="POST" onsubmit="return confirm('¿Desactivar tienda? Se mantendran logs e historico, y las impresoras de esta tienda quedaran inactivas.')">
                                @csrf
                                @method('DELETE')
                                <button type="submit" class="btn btn-warning">Desactivar</button>
                            </form>
                        @else
                            <form action="{{ route('tiendas.activate', $id) }}" method="POST" onsubmit="return confirm('¿Activar tienda de nuevo?')">
                                @csrf
                                <button type="submit" class="btn btn-primary">Activar</button>
                            </form>
                        @endif
                        <form action="{{ route('tiendas.destroy', $id) }}" method="POST" onsubmit="return confirm('¿ELIMINAR DEFINITIVAMENTE la tienda {{ $name }} ({{ $id }})? Esta acci&oacute;n borrar&aacute; tambi&eacute;n hist&oacute;rico de impresi&oacute;n y no se puede deshacer.')">
                            @csrf
                            @method('DELETE')
                            <input type="hidden" name="hardDelete" value="1">
                            <input type="hidden" name="purgeHistory" value="1">
                            <button type="submit" class="btn btn-danger">Eliminar definitivo</button>
                        </form>
                    </x-ui.action-buttons>
                    @endif
                </td>
            </tr>
            @empty
            <x-ui.empty-row colspan="6" message="No hay tiendas." />
            @endforelse
        </tbody>
</x-ui.table>
</x-ui.card>
</div>
@endsection
