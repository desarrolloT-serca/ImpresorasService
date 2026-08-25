@if($isAdminView)
    <div class="dbx-tabs">
        <a href="{{ route('dashboard', ['tab' => 'global', 'storeView' => $storeView ?? 'estado', 'window' => $window, 'showHealthy' => $showHealthy ? 1 : 0]) }}" class="dbx-tab is-active">
            Vista global
        </a>
    </div>
@else
    <div class="dbx-title-row">
        <h2 class="dbx-title">Mi tienda</h2>
        <span class="dbx-subtle">Vista detallada de operacion local</span>
    </div>
@endif
