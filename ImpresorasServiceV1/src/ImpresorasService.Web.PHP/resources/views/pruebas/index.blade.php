@extends('layouts.app')

@section('title', 'Pruebas')

@section('content')
@php
    $scenarios = is_array($scenarios ?? null) ? $scenarios : [];
    $pdfs      = is_array($pdfs ?? null) ? $pdfs : [];
    $stores    = is_array($stores ?? null) ? $stores : [];
    $docTypes  = is_array($docTypes ?? null) ? $docTypes : [];
    $activeId  = request('scenario');
    $active    = collect($scenarios)->firstWhere('id', $activeId);
@endphp

<div class="dbx-wrap">
<section class="dbx-routing-layout">

    {{-- Panel izquierdo: lista de escenarios --}}
    <x-ui.card class="dbx-routing-stores-card" style="display:flex; flex-direction:column;">
        <div class="dbx-title-row">
            <h2 class="dbx-title">Escenarios</h2>
            <a href="{{ route('pruebas.index') }}" class="btn btn-primary btn-sm">+ Nuevo</a>
        </div>

        @if(count($scenarios) === 0)
            <p class="dbx-subtle">Sin escenarios. Crea uno con el bot&oacute;n.</p>
        @else
            <div class="dbx-routing-store-list">
                @foreach($scenarios as $sc)
                    <a href="{{ route('pruebas.index', ['scenario' => $sc['id']]) }}"
                       class="dbx-routing-store-link {{ ($activeId === $sc['id']) ? 'is-active' : '' }}"
                       data-scenario-id="{{ $sc['id'] }}">
                        <span class="dbx-routing-store-name">{{ $sc['name'] }}</span>
                        <span class="dbx-routing-store-meta">{{ count($sc['batches'] ?? []) }} lote(s)</span>
                    </a>
                @endforeach
            </div>
        @endif

        <div style="margin-top:auto; padding-top:1rem; border-top:1px solid var(--ui-border,#e2e8f0);">
            <button type="button" class="btn btn-ghost btn-sm" id="btn-gestionar-pdfs">
                Gestionar PDFs ({{ count($pdfs) }})
            </button>
        </div>
    </x-ui.card>

    {{-- Panel derecho --}}
    <x-ui.card class="dbx-routing-rules-card">

        {{-- ZONA A: Editor --}}
        <div id="pruebas-editor">
            @if($active)
                @include('pruebas._editor', [
                    'scenario' => $active,
                    'stores'   => $stores,
                    'docTypes' => $docTypes,
                    'pdfs'     => $pdfs,
                ])
            @else
                <div class="dbx-empty-state">
                    Selecciona un escenario o crea uno nuevo con el bot&oacute;n + Nuevo.
                </div>
                <form id="form-escenario" data-id="">
                    <div style="margin-bottom:1rem; margin-top:1.5rem;">
                        <label class="dbx-label" for="sc-nombre">Nombre del escenario</label>
                        <input type="text" id="sc-nombre" class="input" name="name" value=""
                               placeholder="Ej. Stress 3 tiendas" required maxlength="100" style="max-width:400px;">
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
                        <tbody id="lotes-tbody"></tbody>
                    </x-ui.table>
                    <button type="button" class="btn btn-ghost btn-sm" id="btn-add-lote" style="margin-top:.5rem;">
                        + A&ntilde;adir lote
                    </button>
                    <div style="margin-top:1rem; display:flex; gap:.5rem;">
                        <button type="button" class="btn btn-ghost" id="btn-guardar-escenario">Guardar</button>
                        <button type="button" class="btn btn-primary" id="btn-lanzar-escenario">&#9654; Lanzar</button>
                    </div>
                </form>
                <datalist id="doc-types-list">
                    @foreach($docTypes as $dt)
                        <option value="{{ $dt }}">
                    @endforeach
                </datalist>
            @endif
        </div>

        {{-- ZONA B: Resultados en vivo --}}
        <div id="pruebas-resultados" style="display:none; margin-top:1.5rem;">
            <div class="dbx-title-row" style="margin-bottom:.75rem;">
                <h3 class="dbx-title" style="font-size:1rem;">Resultados en vivo</h3>
                <span id="pruebas-resultado-resumen" class="dbx-subtle"></span>
            </div>
            <x-ui.table>
                <thead>
                    <tr>
                        <th>Job ID</th>
                        <th>Estado</th>
                        <th>Impresora</th>
                    </tr>
                </thead>
                <tbody id="pruebas-jobs-tbody"></tbody>
            </x-ui.table>
        </div>

    </x-ui.card>

</section>
</div>

@include('pruebas._modal-pdfs', ['pdfs' => $pdfs])

@endsection

@section('page_scripts')
<script>
@include('pruebas._script', [
    'stores'   => $stores,
    'docTypes' => $docTypes,
])
</script>
@endsection
