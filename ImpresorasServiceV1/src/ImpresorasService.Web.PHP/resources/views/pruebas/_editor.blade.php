@php
    $scId      = $scenario['id'] ?? '';
    $scName    = $scenario['name'] ?? '';
    $scBatches = $scenario['batches'] ?? [];
    $pdfsById  = collect($pdfs ?? [])->keyBy('id')->toArray();
@endphp

<div class="dbx-title-row dbx-routing-title-row">
    <div>
        <h2 class="dbx-title">{{ $scName ?: 'Nuevo escenario' }}</h2>
        <span class="dbx-subtle">Edita los lotes y lanza la prueba</span>
    </div>
    <div class="dbx-printer-panel-tools" style="gap:.5rem;">
        <button type="button" class="btn btn-ghost" id="btn-guardar-escenario">Guardar</button>
        <button type="button" class="btn btn-primary" id="btn-lanzar-escenario">&#9654; Lanzar</button>
        @if($scId)
        <button type="button" class="btn btn-danger" id="btn-eliminar-escenario" data-id="{{ $scId }}">Eliminar</button>
        @endif
    </div>
</div>

<form id="form-escenario" data-id="{{ $scId }}">
    <div style="margin-bottom:1rem;">
        <label class="dbx-label" for="sc-nombre">Nombre del escenario</label>
        <input type="text" id="sc-nombre" class="input" name="name" value="{{ $scName }}"
               placeholder="Ej. Stress 3 tiendas" required maxlength="100" style="max-width:400px;">
    </div>

    <div style="margin-bottom:.75rem;">
        <strong style="font-size:.875rem;">Lotes</strong>
    </div>

    <x-ui.table id="tabla-lotes">
        <thead>
            <tr>
                <th>Tienda</th>
                <th>Tipo doc.</th>
                <th>Canal</th>
                <th>Cantidad</th>
                <th>PDF</th>
                <th></th>
            </tr>
        </thead>
        <tbody id="lotes-tbody">
            @foreach($scBatches as $i => $batch)
                @php
                    $pdfEntry = $pdfsById[$batch['pdfId'] ?? ''] ?? null;
                    $pdfName  = $pdfEntry ? $pdfEntry['name'] : '(sin PDF)';
                @endphp
                <tr data-lote-idx="{{ $i }}">
                    <td>
                        <select name="batches[{{ $i }}][storeId]" class="select select-sm" required>
                            <option value="">Tienda&hellip;</option>
                            @foreach($stores as $store)
                                @php
                                    $sid = $store['storeId'] ?? $store['StoreId'] ?? null;
                                    $sname = $store['name'] ?? $store['Name'] ?? '';
                                @endphp
                                <option value="{{ $sid }}"
                                    {{ (string)($batch['storeId'] ?? '') === (string)$sid ? 'selected' : '' }}>
                                    {{ \App\Helpers\StoreFormat::label($sid, $sname) }}
                                </option>
                            @endforeach
                        </select>
                    </td>
                    <td>
                        <input type="text" name="batches[{{ $i }}][documentType]" class="input input-sm"
                               value="{{ $batch['documentType'] ?? '' }}" list="doc-types-list"
                               placeholder="ALBARAN" required maxlength="50">
                    </td>
                    <td>
                        <input type="text" name="batches[{{ $i }}][channel]" class="input input-sm"
                               value="{{ $batch['channel'] ?? 'DEFAULT' }}" maxlength="50" style="width:90px;">
                    </td>
                    <td>
                        <input type="number" name="batches[{{ $i }}][count]" class="input input-sm"
                               value="{{ $batch['count'] ?? 1 }}" min="1" max="50" required style="width:70px;">
                    </td>
                    <td>
                        <button type="button" class="btn btn-ghost btn-sm btn-elegir-pdf"
                                data-pdf-id="{{ $batch['pdfId'] ?? '' }}">
                            {{ Str::limit($pdfName, 22) }}
                        </button>
                        <input type="hidden" name="batches[{{ $i }}][pdfId]" value="{{ $batch['pdfId'] ?? '' }}">
                    </td>
                    <td>
                        <button type="button" class="btn btn-danger btn-sm btn-eliminar-lote">&#10005;</button>
                    </td>
                </tr>
            @endforeach
        </tbody>
    </x-ui.table>

    <button type="button" class="btn btn-ghost btn-sm" id="btn-add-lote" style="margin-top:.5rem;">
        + A&ntilde;adir lote
    </button>
</form>

<datalist id="doc-types-list">
    @foreach($docTypes as $dt)
        <option value="{{ $dt }}">
    @endforeach
</datalist>
