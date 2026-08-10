@extends('layouts.app')

@section('title', 'Tiendas')

@section('content')
<div class="dbx-wrap">
<x-ui.card>
    <x-ui.toolbar>
        <form method="GET" class="dbx-filters" autocomplete="off">
            <div class="dbx-filter-item">
                <label for="q" class="dbx-filter-label">Buscar</label>
                <input id="q" name="q" type="text" class="input" value="{{ request('q') }}" placeholder="Número o nombre">
            </div>
        </form>
        <div class="dbx-form-actions">
            <a href="{{ route('tiendas.create') }}" class="btn btn-primary btn-icon" aria-label="Nueva tienda" title="Nueva tienda">
                <x-ui.action-icon name="plus" label="Nueva tienda" />
            </a>
        </div>
    </x-ui.toolbar>

    @forelse($stores as $store)
    @php
        if (!isset($gridStarted)) { $gridStarted = true; echo '<div class="store-grid">'; }
        $id           = $store['storeId']      ?? $store['StoreId']      ?? null;
        $name         = $store['name']         ?? $store['Name']         ?? '-';
        $isActive     = $store['isActive']     ?? $store['IsActive']     ?? false;
        $usersCount   = (int) ($store['usersCount']   ?? $store['UsersCount']   ?? 0);
        $printersCount= (int) ($store['printersCount'] ?? $store['PrintersCount'] ?? 0);
        $numPadded    = str_pad((string)$id, 3, '0', STR_PAD_LEFT);
    @endphp
    <div class="store-card"
         data-store-id="{{ $id }}"
         data-store-name="{{ e($name) }}"
         role="button"
         tabindex="0"
         aria-pressed="false"
         aria-label="Tienda {{ $name }}">

        <div class="store-card-header">
            <span class="store-card-number">#{{ $numPadded }}</span>
            <x-ui.status :level="$isActive ? 'healthy' : 'critical'">{{ $isActive ? 'Activa' : 'Inactiva' }}</x-ui.status>
        </div>

        <div class="store-card-body">
            <div class="store-card-name">{{ $name }}</div>
            <div class="store-card-stats">
                <a href="{{ route('usuarios.index', ['q' => $id]) }}"
                   class="store-stat-chip"
                   title="Ver usuarios"
                   onclick="event.stopPropagation()">
                    <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
                        <path d="M8 8a3 3 0 1 0 0-6 3 3 0 0 0 0 6zm-5 6a5 5 0 0 1 10 0H3z"/>
                    </svg>
                    {{ $usersCount }} {{ $usersCount === 1 ? 'usuario' : 'usuarios' }}
                </a>
                <a href="{{ route('impresoras.index', ['storeId' => $id]) }}"
                   class="store-stat-chip"
                   title="Ver impresoras"
                   onclick="event.stopPropagation()">
                    <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
                        <path d="M5 1h6v3H5V1zM2 6a1 1 0 0 0-1 1v5a1 1 0 0 0 1 1h1v-2h10v2h1a1 1 0 0 0 1-1V7a1 1 0 0 0-1-1H2zm1 1h10v3H3V7zm9.5 1a.5.5 0 1 1 0 1 .5.5 0 0 1 0-1zM4 12h8v2H4v-2z"/>
                    </svg>
                    {{ $printersCount }} {{ $printersCount === 1 ? 'impresora' : 'impresoras' }}
                </a>
            </div>
        </div>

        @if($id !== null && $id !== '')
        <div class="store-card-footer" onclick="event.stopPropagation()">
            <x-ui.action-buttons>
                <a href="{{ route('tiendas.edit', $id) }}"
                   class="btn btn-ghost btn-icon"
                   title="Editar tienda"
                   aria-label="Editar {{ $name }}">
                    <x-ui.action-icon name="edit" label="Editar" />
                </a>

                @if($isActive)
                    <x-ui.confirm-form
                        :action="route('tiendas.destroy', $id)"
                        method="DELETE"
                        title="Desactivar tienda"
                        message="Se mantendran logs e historico, y las impresoras de esta tienda quedaran inactivas."
                        confirm-label="Desactivar"
                    >
                        <x-slot:trigger>
                            <button type="submit" class="btn btn-warning">Desactivar</button>
                        </x-slot:trigger>
                    </x-ui.confirm-form>
                @else
                    <x-ui.confirm-form
                        :action="route('tiendas.activate', $id)"
                        title="Activar tienda"
                        message="La tienda {{ $name }} volvera a estar disponible para enrutado."
                        confirm-label="Activar"
                    >
                        <x-slot:trigger>
                            <button type="submit" class="btn btn-primary">Activar</button>
                        </x-slot:trigger>
                    </x-ui.confirm-form>
                @endif

                <x-ui.confirm-form
                    :action="route('tiendas.destroy', $id)"
                    method="DELETE"
                    title="Eliminar tienda definitivamente"
                    message="Esta accion borrara tambien el historico de impresion de {{ $name }} (#{{ $id }}) y no se puede deshacer."
                    confirm-label="Eliminar definitivo"
                    danger
                    :type-to-confirm="(string) $id"
                >
                    <input type="hidden" name="hardDelete" value="1">
                    <input type="hidden" name="purgeHistory" value="1">
                    <x-slot:trigger>
                        <button type="submit" class="btn btn-danger">Eliminar</button>
                    </x-slot:trigger>
                </x-ui.confirm-form>
            </x-ui.action-buttons>
        </div>
        @endif
    </div>

    @empty
    <p class="dbx-empty-text" style="padding:24px 0;text-align:center;color:var(--ui-text-muted);">No hay tiendas.</p>
    @endforelse

    @if(!empty($stores))
    </div>{{-- .store-grid --}}

    <div class="store-detail-panel" id="store-detail" hidden>
        <div class="store-detail-header">
            <div>
                <span class="store-detail-number" id="store-detail-number"></span><span class="store-detail-title" id="store-detail-title"></span>
            </div>
            <button type="button" class="btn btn-ghost btn-icon btn-sm" id="store-detail-close" aria-label="Cerrar detalle">
                <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
                    <path d="M4.146 4.146a.5.5 0 0 1 .708 0L8 7.293l3.146-3.147a.5.5 0 0 1 .708.708L8.707 8l3.147 3.146a.5.5 0 0 1-.708.708L8 8.707l-3.146 3.147a.5.5 0 0 1-.708-.708L7.293 8 4.146 4.854a.5.5 0 0 1 0-.708z"/>
                </svg>
            </button>
        </div>
        <div class="store-detail-body">
            <div class="store-detail-col">
                <div class="store-detail-col-title">Usuarios</div>
                <div id="store-detail-users"></div>
            </div>
            <div class="store-detail-col">
                <div class="store-detail-col-title">Impresoras</div>
                <div id="store-detail-printers"></div>
            </div>
        </div>
    </div>
    @endif

</x-ui.card>
</div>

@if(!empty($stores))
<script>
(function () {
    const allUsers    = @json($allUsers);
    const allPrinters = @json($allPrinters);

    const roleLabels = { Admin: 'Admin', StoreManager: 'Jefe', Supervisor: 'Jefe', Employee: 'Empleado' };
    const roleClass  = { Admin: 'admin', StoreManager: 'manager', Supervisor: 'manager', Employee: 'employee' };

    function initials(name) {
        if (!name) return '?';
        const parts = name.trim().split(/\s+/).filter(Boolean);
        if (parts.length < 2) return parts[0].slice(0, 2).toUpperCase();
        return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
    }

    function renderUsers(storeId) {
        const users = allUsers.filter(u => String(u.storeId ?? u.StoreId ?? '') === String(storeId));
        if (!users.length) return '<p class="store-detail-empty">Sin usuarios asignados.</p>';
        return users.map(u => {
            const name  = u.displayName ?? u.DisplayName ?? u.login ?? u.Login ?? '?';
            const login = u.login ?? u.Login ?? '';
            const role  = u.role ?? u.Role ?? 'Employee';
            const rc    = roleClass[role] ?? 'employee';
            const rl    = roleLabels[role] ?? role;
            return `<div class="store-detail-user">
                <span class="user-avatar user-avatar-${rc}" aria-hidden="true">${initials(name)}</span>
                <span class="store-detail-user-info">
                    <span class="store-detail-user-name">${name}</span>
                    <span class="store-detail-user-login">${login}</span>
                </span>
                <span class="badge badge-neutral badge-role-${rc}">${rl}</span>
            </div>`;
        }).join('');
    }

    function renderPrinters(storeId) {
        const printers = allPrinters.filter(p => String(p.storeId ?? p.StoreId ?? '') === String(storeId));
        if (!printers.length) return '<p class="store-detail-empty">Sin impresoras asignadas.</p>';
        return printers.map(p => {
            const name     = p.name ?? p.Name ?? '?';
            const failures = p.connectionFailuresStreak ?? p.ConnectionFailuresStreak ?? 0;
            const isActive = p.isActive ?? p.IsActive ?? true;
            const dot      = !isActive ? 'dot-off' : (failures === 0 ? 'dot-ok' : 'dot-warn');
            const tip      = !isActive ? 'Inactiva' : (failures === 0 ? 'Conectada' : 'Con fallos');
            return `<div class="store-detail-printer">
                <span class="store-detail-printer-dot ${dot}" title="${tip}"></span>
                <span class="store-detail-printer-name">${name}</span>
            </div>`;
        }).join('');
    }

    let selectedId = null;
    const panel    = document.getElementById('store-detail');

    function showDetail(card) {
        const storeId   = card.dataset.storeId;
        const storeName = card.dataset.storeName;
        const storeNum  = card.querySelector('.store-card-number')?.textContent?.trim() ?? '';

        document.querySelectorAll('.store-card.is-selected').forEach(c => {
            c.classList.remove('is-selected');
            c.setAttribute('aria-pressed', 'false');
        });

        if (selectedId === storeId) {
            selectedId = null;
            panel.hidden = true;
            return;
        }

        selectedId = storeId;
        card.classList.add('is-selected');
        card.setAttribute('aria-pressed', 'true');

        document.getElementById('store-detail-number').textContent = storeNum + ' — ';
        document.getElementById('store-detail-title').textContent  = storeName;
        document.getElementById('store-detail-users').innerHTML    = renderUsers(storeId);
        document.getElementById('store-detail-printers').innerHTML = renderPrinters(storeId);

        panel.hidden = false;
        panel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    document.querySelectorAll('.store-card').forEach(card => {
        card.addEventListener('click', () => showDetail(card));
        card.addEventListener('keydown', e => {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); showDetail(card); }
        });
    });

    document.getElementById('store-detail-close')?.addEventListener('click', () => {
        panel.hidden = true;
        selectedId = null;
        document.querySelectorAll('.store-card.is-selected').forEach(c => {
            c.classList.remove('is-selected');
            c.setAttribute('aria-pressed', 'false');
        });
    });
})();
</script>
@endif
@endsection
