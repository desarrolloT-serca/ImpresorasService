{{-- Vive en la barra superior, junto al selector de tienda: son los dos filtros de ambito y
     asi la pagina arranca directamente en el contenido. --}}
<form method="GET" class="filters-row dbx-topbar-filters" autocomplete="off" data-dashboard-filter-form>
    <div class="dbx-filter-item">
        <label for="window" class="dbx-filter-label">Periodo</label>
        <select id="window" name="window" class="select !w-auto" data-current="{{ $window }}">
            <option value="today" {{ $window === 'today' ? 'selected' : '' }}>Hoy</option>
            <option value="7d" {{ $window === '7d' ? 'selected' : '' }}>7 dias</option>
            <option value="30d" {{ $window === '30d' ? 'selected' : '' }}>30 dias</option>
        </select>
    </div>
    @if($isAdminView)
        <label class="dbx-toggle">
            <input type="hidden" name="showHealthy" value="0">
            <input type="checkbox" name="showHealthy" value="1" {{ $showHealthy ? 'checked' : '' }}>
            Incluir saludables
        </label>
    @endif
    <input type="hidden" name="tab" value="{{ $tab }}">
    <input type="hidden" name="storeView" value="{{ $storeView ?? 'estado' }}">
</form>
