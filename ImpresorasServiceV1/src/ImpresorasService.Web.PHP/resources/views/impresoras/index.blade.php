@extends('layouts.app')

@section('title', 'Impresoras')

@section('content')
@php
    $storeNameById = collect($storeOptions ?? [])->mapWithKeys(fn ($store) => [(string) ($store['storeId'] ?? '') => $store['name'] ?? null]);
@endphp
@if(session('success'))
    <div class="mb-4 alert alert-success">{{ session('success') }}</div>
@endif

<div class="dbx-wrap">
<x-ui.card>
    <x-ui.toolbar>
        <form method="GET" class="dbx-filters">
            <div>
                <label class="dbx-filter-label">Tienda</label>
                @if($isAdmin ?? false)
                <select name="storeId" class="select">
                    <option value="">Todas</option>
                    @foreach($storeOptions ?? [] as $store)
                        <option value="{{ $store['storeId'] }}" {{ (string)request('storeId', '') === (string)$store['storeId'] ? 'selected' : '' }}>
                            {{ \App\Helpers\StoreFormat::label($store['storeId'], $store['name']) }}
                        </option>
                    @endforeach
                </select>
                @else
                <input type="text" value="{{ \App\Helpers\StoreFormat::label($effectiveStoreId ?? null, $storeNameById[(string) ($effectiveStoreId ?? '')] ?? null) }}" class="input !w-auto" disabled>
                @endif
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
                <a href="{{ route('impresoras.index') }}" class="btn btn-ghost">Limpiar</a>
            </div>
        </form>
        @if($isAdmin ?? false)
            <div class="dbx-form-actions">
                <a href="{{ route('impresoras.create') }}" class="btn btn-primary">Nueva impresora</a>
            </div>
        @endif
    </x-ui.toolbar>
</x-ui.card>

<x-ui.card>
    <div class="dbx-title-row">
        <h2 class="dbx-title">Impresoras</h2>
        <span class="dbx-subtle">Estado y conectividad</span>
    </div>
<x-ui.table class="dbx-actions-table">
        <thead>
            <tr>
                <th>ID</th>
                <th>Nombre</th>
                <th>SpoolQueue</th>
                <th>Tienda</th>
                <th>Activa</th>
                <th>Estado</th>
                <th>Puerto</th>
                <th>Acciones</th>
            </tr>
        </thead>
        <tbody>
            @forelse($printers as $p)
            <tr data-printer-id="{{ $p['printerId'] ?? $p['PrinterId'] ?? '' }}">
                <td>{{ $p['printerId'] ?? $p['PrinterId'] ?? '-' }}</td>
                <td>{{ $p['printerName'] ?? $p['PrinterName'] ?? '-' }}</td>
                <td>{{ $p['spoolQueue'] ?? $p['SpoolQueue'] ?? '-' }}</td>
                @php $rowStoreId = $p['storeId'] ?? $p['StoreId'] ?? null; @endphp
                <td>{{ \App\Helpers\StoreFormat::label($rowStoreId, $storeNameById[(string) $rowStoreId] ?? null) }}</td>
                <td>
                    <span class="badge status-chip {{ ($p['isActive'] ?? $p['IsActive'] ?? false) ? 'badge-success' : 'badge-danger' }}" aria-label="{{ ($p['isActive'] ?? $p['IsActive'] ?? false) ? 'Impresora activa' : 'Impresora inactiva' }}">
                        {{ ($p['isActive'] ?? $p['IsActive'] ?? false) ? 'Si' : 'No' }}
                    </span>
                </td>
                <td>
                    <span class="ping-status badge badge-neutral" data-id="{{ $p['printerId'] ?? $p['PrinterId'] ?? '' }}">-</span>
                </td>
                <td>
                    <span class="ping-port badge badge-neutral" data-id="{{ $p['printerId'] ?? $p['PrinterId'] ?? '' }}">-</span>
                </td>
                <td>
                    @if($isAdmin ?? false)
                    @php $id = $p['printerId'] ?? $p['PrinterId'] ?? null; @endphp
                    @if($id)
                    <x-ui.action-buttons>
                        <a href="{{ route('impresoras.edit', $id) }}" class="btn btn-ghost">Editar</a>
                        <form action="{{ route('impresoras.destroy', $id) }}" method="POST" onsubmit="return confirm('¿Eliminar?')">
                            @csrf
                            @method('DELETE')
                            <button type="submit" class="btn btn-danger">Eliminar</button>
                        </form>
                    </x-ui.action-buttons>
                    @endif
                    @else
                    <span class="text-slate-400 text-sm">-</span>
                    @endif
                </td>
            </tr>
            @empty
            <x-ui.empty-row colspan="8" message="No hay impresoras." />
            @endforelse
        </tbody>
</x-ui.table>
</x-ui.card>
</div>

<script>
(function() {
    const csrf = document.querySelector('meta[name="csrf-token"]')?.content || '';
    const pingInterval = {{ $pingIntervalSeconds ?? 30 }} * 1000;
    const isAdmin = {{ ($isAdmin ?? false) ? 'true' : 'false' }};
    const slowLatencyMs = 250;

    function classifyConnectionState(data) {
        if (!data || !data.reachable) return 'error';
        const latency = Number(data.latencyMs || 0);
        if (latency > slowLatencyMs) return 'slow';
        return 'ok';
    }

    function userFriendlyStatusText(data) {
        const state = classifyConnectionState(data);

        if (state === 'ok') {
            if (isAdmin) {
                if (data.latencyMs) return data.latencyMs + ' ms';
            }
            return 'Conectada';
        }

        if (state === 'slow') {
            if (isAdmin) {
                if (data.latencyMs) return 'Lenta: ' + data.latencyMs + ' ms';
            }
            return 'Conectada (lenta)';
        }

        if (isHostNotConfigured(data)) {
            return 'Sin host';
        }

        if (isAdmin) {
            return 'Error';
        }

        return 'Sin conexión';
    }

    function isHostNotConfigured(data) {
        const error = String(data?.error || data?.message || '').toLowerCase();
        return error.includes('sin host')
            || error.includes('host no configurado')
            || error.includes('host not configured')
            || error.includes('hostname no configurado');
    }

    function technicalStatusTitle(data) {
        if (!data) return '';
        if (data.error) return data.error;
        if (data.message) return data.message;
        if (data.transport) return data.transport;
        if (data.latencyMs) return 'Latencia: ' + data.latencyMs + ' ms';
        return '';
    }

    function applyConnectionBadgeClass(statusEl, data) {
        const state = classifyConnectionState(data);
        if (state === 'ok') {
            statusEl.className = 'ping-status badge badge-success';
            return;
        }
        if (state === 'slow') {
            statusEl.className = 'ping-status badge badge-warning';
            return;
        }
        statusEl.className = 'ping-status badge badge-danger';
    }

    function doNetConnection(id) {
        if (!id) return;
        const statusEl = document.querySelector('.ping-status[data-id="' + id + '"]');
        const portEl = document.querySelector('.ping-port[data-id="' + id + '"]');
        if (statusEl) statusEl.textContent = '...';
        if (portEl) portEl.textContent = '...';

        fetch('{{ url("/impresoras") }}/' + id + '/netconnection', {
            method: 'POST',
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json',
                'X-CSRF-TOKEN': csrf,
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: JSON.stringify({})
        })
        .then(r => r.json())
        .then(data => {
            if (statusEl) {
                statusEl.textContent = userFriendlyStatusText(data);
                statusEl.title = technicalStatusTitle(data);
                applyConnectionBadgeClass(statusEl, data);
            }
            if (portEl) {
                if (data && data.transport && typeof data.transport === 'string') {
                    const match = data.transport.match(/\/(\d+)$/);
                    portEl.textContent = match ? match[1] : data.transport;
                } else {
                    portEl.textContent = '-';
                }
            }
        })
        .catch(() => {
            if (statusEl) {
                statusEl.textContent = 'Error';
                statusEl.title = 'No se pudo comprobar la conectividad.';
                statusEl.className = 'ping-status badge badge-danger';
            }
            if (portEl) {
                portEl.textContent = '-';
            }
        })
        .finally(() => {});
    }

    function netConnectionAll() {
        document.querySelectorAll('[data-printer-id]').forEach(row => {
            const id = row.dataset.printerId;
            if (id) doNetConnection(id);
        });
    }

    if (pingInterval > 0) {
        netConnectionAll();
        setInterval(netConnectionAll, pingInterval);
    }
})();
</script>
@endsection
