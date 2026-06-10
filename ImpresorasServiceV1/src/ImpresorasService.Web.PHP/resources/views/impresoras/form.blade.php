@extends('layouts.app')

@section('title', $printer ? 'Editar impresora' : 'Nueva impresora')

@section('content')
@php
    $storeNameById = collect($storeOptions ?? [])->mapWithKeys(fn ($store) => [(string) ($store['storeId'] ?? '') => $store['name'] ?? null]);
    $printerStoreId = $printer ? ($printer['storeId'] ?? $printer['StoreId'] ?? null) : null;
    $fallbackStoreId = ($storeOptions ?? []) ? (($storeOptions[0]['storeId'] ?? null) ?: null) : null;
    $currentStoreId = old('storeId', $selectedStoreId ?? $printerStoreId ?? $fallbackStoreId ?? '');
    $currentStoreName = (string) $currentStoreId !== ''
        ? ($storeNameById[(string) $currentStoreId] ?? null)
        : null;
    $currentStoreLabel = (string) $currentStoreId !== ''
        ? \App\Helpers\StoreFormat::label($currentStoreId, $currentStoreName)
        : null;
    $cancelUrl = (string) $currentStoreId !== ''
        ? route('impresoras.index', ['storeId' => $currentStoreId])
        : route('impresoras.index');
    $formAction = $printer
        ? route('impresoras.update', $printer['printerId'] ?? $printer['PrinterId'] ?? 0)
        : route('impresoras.store');
@endphp

<div class="dbx-wrap">
<x-ui.card class="dbx-rule-form-card">
    <form method="POST" action="{{ $formAction }}" class="dbx-rule-form">
        @csrf
        @if($printer) @method('PUT') @endif

        <div class="dbx-rule-form-header">
            <div>
                <span class="dbx-rule-form-kicker">Impresoras</span>
                <h2 class="dbx-title">{{ $printer ? 'Editar impresora' : 'Nueva impresora' }}</h2>
                <p class="dbx-subtle">Configura destino, cola y conectividad.</p>
            </div>
            @if($currentStoreLabel)
                <span class="badge badge-neutral">Tienda: {{ $currentStoreLabel }}</span>
            @endif
        </div>

        <section class="dbx-rule-form-section">
            <div class="dbx-rule-form-section-title">
                <h3>Identificaci&oacute;n</h3>
                <span>Nombre operativo y tienda asociada.</span>
            </div>
            <div class="dbx-rule-form-grid">
                <div>
                    <label for="printerName" class="form-label">Nombre *</label>
                    <input type="text" name="printerName" id="printerName" value="{{ old('printerName', $printer ? ($printer['printerName'] ?? $printer['PrinterName'] ?? '') : '') }}" required
                        class="input @error('printerName') border-red-500 @enderror">
                    @error('printerName')<p class="field-error">{{ $message }}</p>@enderror
                </div>

                <div>
                    <label for="storeId" class="form-label">Tienda *</label>
                    @if($storeIdLocked ?? false)
                        <input type="hidden" id="storeId" name="storeId" value="{{ $effectiveStoreId ?? $currentStoreId }}">
                        <input type="text" id="storeIdDisplay" value="{{ \App\Helpers\StoreFormat::label($effectiveStoreId ?? $currentStoreId, $storeNameById[(string) ($effectiveStoreId ?? $currentStoreId ?? '')] ?? null) }}" disabled class="input opacity-70 cursor-not-allowed">
                        <p class="form-hint">Fijado a tu tienda.</p>
                    @else
                        <select name="storeId" id="storeId" required class="select @error('storeId') border-red-500 @enderror">
                            <option value="">Seleccionar tienda...</option>
                            @foreach($storeOptions ?? [] as $store)
                                <option value="{{ $store['storeId'] }}" {{ (string) $currentStoreId === (string) $store['storeId'] ? 'selected' : '' }}>
                                    {{ \App\Helpers\StoreFormat::label($store['storeId'], $store['name']) }}
                                </option>
                            @endforeach
                        </select>
                        @error('storeId')<p class="field-error">{{ $message }}</p>@enderror
                    @endif
                </div>
            </div>
        </section>

        <section class="dbx-rule-form-section">
            <div class="dbx-rule-form-section-title">
                <h3>Configuraci&oacute;n de cola</h3>
                <span>Ruta de spool y host para la comprobaci&oacute;n.</span>
            </div>
            <div class="dbx-rule-form-grid">
                <div>
                    <label for="spoolQueue" class="form-label">SpoolQueue *</label>
                    <input type="text" name="spoolQueue" id="spoolQueue" value="{{ old('spoolQueue', $printer ? ($printer['spoolQueue'] ?? $printer['SpoolQueue'] ?? '') : '') }}" required
                        class="input @error('spoolQueue') border-red-500 @enderror">
                    @error('spoolQueue')<p class="field-error">{{ $message }}</p>@enderror
                </div>

                <div>
                    <label for="host" class="form-label">Host</label>
                    <input type="text" name="host" id="host" value="{{ old('host', $printer ? ($printer['host'] ?? $printer['Host'] ?? '') : '') }}"
                        class="input @error('host') border-red-500 @enderror">
                    <p class="form-hint">Usado para comprobar conectividad SMB cuando SpoolQueue no es UNC.</p>
                    @error('host')<p class="field-error">{{ $message }}</p>@enderror
                </div>
            </div>
        </section>

        <section class="dbx-rule-form-section">
            <div class="dbx-rule-form-section-title">
                <h3>Estado</h3>
                <span>Disponibilidad de la impresora en la operativa.</span>
            </div>
            <div class="dbx-rule-form-grid">
                <div class="dbx-rule-active-field">
                    <label class="dbx-toggle">
                        <input type="hidden" name="isActive" value="0">
                        <input type="checkbox" name="isActive" value="1" {{ old('isActive', $printer ? ($printer['isActive'] ?? $printer['IsActive'] ?? true) : true) ? 'checked' : '' }}>
                        Activa
                    </label>
                    @error('isActive')<p class="field-error">{{ $message }}</p>@enderror
                </div>
            </div>
        </section>

        <div class="dbx-rule-form-actions">
            <a href="{{ $cancelUrl }}" class="btn btn-ghost">Cancelar</a>
            <button type="submit" class="btn btn-primary">{{ $printer ? 'Guardar' : 'Crear' }}</button>
        </div>
    </form>
</x-ui.card>
</div>
@endsection
