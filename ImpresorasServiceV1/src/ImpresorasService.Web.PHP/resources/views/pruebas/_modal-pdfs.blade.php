<div id="modal-pdfs" style="display:none; position:fixed; inset:0; z-index:1000;
     background:rgba(0,0,0,.45); align-items:center; justify-content:center;">
    <div class="dbx-card" style="width:min(560px,94vw); max-height:80vh;
         overflow-y:auto; padding:1.5rem; position:relative;">
        <div class="dbx-title-row" style="margin-bottom:1rem;">
            <h2 class="dbx-title" style="font-size:1.05rem;">Biblioteca de PDFs</h2>
            <button type="button" id="btn-cerrar-modal-pdfs" class="btn btn-ghost btn-sm">&#10005;</button>
        </div>

        <div style="margin-bottom:1rem;">
            <label class="btn btn-primary btn-sm" for="input-subir-pdf" style="cursor:pointer;">
                Subir PDF
            </label>
            <input type="file" id="input-subir-pdf" accept=".pdf" style="display:none;">
            <span id="pdf-upload-status" class="dbx-subtle" style="margin-left:.5rem;"></span>
        </div>

        @if(count($pdfs) === 0)
            <p class="dbx-subtle" id="pdfs-empty-msg">No hay PDFs subidos.</p>
        @endif

        <x-ui.table id="tabla-pdfs" style="{{ count($pdfs) === 0 ? 'display:none;' : '' }}">
            <thead>
                <tr>
                    <th>Nombre</th>
                    <th>Tama&ntilde;o</th>
                    <th></th>
                </tr>
            </thead>
            <tbody id="pdfs-tbody">
                @foreach($pdfs as $pdf)
                    <tr data-pdf-id="{{ $pdf['id'] }}">
                        <td>{{ $pdf['name'] }}</td>
                        <td>{{ number_format(($pdf['size'] ?? 0) / 1024, 1) }} KB</td>
                        <td>
                            <x-ui.action-buttons>
                                <button type="button" class="btn btn-ghost btn-sm btn-seleccionar-pdf"
                                        data-pdf-id="{{ $pdf['id'] }}" data-pdf-name="{{ $pdf['name'] }}">
                                    Seleccionar
                                </button>
                                <button type="button" class="btn btn-danger btn-sm btn-eliminar-pdf"
                                        data-pdf-id="{{ $pdf['id'] }}">
                                    Eliminar
                                </button>
                            </x-ui.action-buttons>
                        </td>
                    </tr>
                @endforeach
            </tbody>
        </x-ui.table>
    </div>
</div>
