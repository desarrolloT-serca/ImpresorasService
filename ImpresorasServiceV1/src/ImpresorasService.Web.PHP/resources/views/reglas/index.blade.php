@extends('layouts.app')

@section('title', 'Reglas de enrutado')

@section('content')
@if(session('success'))
    <div class="mb-4 alert alert-success">{{ session('success') }}</div>
@endif

<div class="dbx-wrap">
<x-ui.card>
    <x-ui.toolbar>
        <form method="GET" class="dbx-filters">
            <div>
                <label class="dbx-filter-label">Tienda</label>
                <select name="storeId" class="select">
                    <option value="">Todas</option>
                    @foreach($storeOptions ?? [] as $store)
                        <option value="{{ $store['storeId'] }}" {{ (string)request('storeId', '') === (string)$store['storeId'] ? 'selected' : '' }}>
                            {{ $store['name'] }} ({{ $store['storeId'] }})
                        </option>
                    @endforeach
                </select>
            </div>
            <div>
                <label class="dbx-filter-label">Estado</label>
                <select name="isActive" class="select">
                    <option value="">Todas</option>
                    <option value="1" {{ request('isActive') === '1' ? 'selected' : '' }}>Activas</option>
                    <option value="0" {{ request('isActive') === '0' ? 'selected' : '' }}>Inactivas</option>
                </select>
            </div>
            <div class="dbx-form-actions">
                <a href="{{ route('reglas.index') }}" class="btn btn-ghost">Limpiar</a>
            </div>
        </form>
        <div class="dbx-form-actions">
            <a href="{{ route('reglas.create') }}" class="btn btn-primary">Nueva regla</a>
        </div>
    </x-ui.toolbar>
</x-ui.card>

<x-ui.card>
        <div class="dbx-title-row">
            <h2 class="dbx-title">Reglas de enrutado</h2>
            <span class="dbx-subtle">Priorizacion y destino</span>
        </div>
<x-ui.table class="dbx-actions-table">
        <thead>
            <tr>
                <th>ID</th>
                <th>Prioridad</th>
                <th>Tienda</th>
                <th>Tipo doc</th>
                <th>Canal</th>
                <th>Impresora</th>
                <th>Activa</th>
                <th>Acciones</th>
            </tr>
        </thead>
        <tbody>
            @forelse($rules as $r)
            <tr>
                <td>{{ $r['ruleId'] ?? $r['RuleId'] ?? '-' }}</td>
                <td>{{ $r['priority'] ?? $r['Priority'] ?? '-' }}</td>
                <td>{{ $r['storeId'] ?? $r['StoreId'] ?? '-' }}</td>
                <td>{{ $r['documentType'] ?? $r['DocumentType'] ?? '-' }}</td>
                <td>{{ $r['channel'] ?? $r['Channel'] ?? '-' }}</td>
                <td>{{ ($r['printer'] ?? null) ? ($r['printer']['printerName'] ?? $r['printer']['PrinterName'] ?? $r['printerId'] ?? $r['PrinterId']) : ($r['printerId'] ?? $r['PrinterId'] ?? '-') }}</td>
                <td>
                    <span class="badge status-chip {{ ($r['isActive'] ?? $r['IsActive'] ?? false) ? 'badge-success' : 'badge-danger' }}" aria-label="{{ ($r['isActive'] ?? $r['IsActive'] ?? false) ? 'Regla activa' : 'Regla inactiva' }}">
                        {{ ($r['isActive'] ?? $r['IsActive'] ?? false) ? 'Si' : 'No' }}
                    </span>
                </td>
                <td>
                    @php $id = $r['ruleId'] ?? $r['RuleId'] ?? null; @endphp
                    @if($id)
                    <x-ui.action-buttons>
                        <a href="{{ route('reglas.edit', $id) }}" class="btn btn-ghost">Editar</a>
                        <form action="{{ route('reglas.destroy', $id) }}" method="POST" onsubmit="return confirm('¿Eliminar?')">
                            @csrf
                            @method('DELETE')
                            <button type="submit" class="btn btn-danger">Eliminar</button>
                        </form>
                    </x-ui.action-buttons>
                    @endif
                </td>
            </tr>
            @empty
            <x-ui.empty-row colspan="8" message="No hay reglas." />
            @endforelse
        </tbody>
</x-ui.table>
</x-ui.card>
</div>
@endsection
