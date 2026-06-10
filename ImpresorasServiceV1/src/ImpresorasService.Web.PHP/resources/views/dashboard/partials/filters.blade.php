<x-ui.card>
    <form method="GET" class="filters-row" autocomplete="off">
        <x-ui.toolbar>
        <div class="dbx-filters">
            <div class="dbx-filter-item">
                <label for="window" class="dbx-filter-label">Periodo</label>
                <select id="window" name="window" class="select !w-auto" data-current="{{ $window }}">
                    <option value="today" {{ $window === 'today' ? 'selected' : '' }}>Hoy</option>
                    <option value="7d" {{ $window === '7d' ? 'selected' : '' }}>7 dias</option>
                    <option value="30d" {{ $window === '30d' ? 'selected' : '' }}>30 dias</option>
                </select>
            </div>
            @if($isAdminView)
                <div class="dbx-filter-item">
                    <label for="health" class="dbx-filter-label">Salud tienda</label>
                    <select id="health" name="health" class="select !w-auto" data-current="{{ $health }}">
                        <option value="all" {{ $health === 'all' ? 'selected' : '' }}>Todas</option>
                        <option value="healthy" {{ $health === 'healthy' ? 'selected' : '' }}>Saludable</option>
                        <option value="warning" {{ $health === 'warning' ? 'selected' : '' }}>Advertencia</option>
                        <option value="critical" {{ $health === 'critical' ? 'selected' : '' }}>Cr&iacute;tica</option>
                    </select>
                </div>
            @endif
            @if($isAdminView)
                <div class="dbx-filter-item">
                    <label for="autoRefresh" class="dbx-filter-label">Auto refresh</label>
                    <select id="autoRefresh" name="autoRefresh" class="select !w-auto" data-current="{{ $autoRefreshSeconds }}">
                        <option value="0" {{ $autoRefreshSeconds === 0 ? 'selected' : '' }}>Off</option>
                        <option value="15" {{ $autoRefreshSeconds === 15 ? 'selected' : '' }}>15s</option>
                        <option value="30" {{ $autoRefreshSeconds === 30 ? 'selected' : '' }}>30s</option>
                        <option value="60" {{ $autoRefreshSeconds === 60 ? 'selected' : '' }}>60s</option>
                        <option value="120" {{ $autoRefreshSeconds === 120 ? 'selected' : '' }}>120s</option>
                    </select>
                </div>
                <div class="dbx-filter-item">
                    <label for="storeSort" class="dbx-filter-label">Orden</label>
                    <select id="storeSort" name="storeSort" class="select !w-auto" data-current="{{ $storeSort }}">
                        <option value="severity" {{ $storeSort === 'severity' ? 'selected' : '' }}>Severidad</option>
                        <option value="queue" {{ $storeSort === 'queue' ? 'selected' : '' }}>Cola</option>
                        <option value="name" {{ $storeSort === 'name' ? 'selected' : '' }}>Nombre</option>
                    </select>
                </div>
                <div class="dbx-filter-item">
                    <label for="storeCols" class="dbx-filter-label">Tiendas por fila</label>
                    <select id="storeCols" name="storeCols" class="select !w-auto" data-current="{{ $storeCols }}">
                        <option value="2" {{ $storeCols === 2 ? 'selected' : '' }}>2</option>
                        <option value="3" {{ $storeCols === 3 ? 'selected' : '' }}>3</option>
                        <option value="4" {{ $storeCols === 4 ? 'selected' : '' }}>4</option>
                    </select>
                </div>
                <label class="dbx-toggle">
                    <input type="hidden" name="showHealthy" value="0">
                    <input type="checkbox" name="showHealthy" value="1" {{ $showHealthy ? 'checked' : '' }}>
                    Incluir saludables
                </label>
            @endif
            <input type="hidden" name="tab" value="{{ $tab }}">
            <input type="hidden" name="storeView" value="{{ $storeView ?? 'estado' }}">
        </div>
        </x-ui.toolbar>
    </form>
</x-ui.card>
