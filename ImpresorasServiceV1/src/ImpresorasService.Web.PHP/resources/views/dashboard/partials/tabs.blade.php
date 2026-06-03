@if($isAdminView)
    <div class="dbx-tabs">
        <a href="{{ route('dashboard', ['tab' => 'global', 'window' => $window, 'showHealthy' => $showHealthy ? 1 : 0, 'autoRefresh' => $autoRefreshSeconds]) }}" class="dbx-tab {{ $tab === 'global' ? 'is-active' : '' }}">
            Vista global
        </a>
        <a href="{{ route('dashboard', ['tab' => 'stores', 'window' => $window, 'health' => $health, 'showHealthy' => $showHealthy ? 1 : 0, 'autoRefresh' => $autoRefreshSeconds]) }}" class="dbx-tab {{ $tab === 'stores' ? 'is-active' : '' }}">
            Por tiendas
        </a>
    </div>
@else
    <div class="dbx-title-row">
        <h2 class="dbx-title">Mi tienda</h2>
        <span class="dbx-subtle">Vista detallada de operacion local</span>
    </div>
@endif
